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

        public async Task<IActionResult> Dashboard()
        {
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return View(new LandlordDashboardViewModel());
            }

            var landlord = await _dbContext.Users.FirstOrDefaultAsync(u =>
                u.Email.ToLower() == userEmail.ToLower() &&
                u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return View(new LandlordDashboardViewModel());
            }

            var utcNow = DateTime.UtcNow;
            var currentMonthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var nextMonthStart = currentMonthStart.AddMonths(1);

            var propertiesQuery = _dbContext.Properties
                .AsNoTracking()
                .Where(p => p.LandlordId == landlord.Id);

            var tenants = await _dbContext.Tenants
                .AsNoTracking()
                .Include(t => t.User)
                .Include(t => t.Property)
                .Include(t => t.Payments)
                .Where(t => t.Property.LandlordId == landlord.Id)
                .ToListAsync();

            var unpaidTenantsCount = tenants.Count(t =>
                t.LeaseStatus == LeaseStatus.Active &&
                !t.Payments.Any(p => p.Status == PaymentStatus.Verified
                    && p.PaymentDate >= currentMonthStart
                    && p.PaymentDate < nextMonthStart));

            var activeMaintenanceStatuses = new[]
            {
                MaintenanceStatus.Pending,
                MaintenanceStatus.Approved,
                MaintenanceStatus.InProgress
            };

            var model = new LandlordDashboardViewModel
            {
                LandlordEmail = landlord.Email,
                MyPropertiesCount = await propertiesQuery.CountAsync(),
                TenantCount = tenants.Count,
                VacantPropertiesCount = await propertiesQuery.CountAsync(p => p.Tenant == null),
                MonthlyRentalIncome = await _dbContext.Payments
                    .AsNoTracking()
                    .Where(p => p.Property.LandlordId == landlord.Id
                        && p.Status == PaymentStatus.Verified
                        && p.PaymentDate >= currentMonthStart
                        && p.PaymentDate < nextMonthStart)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0m,
                UnpaidTenantsCount = unpaidTenantsCount,
                ActiveMaintenanceRequestsCount = await _dbContext.MaintenanceRequests
                    .AsNoTracking()
                    .CountAsync(m => m.Property.LandlordId == landlord.Id
                        && activeMaintenanceStatuses.Contains(m.Status)),
                RecentProperties = await propertiesQuery
                    .OrderByDescending(p => p.CreatedAt)
                    .Take(4)
                    .ToListAsync(),
                RecentPayments = await _dbContext.Payments
                    .AsNoTracking()
                    .Include(p => p.Property)
                    .Include(p => p.Tenant)
                    .ThenInclude(t => t.User)
                    .Where(p => p.Property.LandlordId == landlord.Id)
                    .OrderByDescending(p => p.CreatedAt)
                    .Take(4)
                    .ToListAsync(),
                RecentMaintenanceRequests = await _dbContext.MaintenanceRequests
                    .AsNoTracking()
                    .Include(m => m.Property)
                    .Include(m => m.Tenant)
                    .ThenInclude(t => t.User)
                    .Where(m => m.Property.LandlordId == landlord.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(4)
                    .ToListAsync()
            };

            return View(model);
        }

        public IActionResult MyProperties()
        {
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return View(new List<Property>());
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
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
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("MyProperties");
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
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
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                ModelState.AddModelError(string.Empty, "User email not found.");
                return View(model);
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
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
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("MyProperties");
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
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
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                ModelState.AddModelError(string.Empty, "User email not found.");
                return View(model);
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
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
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("MyProperties");
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
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
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return View(new List<Tenant>());
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
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
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("Tenants");
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
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

        public IActionResult MaintenanceRequests()
        {
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return View(new List<MaintenanceRequest>());
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
                u.Email.ToLower() == userEmail.ToLower() &&
                u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return View(new List<MaintenanceRequest>());
            }

            var requests = _dbContext.MaintenanceRequests
                .Include(m => m.Property)
                .Include(m => m.Tenant)
                .ThenInclude(t => t.User)
                .Where(m => m.Property.LandlordId == landlord.Id)
                .OrderByDescending(m => m.CreatedAt)
                .ToList();

            return View(requests);
        }

        [HttpGet]
        public IActionResult EditMaintenanceRequest(int id)
        {
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("MaintenanceRequests");
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
                u.Email.ToLower() == userEmail.ToLower() &&
                u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction("MaintenanceRequests");
            }

            var request = _dbContext.MaintenanceRequests
                .Include(m => m.Property)
                .Include(m => m.Tenant)
                .ThenInclude(t => t.User)
                .FirstOrDefault(m =>
                    m.RequestId == id &&
                    m.Property.LandlordId == landlord.Id);

            if (request == null)
            {
                TempData["ErrorMessage"] = "Maintenance request not found.";
                return RedirectToAction("MaintenanceRequests");
            }

            return View(request);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditMaintenanceRequest(MaintenanceRequest model)
        {
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("MaintenanceRequests");
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
                u.Email.ToLower() == userEmail.ToLower() &&
                u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction("MaintenanceRequests");
            }

            var existingRequest = _dbContext.MaintenanceRequests
                .Include(m => m.Property)
                .FirstOrDefault(m =>
                    m.RequestId == model.RequestId &&
                    m.Property.LandlordId == landlord.Id);

            if (existingRequest == null)
            {
                TempData["ErrorMessage"] = "Maintenance request not found.";
                return RedirectToAction("MaintenanceRequests");
            }

            existingRequest.Priority = model.Priority;
            existingRequest.Status = model.Status;
            existingRequest.LandlordRemarks = model.LandlordRemarks;
            existingRequest.UpdatedAt = DateTime.UtcNow;

            if (model.Status == MaintenanceStatus.Completed)
            {
                existingRequest.ResolvedDate = DateTime.UtcNow;
            }
            else
            {
                existingRequest.ResolvedDate = null;
            }

            _dbContext.SaveChanges();

            TempData["SuccessMessage"] = "Maintenance request updated successfully.";
            return RedirectToAction("MaintenanceRequests");
        }

        public IActionResult Payments()
        {
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return View(new List<Payment>());
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
                u.Email.ToLower() == userEmail.ToLower() &&
                u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return View(new List<Payment>());
            }

            var payments = _dbContext.Payments
                .Include(p => p.Property)
                .Include(p => p.Tenant)
                .ThenInclude(t => t.User)
                .Where(p => p.Property.LandlordId == landlord.Id)
                .OrderByDescending(p => p.PaymentYear)
                .ThenByDescending(p => p.CreatedAt)
                .ToList();

            return View(payments);
        }

        [Authorize(Roles = "Landlord")]
        public async Task<IActionResult> Announcements()
        {
            var announcements = await _dbContext.SystemAnnouncements
                .AsNoTracking()
                .Where(a => a.VisibleTo == "All" || a.VisibleTo == "Landlord")
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return View(announcements);
        }
    }
}
