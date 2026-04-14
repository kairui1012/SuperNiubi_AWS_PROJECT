using Amazon.AspNetCore.Identity.Cognito;
using Amazon.Extensions.CognitoAuthentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

        public AccountController(SignInManager<CognitoUser> signInManager, UserManager<CognitoUser> userManager, CognitoUserPool pool, AppDbContext dbContext) 
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _pool = pool;
            _dbContext = dbContext;
        }

        // --- LOGIN ---
        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                // 1. LOOKUP BY EMAIL NOW
                var appUser = _dbContext.Users.FirstOrDefault(u => u.Email.ToLower() == model.Email.ToLower());
                
                if (appUser == null) {
                    ModelState.AddModelError(string.Empty, "User does not exist.");
                    return View(model);
                }

                if (appUser.IsDisabled) {
                    ModelState.AddModelError(string.Empty, "Your account has been disabled by the administrator.");
                    return View(model);
                }

                if (!appUser.IsApproved) {
                    ModelState.AddModelError(string.Empty, "Your account is pending Admin approval.");
                    return View(model);
                }

                try 
                {
                    // 2. SIGN IN TO COGNITO USING EMAIL
                    var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, false);

                    if (result.Succeeded)
                    {
                        if (appUser.Role == "Admin") return RedirectToAction("Admin", "Admin");
                        if (appUser.Role == "Landlord") return RedirectToAction("Landlord", "Landlord");
                        return RedirectToAction("Tenant", "Tenant");
                    }
                    ModelState.AddModelError(string.Empty, "Invalid password.");
                }
                catch (Amazon.CognitoIdentityProvider.Model.UserNotConfirmedException)
                {
                    ModelState.AddModelError(string.Empty, "Your account has not been confirmed by the administrator yet.");
                }
            }
            return View(model);
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // --- REGISTER ---
        [HttpGet]
        public IActionResult Register() => View();

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
                        Role = "Tenant" 
                    });
                    await _dbContext.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Registration successful! Please wait for admin approval.";
                    return RedirectToAction("Login");
                }
                foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }
    }
}