using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
            var userEmail = User.Claims
                .FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return View(new List<Property>());
            }

            var landlord = _dbContext.Users
                .FirstOrDefault(u =>
                    u.Email.ToLower() == userEmail.ToLower() &&
                    u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return View(new List<Property>());
            }

            var properties = _dbContext.Properties
                .Where(p => p.LandlordId == landlord.Id)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            return View(properties);
        }

        [HttpGet]
        public IActionResult PropertyDetails(int id)
        {
            var userEmail = User.Claims
                .FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("MyProperties");
            }

            var landlord = _dbContext.Users
                .FirstOrDefault(u =>
                    u.Email.ToLower() == userEmail.ToLower() &&
                    u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction("MyProperties");
            }

            var property = _dbContext.Properties
                .FirstOrDefault(p => p.PropertyId == id && p.LandlordId == landlord.Id);

            if (property == null)
            {
                TempData["ErrorMessage"] = "Property not found.";
                return RedirectToAction("MyProperties");
            }

            return View(property);
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
            var userEmail = User.Claims
                .FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                ModelState.AddModelError(string.Empty, "User email not found.");
                return View(model);
            }

            var landlord = _dbContext.Users
                .FirstOrDefault(u =>
                    u.Email.ToLower() == userEmail.ToLower() &&
                    u.Role == "Landlord");

            if (landlord == null)
            {
                ModelState.AddModelError(string.Empty, "Landlord not found.");
                return View(model);
            }

            model.LandlordId = landlord.Id;
            model.CreatedAt = DateTime.UtcNow;
            model.UpdatedAt = DateTime.UtcNow;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                _dbContext.Properties.Add(model);
                _dbContext.SaveChanges();

                TempData["SuccessMessage"] = "Property saved successfully.";
                return RedirectToAction("MyProperties");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Failed to save property: " + ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult EditProperty(int id)
        {
            var userEmail = User.Claims
                .FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("MyProperties");
            }

            var landlord = _dbContext.Users
                .FirstOrDefault(u =>
                    u.Email.ToLower() == userEmail.ToLower() &&
                    u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction("MyProperties");
            }

            var property = _dbContext.Properties
                .FirstOrDefault(p => p.PropertyId == id && p.LandlordId == landlord.Id);

            if (property == null)
            {
                TempData["ErrorMessage"] = "Property not found.";
                return RedirectToAction("MyProperties");
            }

            return View(property);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditProperty(Property model)
        {
            var userEmail = User.Claims
                .FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                ModelState.AddModelError(string.Empty, "User email not found.");
                return View(model);
            }

            var landlord = _dbContext.Users
                .FirstOrDefault(u =>
                    u.Email.ToLower() == userEmail.ToLower() &&
                    u.Role == "Landlord");

            if (landlord == null)
            {
                ModelState.AddModelError(string.Empty, "Landlord account not found.");
                return View(model);
            }

            var existingProperty = _dbContext.Properties
                .FirstOrDefault(p => p.PropertyId == model.PropertyId && p.LandlordId == landlord.Id);

            if (existingProperty == null)
            {
                ModelState.AddModelError(string.Empty, "Property not found.");
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                existingProperty.PropertyName = model.PropertyName;
                existingProperty.PropertyType = model.PropertyType;
                existingProperty.AddressLine1 = model.AddressLine1;
                existingProperty.AddressLine2 = model.AddressLine2;
                existingProperty.City = model.City;
                existingProperty.State = model.State;
                existingProperty.PostalCode = model.PostalCode;
                existingProperty.FloorNumber = model.FloorNumber;
                existingProperty.UnitNumber = model.UnitNumber;
                existingProperty.SizeSqFt = model.SizeSqFt;
                existingProperty.Bedrooms = model.Bedrooms;
                existingProperty.Bathrooms = model.Bathrooms;
                existingProperty.MonthlyRent = model.MonthlyRent;
                existingProperty.DepositAmount = model.DepositAmount;
                existingProperty.ParkingBay = model.ParkingBay;
                existingProperty.Description = model.Description;
                existingProperty.UpdatedAt = DateTime.UtcNow;

                _dbContext.SaveChanges();

                TempData["SuccessMessage"] = "Property updated successfully.";
                return RedirectToAction("MyProperties");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Failed to update property: " + ex.Message);
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteProperty(int id)
        {
            var userEmail = User.Claims
                .FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("MyProperties");
            }

            var landlord = _dbContext.Users
                .FirstOrDefault(u =>
                    u.Email.ToLower() == userEmail.ToLower() &&
                    u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction("MyProperties");
            }

            var property = _dbContext.Properties
                .FirstOrDefault(p => p.PropertyId == id && p.LandlordId == landlord.Id);

            if (property == null)
            {
                TempData["ErrorMessage"] = "Property not found.";
                return RedirectToAction("MyProperties");
            }

            try
            {
                _dbContext.Properties.Remove(property);
                _dbContext.SaveChanges();

                TempData["SuccessMessage"] = "Property deleted successfully.";
                return RedirectToAction("MyProperties");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Failed to delete property: " + ex.Message;
                return RedirectToAction("MyProperties");
            }
        }

        public IActionResult Tenants()
        {
            var userEmail = User.Claims
                .FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return View(new List<Tenant>());
            }

            var landlord = _dbContext.Users
                .FirstOrDefault(u =>
                    u.Email.ToLower() == userEmail.ToLower() &&
                    u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return View(new List<Tenant>());
            }

            var tenants = _dbContext.Tenants
                .Include(t => t.Property)
                .Include(t => t.User)
                .Where(t => t.Property.LandlordId == landlord.Id)
                .OrderByDescending(t => t.CreatedAt)
                .ToList();

            return View(tenants);
        }

        [HttpGet]
        public IActionResult TenantDetails(int id)
        {
            var userEmail = User.Claims
                .FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("Tenants");
            }

            var landlord = _dbContext.Users
                .FirstOrDefault(u =>
                    u.Email.ToLower() == userEmail.ToLower() &&
                    u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction("Tenants");
            }

            var tenant = _dbContext.Tenants
                .Include(t => t.Property)
                .Include(t => t.User)
                .FirstOrDefault(t => t.TenantId == id && t.Property.LandlordId == landlord.Id);

            if (tenant == null)
            {
                TempData["ErrorMessage"] = "Tenant not found.";
                return RedirectToAction("Tenants");
            }

            return View(tenant);
        }
    }
}