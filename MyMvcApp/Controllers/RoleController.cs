using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyMvcApp.Controllers
{
    /// <summary>
    /// Serves simple role-specific pages for authenticated users.
    /// </summary>
    [Authorize]
    public class RoleController : Controller
    {
        /// <summary>
        /// Shows the admin role page.
        /// </summary>
        public IActionResult Admin()
        {
            return View();
        }

        /// <summary>
        /// Shows the manager role page.
        /// </summary>
        public IActionResult Manager()
        {
            return View();
        }

        /// <summary>
        /// Shows the standard user role page.
        /// </summary>
        public IActionResult StandardUser()
        {
            return View();
        }
    }
}
