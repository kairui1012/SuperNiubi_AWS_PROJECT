using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using QuestPDF.Infrastructure; 

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Email Service
builder.Services.AddScoped<EmailService>();

builder.Services.AddScoped<IClaimsTransformation, MyMvcApp.Services.RoleClaimsTransformation>();

builder.Services.AddCognitoIdentity();

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            options.SaveTokens = true;
        });
}

builder.Services.AddAWSService<Amazon.CognitoIdentityProvider.IAmazonCognitoIdentityProvider>();

// 1. Add Amazon AWS setup
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<Amazon.S3.IAmazonS3>();

// 2. Register your custom S3 Image Service
builder.Services.AddScoped<MyMvcApp.Services.IS3ImageService, MyMvcApp.Services.S3ImageService>();

var app = builder.Build();

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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
