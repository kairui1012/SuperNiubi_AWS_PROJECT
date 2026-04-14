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

        public IActionResult Admin()
        {
            var users = _dbContext.Users.OrderBy(u => u.IsApproved).ToList();
            return View(users);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user != null && !user.IsApproved)
            {
                // 1. Confirm the user in AWS Cognito directly
                try
                {
                    var userPoolId = _config["AWS:UserPoolId"];
                    var confirmRequest = new AdminConfirmSignUpRequest
                    {
                        UserPoolId = userPoolId,
                        Username = user.Email // Using Email for Cognito
                    };
                    await _cognitoClient.AdminConfirmSignUpAsync(confirmRequest);
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Failed to confirm in Cognito: {ex.Message}";
                    return RedirectToAction("Admin");
                }

                // 2. Approve in Neon Database
                user.IsApproved = true;
                await _dbContext.SaveChangesAsync();
                
                // 3. Send email safely (won't crash the app if it fails)
                try 
                {
                    await _emailService.SendApprovalEmailAsync(user.Email, user.Nickname);
                    TempData["SuccessMessage"] = "User approved and email sent!";
                }
                catch (Exception ex)
                {
                    // The user is still approved in the DB, but we log that the email failed
                    Console.WriteLine($"Email Failed: {ex.Message}");
                    TempData["SuccessMessage"] = "User approved, but the notification email failed to send.";
                }
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