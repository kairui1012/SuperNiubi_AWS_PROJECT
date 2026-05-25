using Amazon.AspNetCore.Identity.Cognito;
using Amazon.Extensions.CognitoAuthentication;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using MyMvcApp.Models;
using MyMvcApp.Data; // ADD THIS

namespace MyMvcApp.Controllers
{
    /// <summary>
    /// Handles Cognito-backed login, registration, logout, and password reset flows.
    /// </summary>
    public class AccountController : Controller
    {
        /// <summary>
        /// Signs users into and out of the application cookie through Cognito Identity.
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
        /// Provides access to local application user and password reset records.
        /// </summary>
        private readonly AppDbContext _dbContext; // ADD THIS

        /// <summary>
        /// Calls Cognito APIs that are not exposed through the ASP.NET Identity wrapper.
        /// </summary>
        private readonly IAmazonCognitoIdentityProvider _cognitoClient;

        /// <summary>
        /// Reads Cognito app client settings.
        /// </summary>
        private readonly IConfiguration _config;

        /// <summary>
        /// Creates a controller instance with Cognito identity, Cognito API, and local user data services.
        /// </summary>
        public AccountController(
            SignInManager<CognitoUser> signInManager,
            UserManager<CognitoUser> userManager,
            CognitoUserPool pool,
            AppDbContext dbContext,
            IAmazonCognitoIdentityProvider cognitoClient,
            IConfiguration config) 
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _pool = pool;
            _dbContext = dbContext;
            _cognitoClient = cognitoClient;
            _config = config;
        }

        /// <summary>
        /// Shows the combined login and registration page in the requested mode.
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
        /// Authenticates an approved local user against Cognito and redirects by application role.
        /// </summary>
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
                    return RedirectToAction(nameof(PendingApproval));
                }

                try 
                {
                    // 2. SIGN IN TO COGNITO USING EMAIL
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
        /// Shows the waiting page for registered users who have not been approved by an admin.
        /// </summary>
        [HttpGet]
        public IActionResult PendingApproval()
        {
            return View();
        }

        /// <summary>
        /// Shows the access denied page for authenticated users without permission.
        /// </summary>
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        /// <summary>
        /// Creates a pending local password reset request for admin review.
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
        /// Shows the form for entering a Cognito password reset code and new password.
        /// </summary>
        [HttpGet]
        public IActionResult ResetPassword(string? email = null)
        {
            return View(new ResetPasswordViewModel
            {
                Email = email ?? string.Empty
            });
        }

        /// <summary>
        /// Confirms a Cognito password reset code and updates the user's password.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var normalizedEmail = model.Email.Trim().ToLowerInvariant();
            var clientId = _config["AWS:UserPoolClientId"];

            if (string.IsNullOrWhiteSpace(clientId))
            {
                ModelState.AddModelError(string.Empty, "Cognito app client is not configured.");
                return View(model);
            }

            try
            {
                var request = new ConfirmForgotPasswordRequest
                {
                    ClientId = clientId,
                    Username = normalizedEmail,
                    ConfirmationCode = model.Code.Trim(),
                    Password = model.NewPassword
                };

                var secretHash = ComputeCognitoSecretHash(normalizedEmail, clientId);
                if (!string.IsNullOrWhiteSpace(secretHash))
                {
                    request.SecretHash = secretHash;
                }

                await _cognitoClient.ConfirmForgotPasswordAsync(request);

                TempData["SuccessMessage"] = "Password reset successful. Please sign in with your new password.";
                return RedirectToAction(nameof(Login));
            }
            catch (CodeMismatchException)
            {
                ModelState.AddModelError(nameof(model.Code), "The reset code is incorrect.");
            }
            catch (ExpiredCodeException)
            {
                ModelState.AddModelError(nameof(model.Code), "The reset code has expired. Please request a new password reset.");
            }
            catch (InvalidPasswordException ex)
            {
                ModelState.AddModelError(nameof(model.NewPassword), ex.Message);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Password reset failed: {ex.Message}");
            }

            return View(model);
        }

        /// <summary>
        /// Computes the Cognito client secret hash when the app client has a configured secret.
        /// </summary>
        private string? ComputeCognitoSecretHash(string username, string clientId)
        {
            var clientSecret = _config["AWS:UserPoolClientSecret"];

            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                return null;
            }

            var message = Encoding.UTF8.GetBytes(username + clientId);
            var key = Encoding.UTF8.GetBytes(clientSecret);

            using var hmac = new HMACSHA256(key);
            return Convert.ToBase64String(hmac.ComputeHash(message));
        }

        /// <summary>
        /// Redirects legacy register links to the combined authentication page.
        /// </summary>
        [HttpGet]
        public IActionResult Register() => RedirectToAction(nameof(Login), new { mode = "register" });

        /// <summary>
        /// Registers a Cognito user and creates a pending local application account.
        /// </summary>
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
        /// Signs the current user out of the application.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login), "Account");
        }

        /// <summary>
        /// Returns current authentication and claims details for diagnostics.
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
