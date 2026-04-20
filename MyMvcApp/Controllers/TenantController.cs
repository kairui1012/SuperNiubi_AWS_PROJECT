using Amazon.Extensions.CognitoAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MyMvcApp.Controllers

{
    [Authorize] // Ensures only logged-in users can reach this page
    public class TenantController : Controller
    {
        private readonly UserManager<CognitoUser> _userManager;

        public TenantController(UserManager<CognitoUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<IActionResult> Tenant()
        {
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                    ?? User.Identity?.Name;
            ViewBag.TenantEmail = email;
            return View();
        }
    }
}