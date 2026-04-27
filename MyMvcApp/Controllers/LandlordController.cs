using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using System.Linq;
using System.Security.Claims;

namespace MyMvcApp.Controllers
{
    [Authorize]
    public class LandlordController : Controller
    {
        private static readonly string[] AllowedAnnouncementAudiences = { "All", "Tenant", "Landlord" };
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
            var landlord = GetCurrentLandlord();

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

            LoadAvailablePropertyOptions(landlord.Id, tenant.PropertyId);
            LoadLeaseHistory(tenant.TenantId);

            return View(tenant);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RenewLease(RenewLeaseViewModel model)
        {
            var result = GetManagedTenant(model.TenantId);
            if (result.Tenant == null)
            {
                TempData["ErrorMessage"] = result.ErrorMessage;
                return RedirectToAction("Tenants");
            }

            if (model.LeaseEndDate <= model.LeaseStartDate)
            {
                TempData["ErrorMessage"] = "Lease end date must be later than lease start date.";
                return RedirectToAction("TenantDetails", new { id = model.TenantId });
            }

            var oldValue = $"Start: {result.Tenant.LeaseStartDate:yyyy-MM-dd}; End: {result.Tenant.LeaseEndDate:yyyy-MM-dd}; Status: {result.Tenant.LeaseStatus}";
            var newStart = DateTime.SpecifyKind(model.LeaseStartDate, DateTimeKind.Utc);
            var newEnd = DateTime.SpecifyKind(model.LeaseEndDate, DateTimeKind.Utc);

            result.Tenant.LeaseStartDate = newStart;
            result.Tenant.LeaseEndDate = newEnd;
            result.Tenant.LeaseStatus = LeaseStatus.Active;
            result.Tenant.Notes = model.Notes;
            result.Tenant.UpdatedAt = DateTime.UtcNow;
            AddLeaseHistory(
                result.Tenant,
                "Renew lease",
                oldValue,
                $"Start: {newStart:yyyy-MM-dd}; End: {newEnd:yyyy-MM-dd}; Status: {LeaseStatus.Active}",
                model.Notes);

            _dbContext.SaveChanges();

            TempData["SuccessMessage"] = "Lease renewed successfully.";
            return RedirectToAction("TenantDetails", new { id = model.TenantId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult TerminateLease(TerminateLeaseViewModel model)
        {
            var result = GetManagedTenant(model.TenantId);
            if (result.Tenant == null)
            {
                TempData["ErrorMessage"] = result.ErrorMessage;
                return RedirectToAction("Tenants");
            }

            var oldValue = $"End: {result.Tenant.LeaseEndDate:yyyy-MM-dd}; Status: {result.Tenant.LeaseStatus}";
            var terminatedAt = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

            result.Tenant.LeaseStatus = LeaseStatus.Terminated;
            result.Tenant.LeaseEndDate = terminatedAt;
            result.Tenant.Notes = model.Notes;
            result.Tenant.UpdatedAt = DateTime.UtcNow;
            AddLeaseHistory(
                result.Tenant,
                "Terminate lease",
                oldValue,
                $"End: {terminatedAt:yyyy-MM-dd}; Status: {LeaseStatus.Terminated}",
                model.Notes);

            _dbContext.SaveChanges();

            TempData["SuccessMessage"] = "Lease terminated successfully.";
            return RedirectToAction("TenantDetails", new { id = model.TenantId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AdjustRent(AdjustRentViewModel model)
        {
            var result = GetManagedTenant(model.TenantId);
            if (result.Tenant == null)
            {
                TempData["ErrorMessage"] = result.ErrorMessage;
                return RedirectToAction("Tenants");
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please enter a valid rent amount and rent due day.";
                return RedirectToAction("TenantDetails", new { id = model.TenantId });
            }

            var oldValue = $"Monthly rent: RM {result.Tenant.MonthlyRent:N2}; Due day: {result.Tenant.RentDueDay}";

            result.Tenant.MonthlyRent = model.MonthlyRent;
            result.Tenant.RentDueDay = model.RentDueDay;
            result.Tenant.UpdatedAt = DateTime.UtcNow;
            AddLeaseHistory(
                result.Tenant,
                "Adjust rent",
                oldValue,
                $"Monthly rent: RM {model.MonthlyRent:N2}; Due day: {model.RentDueDay}",
                null);

            _dbContext.SaveChanges();

            TempData["SuccessMessage"] = "Rent details updated successfully.";
            return RedirectToAction("TenantDetails", new { id = model.TenantId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeTenantProperty(ChangeTenantPropertyViewModel model)
        {
            var result = GetManagedTenant(model.TenantId);
            if (result.Tenant == null || result.Landlord == null)
            {
                TempData["ErrorMessage"] = result.ErrorMessage;
                return RedirectToAction("Tenants");
            }

            var property = _dbContext.Properties.FirstOrDefault(p =>
                p.PropertyId == model.PropertyId &&
                p.LandlordId == result.Landlord.Id);

            if (property == null)
            {
                TempData["ErrorMessage"] = "Selected property is invalid.";
                return RedirectToAction("TenantDetails", new { id = model.TenantId });
            }

            var propertyOccupied = _dbContext.Tenants.Any(t =>
                t.PropertyId == model.PropertyId &&
                t.TenantId != model.TenantId);

            if (propertyOccupied)
            {
                TempData["ErrorMessage"] = "Selected property already has an assigned tenant.";
                return RedirectToAction("TenantDetails", new { id = model.TenantId });
            }

            var oldValue = $"Property: {result.Tenant.Property.PropertyName}";

            result.Tenant.PropertyId = model.PropertyId;
            result.Tenant.UpdatedAt = DateTime.UtcNow;
            AddLeaseHistory(
                result.Tenant,
                "Move property",
                oldValue,
                $"Property: {property.PropertyName}",
                null);

            _dbContext.SaveChanges();

            TempData["SuccessMessage"] = "Tenant property changed successfully.";
            return RedirectToAction("TenantDetails", new { id = model.TenantId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeDepositStatus(ChangeDepositStatusViewModel model)
        {
            var result = GetManagedTenant(model.TenantId);
            if (result.Tenant == null)
            {
                TempData["ErrorMessage"] = result.ErrorMessage;
                return RedirectToAction("Tenants");
            }

            var oldValue = $"Deposit status: {result.Tenant.DepositStatus}";
            result.Tenant.DepositStatus = model.DepositStatus;
            result.Tenant.Notes = model.Notes;
            result.Tenant.UpdatedAt = DateTime.UtcNow;
            AddLeaseHistory(
                result.Tenant,
                "Change deposit status",
                oldValue,
                $"Deposit status: {model.DepositStatus}",
                model.Notes);

            _dbContext.SaveChanges();

            TempData["SuccessMessage"] = "Deposit status updated successfully.";
            return RedirectToAction("TenantDetails", new { id = model.TenantId });
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
            var currentUserEmail = GetCurrentUserEmail();

            var announcements = await _dbContext.SystemAnnouncements
                .AsNoTracking()
                .Where(a => a.VisibleTo == "All"
                    || a.VisibleTo == "Landlord"
                    || a.CreatedByEmail.ToLower() == currentUserEmail.ToLower())
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return View(announcements);
        }

        [HttpPost]
        [Authorize(Roles = "Landlord")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAnnouncement(CreateLandlordAnnouncementViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please fill in the title and message before publishing.";
                return RedirectToAction(nameof(Announcements));
            }

            var currentUserEmail = GetCurrentUserEmail();
            var landlordExists = await _dbContext.Users.AnyAsync(u =>
                u.Email.ToLower() == currentUserEmail.ToLower() &&
                u.Role == "Landlord");

            if (!landlordExists)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction(nameof(Announcements));
            }

            var visibleTo = NormalizeAnnouncementAudience(model.VisibleTo);
            var announcement = new SystemAnnouncement
            {
                Title = model.Title.Trim(),
                Body = model.Body.Trim(),
                VisibleTo = visibleTo,
                CreatedAt = DateTime.UtcNow,
                CreatedByEmail = currentUserEmail
            };

            _dbContext.SystemAnnouncements.Add(announcement);
            _dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "LandlordCreateAnnouncement",
                ActorEmail = currentUserEmail,
                TargetType = "SystemAnnouncement",
                Details = $"Landlord created announcement '{announcement.Title}' visible to {announcement.VisibleTo}.",
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Announcement published successfully.";
            return RedirectToAction(nameof(Announcements));
        }

        [HttpGet]
        public IActionResult AssignTenant()
        {
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("Dashboard");
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
                u.Email.ToLower() == userEmail.ToLower() &&
                u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction("Dashboard");
            }

            var model = new AssignTenantViewModel();
            LoadAssignTenantDropdowns(model, landlord.Id);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AssignTenant(AssignTenantViewModel model)
        {
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                TempData["ErrorMessage"] = "User email not found.";
                return RedirectToAction("Dashboard");
            }

            var landlord = _dbContext.Users.FirstOrDefault(u =>
                u.Email.ToLower() == userEmail.ToLower() &&
                u.Role == "Landlord");

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction("Dashboard");
            }

            if (model.UserId <= 0)
            {
                ModelState.AddModelError("UserId", "Please select a tenant.");
            }

            if (model.PropertyId <= 0)
            {
                ModelState.AddModelError("PropertyId", "Please select a property.");
            }

            if (model.LeaseEndDate <= model.LeaseStartDate)
            {
                ModelState.AddModelError("LeaseEndDate", "Lease end date must be later than lease start date.");
            }

            var selectedUser = _dbContext.Users.FirstOrDefault(u =>
                u.Id == model.UserId &&
                u.Role == "Tenant" &&
                u.IsApproved);

            if (selectedUser == null)
            {
                ModelState.AddModelError("UserId", "Selected tenant user is invalid or not approved.");
            }

            var selectedProperty = _dbContext.Properties.FirstOrDefault(p =>
                p.PropertyId == model.PropertyId &&
                p.LandlordId == landlord.Id);

            if (selectedProperty == null)
            {
                ModelState.AddModelError("PropertyId", "Selected property is invalid.");
            }

            var tenantAlreadyAssigned = _dbContext.Tenants.Any(t => t.UserId == model.UserId);

            if (tenantAlreadyAssigned)
            {
                ModelState.AddModelError("UserId", "This tenant has already been assigned to a property.");
            }

            var propertyAlreadyAssigned = _dbContext.Tenants.Any(t => t.PropertyId == model.PropertyId);

            if (propertyAlreadyAssigned)
            {
                ModelState.AddModelError("PropertyId", "This property already has an assigned tenant.");
            }

            if (!ModelState.IsValid)
            {
                LoadAssignTenantDropdowns(model, landlord.Id);
                return View(model);
            }

            try
            {
                var tenant = new Tenant
                {
                    UserId = model.UserId,
                    PropertyId = model.PropertyId,
                    LeaseStartDate = DateTime.SpecifyKind(model.LeaseStartDate, DateTimeKind.Utc),
                    LeaseEndDate = DateTime.SpecifyKind(model.LeaseEndDate, DateTimeKind.Utc),
                    MonthlyRent = model.MonthlyRent,
                    DepositPaid = model.DepositPaid,
                    DepositStatus = model.DepositStatus,
                    RentDueDay = model.RentDueDay,
                    LeaseStatus = LeaseStatus.Active,
                    Notes = model.Notes,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.Tenants.Add(tenant);
                AddLeaseHistory(
                    tenant,
                    "Create lease",
                    null,
                    $"Property: {selectedProperty?.PropertyName}; Start: {tenant.LeaseStartDate:yyyy-MM-dd}; End: {tenant.LeaseEndDate:yyyy-MM-dd}; Monthly rent: RM {tenant.MonthlyRent:N2}; Deposit: {tenant.DepositStatus}",
                    tenant.Notes);
                _dbContext.SaveChanges();

                TempData["SuccessMessage"] = "Tenant assigned successfully. New TenantId: " + tenant.TenantId;
                return RedirectToAction("Tenants");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Save failed: " + ex.Message);

                if (ex.InnerException != null)
                {
                    ModelState.AddModelError(string.Empty, "Details: " + ex.InnerException.Message);
                }

                LoadAssignTenantDropdowns(model, landlord.Id);
                return View(model);
            }
        }

        private void LoadAssignTenantDropdowns(AssignTenantViewModel model, int landlordId)
        {
            model.TenantUsers = _dbContext.Users
                .Where(u =>
                    u.Role == "Tenant" &&
                    u.IsApproved &&
                    !_dbContext.Tenants.Any(t => t.UserId == u.Id))
                .Select(u => new SelectListItem
                {
                    Value = u.Id.ToString(),
                    Text = u.Email
                })
                .ToList();

            model.Properties = _dbContext.Properties
                .Where(p =>
                    p.LandlordId == landlordId &&
                    !_dbContext.Tenants.Any(t => t.PropertyId == p.PropertyId))
                .Select(p => new SelectListItem
                {
                    Value = p.PropertyId.ToString(),
                    Text = p.PropertyName
                })
                .ToList();
        }

        private static string NormalizeAnnouncementAudience(string? visibleTo)
        {
            if (string.IsNullOrWhiteSpace(visibleTo))
            {
                return "All";
            }

            return AllowedAnnouncementAudiences.FirstOrDefault(audience =>
                string.Equals(audience, visibleTo.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "All";
        }

        private AppUser? GetCurrentLandlord()
        {
            var userEmail = GetCurrentUserEmail();

            if (string.IsNullOrWhiteSpace(userEmail))
            {
                return null;
            }

            return _dbContext.Users.FirstOrDefault(u =>
                u.Email.ToLower() == userEmail.ToLower() &&
                u.Role == "Landlord");
        }

        private (Tenant? Tenant, AppUser? Landlord, string ErrorMessage) GetManagedTenant(int tenantId)
        {
            var landlord = GetCurrentLandlord();

            if (landlord == null)
            {
                return (null, null, "Landlord account not found.");
            }

            var tenant = _dbContext.Tenants
                .Include(t => t.Property)
                .Include(t => t.User)
                .FirstOrDefault(t =>
                    t.TenantId == tenantId &&
                    t.Property.LandlordId == landlord.Id);

            return tenant == null
                ? (null, landlord, "Tenant not found.")
                : (tenant, landlord, string.Empty);
        }

        private void LoadAvailablePropertyOptions(int landlordId, int currentPropertyId)
        {
            ViewBag.AvailableProperties = _dbContext.Properties
                .Where(p =>
                    p.LandlordId == landlordId &&
                    (p.PropertyId == currentPropertyId ||
                        !_dbContext.Tenants.Any(t => t.PropertyId == p.PropertyId)))
                .OrderBy(p => p.PropertyName)
                .Select(p => new SelectListItem
                {
                    Value = p.PropertyId.ToString(),
                    Text = p.PropertyName
                })
                .ToList();
        }

        private void LoadLeaseHistory(int tenantId)
        {
            ViewBag.LeaseHistory = _dbContext.LeaseHistories
                .AsNoTracking()
                .Where(h => h.TenantId == tenantId)
                .OrderByDescending(h => h.CreatedAt)
                .ToList();
        }

        private void AddLeaseHistory(Tenant tenant, string action, string? oldValue, string? newValue, string? notes)
        {
            _dbContext.LeaseHistories.Add(new LeaseHistory
            {
                Tenant = tenant,
                TenantId = tenant.TenantId,
                Action = action,
                OldValue = oldValue,
                NewValue = newValue,
                Notes = notes,
                ChangedByEmail = GetCurrentUserEmail(),
                CreatedAt = DateTime.UtcNow
            });
        }

        private string GetCurrentUserEmail()
        {
            return User.Claims.FirstOrDefault(c => c.Type == "email")?.Value
                ?? User.FindFirstValue(ClaimTypes.Email)
                ?? User.Identity?.Name
                ?? string.Empty;
        }
    }
}
