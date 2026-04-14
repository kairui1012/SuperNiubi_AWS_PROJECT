using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyMvcApp.Controllers
{
    [Authorize] // Ensures only logged-in users can reach this page
    public class TenantController : Controller
    {
        public IActionResult Tenant()
        {
            return View();
        }
    }
}