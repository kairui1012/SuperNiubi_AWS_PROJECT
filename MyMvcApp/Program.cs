using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Extensions;
using MyMvcApp.Services;
using Microsoft.AspNetCore.Authentication;
using QuestPDF.Infrastructure;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Stripe;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Amazon.XRay.Recorder.Core;
using Microsoft.AspNetCore.Http;


QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

AWSSDKHandler.RegisterXRayForAllServices();

StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// Add services to the container.
builder.Services.AddControllersWithViews();

if (builder.Configuration.GetValue<bool>("EnableForwardedHeaders"))
{
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
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .AddXRayInterceptor(true));

// Add Email Service
builder.Services.AddScoped<EmailService>();

builder.Services.AddScoped<IClaimsTransformation, MyMvcApp.Services.RoleClaimsTransformation>();

// Register AWS options before AWS-backed services so Cognito, S3, and SES use the same configured region/credentials chain.
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());

builder.Services.AddCognitoIdentity();
builder.Services.AddGoogleLogin(builder.Configuration);

builder.Services.AddAWSService<Amazon.CognitoIdentityProvider.IAmazonCognitoIdentityProvider>();

builder.Services.AddAWSService<Amazon.S3.IAmazonS3>();

// 2. Register your custom S3 Image Service
builder.Services.AddScoped<MyMvcApp.Services.IS3ImageService, MyMvcApp.Services.S3ImageService>();

// ========== CRITICAL: DataProtection Key Persistence ==========
// Persist DataProtection keys to a shared, persistent location.
// In production, this MUST be on shared storage (EFS, shared volume) if you have multiple instances.
// Otherwise, each instance will have different keys and cookies won't be recognized across instances.
var dataProtectionKeysPath = Environment.GetEnvironmentVariable("DATAPROTECTION_KEYS_PATH") 
    ?? (builder.Environment.IsDevelopment() 
        ? Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys")
        : "/var/propease/dataprotection-keys");

Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("PropEase");

// ========== Forwarded Headers Configuration ==========
// REQUIRED when behind a reverse proxy (Nginx, ALB, etc).
// This ensures X-Forwarded-Proto, X-Forwarded-For headers are processed correctly.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ========== Identity Application Cookie Configuration ==========
// Explicitly configure the Identity Application Cookie to ensure it's recognized across requests.
// This is crucial when the app sits behind a reverse proxy or load balancer.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.LogoutPath = "/Account/Logout";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);

    // Cookie security settings - important for HTTPS deployments
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.Cookie.Name = ".AspNetCore.Identity.Application";
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("EnableForwardedHeaders"))
{
    app.UseForwardedHeaders();
}

app.UseXRay("MyMvcApp"); // The string here is the name that will appear in the X-Ray console

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// ========== CRITICAL: ForwardedHeaders Middleware MUST come FIRST ==========
// This must be before UseHttpsRedirection, UseAuthentication, etc.
// It processes X-Forwarded-Proto header to determine if request is HTTPS
app.UseForwardedHeaders();

// In containerized HTTP deployments (e.g. direct EC2), keep HTTPS redirection optional.
if (builder.Configuration.GetValue<bool>("EnableHttpsRedirection"))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

// ========== DEBUG MIDDLEWARE: Log authentication state for troubleshooting ==========
// This helps identify whether cookie decryption is working correctly across requests.
// Only logs specific paths to reduce noise.
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

    // Only log specific paths to reduce noise
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
