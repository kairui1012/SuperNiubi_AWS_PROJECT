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
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.ConfigureExternalCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddAWSService<Amazon.CognitoIdentityProvider.IAmazonCognitoIdentityProvider>();

builder.Services.AddAWSService<Amazon.S3.IAmazonS3>();

// 2. Register your custom S3 Image Service
builder.Services.AddScoped<MyMvcApp.Services.IS3ImageService, MyMvcApp.Services.S3ImageService>();

var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("ProPease");

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    Directory.CreateDirectory(dataProtectionKeysPath);
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

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

// In containerized HTTP deployments (e.g. direct EC2), keep HTTPS redirection optional.
if (builder.Configuration.GetValue<bool>("EnableHttpsRedirection"))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication(); 
app.UseAuthorization();

app.UseMiddleware<MyMvcApp.Middlewares.XRayUserTrackingMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
