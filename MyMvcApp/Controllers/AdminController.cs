using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyMvcApp.Data;
using MyMvcApp.Services;
using Amazon.CognitoIdentityProvider; // ADD THIS
using Amazon.CognitoIdentityProvider.Model; // ADD THIS

namespace MyMvcApp.Controllers
{
    [Authorize(Roles = "Admin")] // Optional: secures this controller
    public class AdminController : Controller
    {
        private readonly AppDbContext _dbContext;
        private readonly EmailService _emailService;
        private readonly IAmazonCognitoIdentityProvider _cognitoClient;
        private readonly IConfiguration _config;

        // Inject the Cognito Client and Configuration
        public AdminController(AppDbContext dbContext, EmailService emailService, IAmazonCognitoIdentityProvider cognitoClient, IConfiguration config)
        {
            _dbContext = dbContext;
            _emailService = emailService;
            _cognitoClient = cognitoClient;
            _config = config;
        }

        public IActionResult Admin(string searchEmail)
        {
            var usersQuery = _dbContext.Users.AsQueryable();

            if (!string.IsNullOrEmpty(searchEmail))
            {
                // Filter by email if a search query is provided
                usersQuery = usersQuery.Where(u => u.Email.Contains(searchEmail));
            }

            var users = usersQuery.OrderBy(u => u.IsApproved).ToList();
            return View(users);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user != null && !user.IsApproved)
            {
                try
                {
                    var userPoolId = _config["AWS:UserPoolId"];
                    var confirmRequest = new AdminConfirmSignUpRequest
                    {
                        UserPoolId = userPoolId,
                        Username = user.Email
                    };
                    await _cognitoClient.AdminConfirmSignUpAsync(confirmRequest);
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Failed to confirm in Cognito: {ex.Message}";
                    return RedirectToAction("Admin");
                }

                user.IsApproved = true;
                await _dbContext.SaveChangesAsync();
                
                try 
                {
                    // Updated: Removed the nickname parameter
                    await _emailService.SendApprovalEmailAsync(user.Email);
                    TempData["SuccessMessage"] = "User approved and email sent!";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Email Failed: {ex.Message}");
                    TempData["SuccessMessage"] = "User approved, but the notification email failed to send.";
                }
            }
            return RedirectToAction("Admin");
        }
        [HttpPost]
        public async Task<IActionResult> DisableUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user != null && !user.IsDisabled)
            {
                try
                {
                    // Disable the user inside AWS Cognito to completely revoke access
                    var userPoolId = _config["AWS:UserPoolId"];
                    var disableRequest = new AdminDisableUserRequest
                    {
                        UserPoolId = userPoolId,
                        Username = user.Email
                    };
                    await _cognitoClient.AdminDisableUserAsync(disableRequest);
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Failed to disable in Cognito: {ex.Message}";
                    return RedirectToAction("Admin");
                }

                // Mark as disabled in Neon DB
                user.IsDisabled = true;
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = "User has been disabled successfully.";
            }
            return RedirectToAction("Admin");
        }

        [HttpPost]
        public async Task<IActionResult> EnableUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user != null && user.IsDisabled)
            {
                try
                {
                    // 1. Enable the user inside AWS Cognito
                    var userPoolId = _config["AWS:UserPoolId"];
                    var enableRequest = new AdminEnableUserRequest
                    {
                        UserPoolId = userPoolId,
                        Username = user.Email
                    };
                    await _cognitoClient.AdminEnableUserAsync(enableRequest);
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Failed to enable in Cognito: {ex.Message}";
                    return RedirectToAction("Admin");
                }

                // 2. Mark as enabled in Neon DB
                user.IsDisabled = false;
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = "User has been enabled successfully.";
            }
            return RedirectToAction("Admin");
        }

        [HttpPost]
        public async Task<IActionResult> ChangeRole(int id, string newRole)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user != null)
            {
                user.Role = newRole;
                await _dbContext.SaveChangesAsync();
            }
            return RedirectToAction("Admin");
        }
    }
}