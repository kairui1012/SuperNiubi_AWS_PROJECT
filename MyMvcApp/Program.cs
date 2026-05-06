using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Extensions;
using MyMvcApp.Services;
using Microsoft.AspNetCore.Authentication;
using QuestPDF.Infrastructure; 
using Stripe;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Amazon.XRay.Recorder.Core;


QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

AWSSDKHandler.RegisterXRayForAllServices();

StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// Add services to the container.
builder.Services.AddControllersWithViews();

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

var app = builder.Build();

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
