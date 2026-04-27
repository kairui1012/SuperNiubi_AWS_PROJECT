using Amazon.AspNetCore.Identity.Cognito;
using Amazon.Extensions.CognitoAuthentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Models;
using MyMvcApp.Data; // ADD THIS
using System.Security.Claims;

namespace MyMvcApp.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<CognitoUser> _signInManager;
        private readonly UserManager<CognitoUser> _userManager;
        private readonly CognitoUserPool _pool; 
        private readonly AppDbContext _dbContext; // ADD THIS

        public AccountController(SignInManager<CognitoUser> signInManager, UserManager<CognitoUser> userManager, CognitoUserPool pool, AppDbContext dbContext) 
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _pool = pool;
            _dbContext = dbContext;
        }

        // --- LOGIN ---
        [HttpGet]
        public IActionResult Login(string? mode = null)
        {
            ViewBag.AuthMode = string.Equals(mode, "register", StringComparison.OrdinalIgnoreCase)
                ? "register"
                : "login";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                // 1. LOOKUP BY EMAIL NOW
                var appUser = _dbContext.Users.FirstOrDefault(u => u.Email.ToLower() == model.Email.ToLower());
                
                if (appUser == null) {
                    ViewBag.AuthMode = "login";
                    ViewBag.LoginError = "User does not exist.";
                    return View(model);
                }

                if (appUser.IsDisabled) {
                    ViewBag.AuthMode = "login";
                    ViewBag.LoginError = "Your account has been disabled by the administrator.";
                    return View(model);
                }

                if (!appUser.IsApproved) {
                    ViewBag.AuthMode = "login";
                    ViewBag.LoginError = "Your account is pending Admin approval.";
                    return View(model);
                }

                try 
                {
                    // 2. SIGN IN TO COGNITO USING EMAIL
                    var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, false);

                    if (result.Succeeded)
                    {
                        if (appUser.Role == "Admin") return RedirectToAction("Dashboard", "Admin");
                        if (appUser.Role == "Landlord") return RedirectToAction("Dashboard", "Landlord");
                        return RedirectToAction("Dashboard", "Tenant");
                    }
                    ViewBag.LoginError = "Invalid password.";
                }
                catch (Amazon.CognitoIdentityProvider.Model.UserNotConfirmedException)
                {
                    ViewBag.LoginError = "Your account has not been confirmed by the administrator yet.";
                }
            }
            ViewBag.AuthMode = "login";
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExternalLogin(string provider, string? returnUrl = null, string? mode = null)
        {
            var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
            var selectedScheme = schemes.FirstOrDefault(s => s.Name == provider);

            if (selectedScheme == null)
            {
                TempData["ErrorMessage"] = $"{provider} login is not configured yet.";
                return RedirectToAction(nameof(Login), new { mode = NormalizeAuthMode(mode) });
            }

            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new
            {
                returnUrl,
                mode = NormalizeAuthMode(mode)
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
                return RedirectToAction(nameof(Login), new { mode = authMode });
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                TempData["ErrorMessage"] = "Google login could not be completed.";
                return RedirectToAction(nameof(Login), new { mode = authMode });
            }

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Google did not provide an email address.";
                return RedirectToAction(nameof(Login), new { mode = authMode });
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

                TempData["SuccessMessage"] = "Google registration successful! Please wait for admin approval.";
                return RedirectToAction(nameof(Login));
            }

            if (appUser.IsDisabled)
            {
                TempData["ErrorMessage"] = "Your account has been disabled by the administrator.";
                return RedirectToAction(nameof(Login), new { mode = authMode });
            }

            if (!appUser.IsApproved)
            {
                TempData["ErrorMessage"] = "Your account is pending Admin approval.";
                return RedirectToAction(nameof(Login), new { mode = authMode });
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, normalizedEmail),
                new(ClaimTypes.Name, normalizedEmail),
                new(ClaimTypes.Email, normalizedEmail)
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

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestPasswordReset(string? email)
        {
            var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(normalizedEmail))
            {
                var appUser = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

                if (appUser != null && !appUser.IsDisabled)
                {
                    var hasPendingRequest = await _dbContext.PasswordResetRequests
                        .AnyAsync(r => r.Email.ToLower() == normalizedEmail
                            && r.Status == PasswordResetRequestStatus.Pending);

                    if (!hasPendingRequest)
                    {
                        _dbContext.PasswordResetRequests.Add(new PasswordResetRequest
                        {
                            Email = appUser.Email,
                            AppUserId = appUser.Id,
                            Status = PasswordResetRequestStatus.Pending,
                            RequestedAt = DateTime.UtcNow
                        });

                        await _dbContext.SaveChangesAsync();
                    }
                }
            }

            TempData["SuccessMessage"] = "If the email is registered, your password reset request has been sent to the administrator.";
            return RedirectToAction(nameof(Login));
        }

        // --- REGISTER ---
        [HttpGet]
        public IActionResult Register() => RedirectToAction(nameof(Login), new { mode = "register" });

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                // CREATE COGNITO USER USING EMAIL AS THE IDENTIFIER
                var user = _pool.GetUser(model.Email); 
                user.Attributes.Add("email", model.Email);
                
                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    _dbContext.Users.Add(new AppUser {
                        Email = model.Email,
                        IsApproved = false,
                        Role = "Tenant",
                        CreatedAt = DateTime.UtcNow
                    });
                    await _dbContext.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Registration successful! Please wait for admin approval.";
                    return RedirectToAction(nameof(Login));
                }
                ViewBag.RegisterError = result.Errors.FirstOrDefault()?.Description;
            }

            ViewBag.AuthMode = "register";
            var loginModel = new LoginViewModel
            {
                Email = model.Email ?? string.Empty,
                Password = model.Password ?? string.Empty,
                RememberMe = false
            };

            return View(nameof(Login), loginModel);
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login), "Account");
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
}
