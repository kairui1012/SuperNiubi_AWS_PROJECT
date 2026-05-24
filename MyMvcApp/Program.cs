using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Extensions;
using MyMvcApp.Services;
using Microsoft.AspNetCore.Authentication;
using QuestPDF.Infrastructure;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Stripe;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Amazon.XRay.Recorder.Core;
using Microsoft.AspNetCore.Http;
using System.Text.Json.Serialization;
using Amazon.SecretsManager;


QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Enable AWS SDK calls to appear in X-Ray traces.
AWSSDKHandler.RegisterXRayForAllServices();

// Configure Stripe once so payment services can use the shared API key.
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// MVC controllers and JSON serialization.
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

if (builder.Configuration.GetValue<bool>("EnableForwardedHeaders"))
{
    // Trust proxy headers from Nginx or an AWS load balancer.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(3),
                errorCodesToAdd: null))
        .AddXRayInterceptor(true));

// Application services used by controllers and background-style workflows.
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<StripeEventBridgeProcessingService>();
builder.Services.AddScoped<DocumentUploadService>();
builder.Services.AddScoped<InternalApiKeyProvider>();

// Add role claims after sign-in so authorization policies see the current role.
builder.Services.AddScoped<IClaimsTransformation, MyMvcApp.Services.RoleClaimsTransformation>();

// Register AWS options before AWS-backed services so they share region and credentials.
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());

builder.Services.AddCognitoIdentity();
builder.Services.AddGoogleLogin(builder.Configuration);
builder.Services.ConfigureApplicationCookie(options =>
{
    // Return status codes for API/Ajax requests instead of redirecting to HTML pages.
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Events.OnRedirectToLogin = context =>
    {
        if (IsJsonOrAjaxRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (IsJsonOrAjaxRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.ConfigureExternalCookie(options =>
{
    // External sign-in callbacks require cross-site cookies.
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddAWSService<Amazon.CognitoIdentityProvider.IAmazonCognitoIdentityProvider>();

builder.Services.AddAWSService<Amazon.S3.IAmazonS3>();
builder.Services.AddAWSService<IAmazonSecretsManager>();

// S3 image storage for property and maintenance uploads.
builder.Services.AddScoped<MyMvcApp.Services.IS3ImageService, MyMvcApp.Services.S3ImageService>();

// Persist DataProtection keys so auth cookies survive restarts and multiple instances.
var dataProtectionKeysPath = Environment.GetEnvironmentVariable("DATAPROTECTION_KEYS_PATH") 
    ?? (builder.Environment.IsDevelopment() 
        ? Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys")
        : "/var/propease/dataprotection-keys");

Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("PropEase");

// Process reverse-proxy headers for client IP and original scheme.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Configure the main Identity cookie used by MVC authentication.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.LogoutPath = "/Account/Logout";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);

    // Keep the auth cookie locked to HTTPS and unavailable to client-side scripts.
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.Cookie.Name = ".AspNetCore.Identity.Application";
    options.Events.OnRedirectToLogin = context =>
    {
        if (IsJsonOrAjaxRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (IsJsonOrAjaxRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("EnableForwardedHeaders"))
{
    // Apply proxy headers before the request reaches the main pipeline.
    app.UseForwardedHeaders();
}

app.UseXRay("MyMvcApp");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Forwarded headers must run before HTTPS redirection and authentication.
app.UseForwardedHeaders();

// In containerized HTTP deployments (e.g. direct EC2), keep HTTPS redirection optional.
if (builder.Configuration.GetValue<bool>("EnableHttpsRedirection"))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

// Log authentication state on selected paths to troubleshoot cookie and role issues.
app.Use(async (context, next) =>
{
    var machine = Environment.MachineName;
    var processId = Environment.ProcessId;
    var path = context.Request.Path;
    var host = context.Request.Host.ToString();
    var scheme = context.Request.Scheme;
    var isAuth = context.User.Identity?.IsAuthenticated ?? false;
    var name = context.User.Identity?.Name ?? "anonymous";
    var hasIdentityCookie = context.Request.Cookies.ContainsKey(".AspNetCore.Identity.Application");
    var roles = string.Join(",", context.User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value));

    if (path.StartsWithSegments("/Account/CheckAuth") || 
        path.StartsWithSegments("/Admin") ||
        path.StartsWithSegments("/Account/Login"))
    {
        Console.WriteLine(
            $"[AUTH_DEBUG] Machine={machine} | PID={processId} | Host={host} | Scheme={scheme} | Path={path} | " +
            $"IsAuth={isAuth} | Name={name} | HasIdentityCookie={hasIdentityCookie} | Roles={roles}"
        );
    }

    await next();
});

app.UseAuthorization();

app.UseMiddleware<MyMvcApp.Middlewares.XRayUserTrackingMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static bool IsJsonOrAjaxRequest(HttpRequest request)
{
    // Reuse one check for endpoints that should receive JSON-friendly auth errors.
    return request.Headers.Accept.Any(value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true) ||
           string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
