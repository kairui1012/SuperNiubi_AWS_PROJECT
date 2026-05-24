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
    public class AccountController : Controller
    {
        private readonly SignInManager<CognitoUser> _signInManager;
        private readonly UserManager<CognitoUser> _userManager;
        private readonly CognitoUserPool _pool; 
        private readonly AppDbContext _dbContext; // ADD THIS
        private readonly IAmazonCognitoIdentityProvider _cognitoClient;
        private readonly IConfiguration _config;

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

        [HttpGet]
        public IActionResult PendingApproval()
        {
            return View();
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

        [HttpGet]
        public IActionResult ResetPassword(string? email = null)
        {
            return View(new ResetPasswordViewModel
            {
                Email = email ?? string.Empty
            });
        }

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login), "Account");
        }

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
