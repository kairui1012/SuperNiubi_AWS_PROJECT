using Amazon.AspNetCore.Identity.Cognito;
using Amazon.Extensions.CognitoAuthentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using MyMvcApp.Models;
using MyMvcApp.Data;

namespace MyMvcApp.Controllers
{
    /// <summary>
    /// Handles local login, registration, logout, and password reset requests.
    /// </summary>
    public class AccountController : Controller
    {
        /// <summary>
        /// Handles application cookie sign-in and sign-out for Cognito users.
        /// </summary>
        private readonly SignInManager<CognitoUser> _signInManager;

        /// <summary>
        /// Creates and manages Cognito user accounts.
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
        public AccountController(SignInManager<CognitoUser> signInManager, UserManager<CognitoUser> userManager, CognitoUserPool pool, AppDbContext dbContext) 
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _pool = pool;
            _dbContext = dbContext;
        }

        /// <summary>
        /// Shows the login or registration form based on the requested mode.
        /// </summary>
        [HttpGet]
        public IActionResult Login(string? mode = null)
        {
            ViewBag.AuthMode = string.Equals(mode, "register", StringComparison.OrdinalIgnoreCase)
                ? "register"
                : "login";
            return View();
        }

        /// <summary>
        /// Signs in an approved and enabled user through Cognito.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Look up the local application user before attempting Cognito sign-in.
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
                    return RedirectToAction(nameof(PendingApproval));
                }

                try 
                {
                    // Sign in to Cognito using the email address as the username.
                    var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, false);

                    if (result.Succeeded)
                    {
                        if (appUser.Role == "Admin") return RedirectToAction("Dashboard", "Admin");
                        if (appUser.Role == "Landlord") return RedirectToAction("Dashboard", "Landlord");
                        if (appUser.Role == "Security") return RedirectToAction(nameof(TenantController.ValidateVisitorPass), "Tenant");
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

        /// <summary>
        /// Shows the pending approval page for users waiting for admin approval.
        /// </summary>
        [HttpGet]
        public IActionResult PendingApproval()
        {
            return View();
        }

        /// <summary>
        /// Shows the access denied page when a user cannot access a resource.
        /// </summary>
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        /// <summary>
        /// Creates a pending password reset request for admin review.
        /// </summary>
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

        /// <summary>
        /// Redirects the old register route to the combined login/register page.
        /// </summary>
        [HttpGet]
        public IActionResult Register() => RedirectToAction(nameof(Login), new { mode = "register" });

        /// <summary>
        /// Registers a new Cognito user and creates a pending local application user.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Create the Cognito user using email as the login identifier.
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

                    return RedirectToAction(nameof(PendingApproval));
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

        /// <summary>
        /// Signs out the current user and returns them to the login page.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login), "Account");
        }

        /// <summary>
        /// Returns basic authentication and claims information for diagnostics.
        /// </summary>
        [HttpGet]
        public IActionResult CheckAuth()
        {
            var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
            return Json(new
            {
                IsAuthenticated = User.Identity?.IsAuthenticated ?? false,
                Name = User.Identity?.Name,
                AuthenticationType = User.Identity?.AuthenticationType,
                Claims = claims
            });
        }

    }
}
