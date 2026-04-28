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

public class GoogleLoginController : Controller
{
    private readonly SignInManager<CognitoUser> _signInManager;
    private readonly AppDbContext _dbContext;

    public GoogleLoginController(SignInManager<CognitoUser> signInManager, AppDbContext dbContext)
    {
        _signInManager = signInManager;
        _dbContext = dbContext;
    }

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

    private static string NormalizeAuthMode(string? mode)
    {
        return string.Equals(mode, "register", StringComparison.OrdinalIgnoreCase)
            ? "register"
            : "login";
    }

    private IActionResult RedirectByRole(string role)
    {
        if (role == "Admin") return RedirectToAction("Dashboard", "Admin");
        if (role == "Landlord") return RedirectToAction("Dashboard", "Landlord");
        return RedirectToAction("Dashboard", "Tenant");
    }
}
