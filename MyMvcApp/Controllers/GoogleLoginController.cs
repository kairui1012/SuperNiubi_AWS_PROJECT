using Amazon.AspNetCore.Identity.Cognito;
using Amazon.Extensions.CognitoAuthentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using System.Security.Claims;

namespace MyMvcApp.Controllers;

/// <summary>
/// Handles Google external authentication and maps approved users into the local application session.
/// </summary>
public class GoogleLoginController : Controller
{
    /// <summary>
    /// Starts and completes external authentication sign-in operations.
    /// </summary>
    private readonly SignInManager<CognitoUser> _signInManager;

    /// <summary>
    /// Creates Cognito users for first-time Google sign-ins.
    /// </summary>
    private readonly UserManager<CognitoUser> _userManager;

    /// <summary>
    /// Provides access to the configured Cognito user pool.
    /// </summary>
    private readonly CognitoUserPool _pool;

    /// <summary>
    /// Provides access to local application user records.
    /// </summary>
    private readonly AppDbContext _dbContext;

    /// <summary>
    /// Creates a controller instance with Cognito identity and local user data services.
    /// </summary>
    public GoogleLoginController(
        SignInManager<CognitoUser> signInManager, 
        UserManager<CognitoUser> userManager, 
        CognitoUserPool pool, 
        AppDbContext dbContext)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _pool = pool;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Starts the Google external login challenge.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExternalLogin(string? returnUrl = null, string? mode = null)
    {
        var provider = GoogleDefaults.AuthenticationScheme;
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        var selectedScheme = schemes.FirstOrDefault(s => s.Name == provider);
        var authMode = NormalizeAuthMode(mode);

        if (selectedScheme == null)
        {
            TempData["ErrorMessage"] = "Google login is not configured yet.";
            return RedirectToAction(nameof(AccountController.Login), "Account", new { mode = authMode });
        }

        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "GoogleLogin", new
        {
            returnUrl,
            mode = authMode
        });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);

        return Challenge(properties, provider);
    }

    /// <summary>
    /// Completes Google sign-in, creates a pending local user when needed, and signs in approved users.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null, string? mode = null)
    {
        var authMode = NormalizeAuthMode(mode);

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            TempData["ErrorMessage"] = $"Google login failed: {remoteError}";
            return RedirectToAction(nameof(AccountController.Login), "Account", new { mode = authMode });
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            TempData["ErrorMessage"] = "Google login could not be completed.";
            return RedirectToAction(nameof(AccountController.Login), "Account", new { mode = authMode });
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["ErrorMessage"] = "Google did not provide an email address.";
            return RedirectToAction(nameof(AccountController.Login), "Account", new { mode = authMode });
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var appUser = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        if (appUser == null)
        {
            // Create the identity provider user before adding the local pending account.
            var cognitoUser = _pool.GetUser(normalizedEmail);
            cognitoUser.Attributes.Add("email", normalizedEmail);
            
            // Generate a strong random password to satisfy Cognito requirements.
            // The user will never actually use this password since they login via Google.
            var randomPassword = Guid.NewGuid().ToString("N") + "Aa1!";
            var result = await _userManager.CreateAsync(cognitoUser, randomPassword);

            // Ignore duplicate Cognito users but surface all other identity provider errors.
            if (!result.Succeeded && !result.Errors.Any(e => e.Code == "UsernameExistsException" || e.Description.Contains("already exists")))
            {
                TempData["ErrorMessage"] = "Failed to create user in identity provider.";
                return RedirectToAction(nameof(AccountController.Login), "Account", new { mode = authMode });
            }

            // Create the local application user in a pending approval state.
            _dbContext.Users.Add(new AppUser
            {
                Email = normalizedEmail,
                IsApproved = false,
                Role = "Tenant",
                CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();

            return RedirectToAction(nameof(AccountController.PendingApproval), "Account");
        }

        if (appUser.IsDisabled)
        {
            TempData["ErrorMessage"] = "Your account has been disabled by the administrator.";
            return RedirectToAction(nameof(AccountController.Login), "Account", new { mode = authMode });
        }

        if (!appUser.IsApproved)
        {
            return RedirectToAction(nameof(AccountController.PendingApproval), "Account");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, normalizedEmail),
            new(ClaimTypes.Name, normalizedEmail),
            new(ClaimTypes.Email, normalizedEmail),
            new("email", normalizedEmail)
        };

        var displayName = info.Principal.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            claims.Add(new Claim("display_name", displayName));
        }

        var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme, ClaimTypes.Name, ClaimTypes.Role);
        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, new ClaimsPrincipal(identity));

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectByRole(appUser.Role);
    }

    /// <summary>
    /// Normalizes the requested authentication mode to either login or register.
    /// </summary>
    private static string NormalizeAuthMode(string? mode)
    {
        return string.Equals(mode, "register", StringComparison.OrdinalIgnoreCase)
            ? "register"
            : "login";
    }

    /// <summary>
    /// Redirects an approved user to the dashboard that matches their application role.
    /// </summary>
    private IActionResult RedirectByRole(string role)
    {
        if (role == "Admin") return RedirectToAction("Dashboard", "Admin");
        if (role == "Landlord") return RedirectToAction("Dashboard", "Landlord");
        if (role == "Security") return RedirectToAction(nameof(TenantController.ValidateVisitorPass), "Tenant");
        return RedirectToAction("Dashboard", "Tenant");
    }
}
