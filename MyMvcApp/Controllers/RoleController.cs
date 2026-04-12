using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyMvcApp.Controllers
{
    [Authorize]
    public class RoleController : Controller
    {
        public IActionResult Admin()
        {
            return View();
        }

        public IActionResult Manager()
        {
            return View();
        }

        public IActionResult StandardUser()
        {
            return View();
        }
    }
}