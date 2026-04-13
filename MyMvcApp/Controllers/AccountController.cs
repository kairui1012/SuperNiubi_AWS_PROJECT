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
                // 1. CHECK LOCAL DB FOR APPROVAL FIRST
                var appUser = _dbContext.Users.FirstOrDefault(u => u.Nickname == model.Nickname);
                
                if (appUser == null) {
                    ModelState.AddModelError(string.Empty, "User does not exist.");
                    return View(model);
                }
                if (!appUser.IsApproved) {
                    ModelState.AddModelError(string.Empty, "Your account is pending Admin approval. Please wait for the email.");
                    return View(model);
                }

                // 2. IF APPROVED, LOGIN TO COGNITO
                try 
{
                    var result = await _signInManager.PasswordSignInAsync(model.Nickname, model.Password, model.RememberMe, false);

                    if (result.Succeeded)
                    {
                        // 3. ROLE-BASED REDIRECTION
                        if (appUser.Role == "Admin") return RedirectToAction("Admin", "Admin");
                        if (appUser.Role == "Landlord") return RedirectToAction("Landlord", "Landlord");
                        return RedirectToAction("Tenant", "Tenant");
                    }
                    ModelState.AddModelError(string.Empty, "Invalid password.");
                }
                catch (Amazon.CognitoIdentityProvider.Model.UserNotConfirmedException)
                {
                    // Catch the Cognito error nicely instead of crashing the server
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
                var user = _pool.GetUser(model.Nickname);
                user.Attributes.Add("email", model.Email);
                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    // 1. Save to Neon DB (Unapproved by default)
                    _dbContext.Users.Add(new AppUser {
                        Nickname = model.Nickname,
                        Email = model.Email,
                        IsApproved = false,
                        Role = "Tenant" // Default role
                    });
                    await _dbContext.SaveChangesAsync();

                    // 2. Redirect to login with a success message (DO NOT auto-login)
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
            return RedirectToAction("Index", "Home");
        }
    }
}