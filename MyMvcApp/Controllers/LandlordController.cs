using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyMvcApp.Data;
using MyMvcApp.Models;
using System.Linq;

namespace MyMvcApp.Controllers
{
    [Authorize]
    public class LandlordController : Controller
    {
        private readonly AppDbContext _dbContext;

        public LandlordController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public IActionResult Dashboard()
        {
            return View();
        }

        public IActionResult MyProperties()
        {
            var properties = _dbContext.Properties.ToList();
            return View(properties);
        }

        [HttpGet]
        public IActionResult AddProperty()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddProperty(Property model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            model.CreatedAt = DateTime.UtcNow;
            model.UpdatedAt = DateTime.UtcNow;

            _dbContext.Properties.Add(model);
            _dbContext.SaveChanges();

            return RedirectToAction("MyProperties");
        }
    }
}