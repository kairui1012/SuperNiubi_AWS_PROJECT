using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using MyMvcApp.Services;
using System.Linq;
using System.Security.Claims;

namespace MyMvcApp.Controllers
{
    [Authorize]
    public class LandlordController : Controller
    {
        private static readonly string[] AllowedAnnouncementAudiences = { "All", "Tenant", "Landlord" };
        private static readonly string[] AllowedPropertyImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly string[] AllowedMaintenanceRepairImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly string[] AllowedDocumentExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".txt", ".xlsx", ".xls" };
        private const long MaxPropertyImageSizeBytes = 8 * 1024 * 1024;
        private const long MaxMaintenanceRepairImageSizeBytes = 8 * 1024 * 1024;
        private const long MaxDocumentSizeBytes = 10 * 1024 * 1024;
        private readonly AppDbContext _dbContext;
        private readonly IS3ImageService _s3ImageService;
        private readonly EmailService _emailService;
        private readonly IWebHostEnvironment _environment;
        private readonly IAmazonS3 _s3Client;
        private readonly IConfiguration _configuration;

        public LandlordController(
            AppDbContext dbContext,
            IS3ImageService s3ImageService,
            EmailService emailService,
            IWebHostEnvironment environment,
            IAmazonS3 s3Client,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _s3ImageService = s3ImageService;
            _emailService = emailService;
            _environment = environment;
            _s3Client = s3Client;
            _configuration = configuration;
        }

        public async Task<IActionResult> Dashboard()
        {
            var userEmail = GetCurrentUserEmail();

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
                .Where(p => p.LandlordId == landlord.Id && !p.IsDeleted);

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
                VacantPropertiesCount = await propertiesQuery.CountAsync(p =>
                    !p.Tenants.Any(t => t.LeaseStatus == LeaseStatus.Active)),
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
            var userEmail = GetCurrentUserEmail();

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
                .Include(p => p.Tenants)
                .Where(p => p.LandlordId == landlord.Id && !p.IsDeleted)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            return View(properties);
        }

        [HttpGet]
        public IActionResult PropertyDetails(int id)
        {
            var userEmail = GetCurrentUserEmail();

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
                .Include(p => p.Amenities)
                .FirstOrDefault(p => p.PropertyId == id && p.LandlordId == landlord.Id && !p.IsDeleted);

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
        public async Task<IActionResult> AddProperty(Property model, IFormFile? PropertyImage, string? AmenitiesText)
        {
            var userEmail = GetCurrentUserEmail();

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
            model.ApprovalStatus = PropertyApprovalStatus.Pending;
            model.IsDeleted = false;
            NormalizeShortTermSettings(model);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                if (PropertyImage != null && PropertyImage.Length > 0)
                {
                    var uploadError = ValidatePropertyImage(PropertyImage);
                    if (uploadError != null)
                    {
                        ModelState.AddModelError(string.Empty, uploadError);
                        return View(model);
                    }

                    model.ImageUrl = await _s3ImageService.UploadImageAsync(PropertyImage, "landlord-properties");
                }

                SyncPropertyAmenities(model, AmenitiesText);
                _dbContext.Properties.Add(model);
                await _dbContext.SaveChangesAsync();

                TempData["SuccessMessage"] = "Property saved successfully and is pending admin approval.";
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
            var userEmail = GetCurrentUserEmail();

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
                .Include(p => p.Amenities)
                .FirstOrDefault(p => p.PropertyId == id && p.LandlordId == landlord.Id && !p.IsDeleted);

            if (property == null)
            {
                TempData["ErrorMessage"] = "Property not found.";
                return RedirectToAction("MyProperties");
            }

            ViewBag.AmenitiesText = string.Join(", ", property.Amenities.OrderBy(a => a.AmenityName).Select(a => a.AmenityName));
            return View(property);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProperty(Property model, IFormFile? PropertyImage, string? AmenitiesText)
        {
            var userEmail = GetCurrentUserEmail();

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
                .Include(p => p.Amenities)
                .FirstOrDefault(p => p.PropertyId == model.PropertyId && p.LandlordId == landlord.Id && !p.IsDeleted);

            if (existingProperty == null)
            {
                ModelState.AddModelError(string.Empty, "Property not found.");
                return View(model);
            }

            NormalizeShortTermSettings(model);

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
                existingProperty.AllowShortTerm = model.AllowShortTerm;
                existingProperty.DailyRate = model.DailyRate;
                existingProperty.ParkingBay = model.ParkingBay;
                existingProperty.Description = model.Description;
                existingProperty.AvailabilityStatus = model.AvailabilityStatus;
                existingProperty.ApprovalStatus = PropertyApprovalStatus.Pending;
                existingProperty.UpdatedAt = DateTime.UtcNow;

                if (PropertyImage != null && PropertyImage.Length > 0)
                {
                    var uploadError = ValidatePropertyImage(PropertyImage);
                    if (uploadError != null)
                    {
                        ModelState.AddModelError(string.Empty, uploadError);
                        ViewBag.AmenitiesText = AmenitiesText;
                        return View(model);
                    }

                    existingProperty.ImageUrl = await _s3ImageService.UploadImageAsync(PropertyImage, "landlord-properties");
                }

                SyncPropertyAmenities(existingProperty, AmenitiesText);
                await _dbContext.SaveChangesAsync();

                TempData["SuccessMessage"] = "Property updated successfully and is pending admin approval.";
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
            var userEmail = GetCurrentUserEmail();

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
                .Include(p => p.Tenants)
                .FirstOrDefault(p => p.PropertyId == id && p.LandlordId == landlord.Id && !p.IsDeleted);

            if (property == null)
            {
                TempData["ErrorMessage"] = "Property not found.";
                return RedirectToAction("MyProperties");
            }

            try
            {
                var hasActiveTenant = _dbContext.Tenants.Any(t =>
                    t.PropertyId == id &&
                    t.LeaseStatus == LeaseStatus.Active);

                if (hasActiveTenant)
                {
                    TempData["ErrorMessage"] = "Cannot delete a property with an active tenant.";
                    return RedirectToAction("MyProperties");
                }

                property.IsDeleted = true;
                property.DeletedAt = DateTime.UtcNow;
                property.UpdatedAt = DateTime.UtcNow;
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
            var userEmail = GetCurrentUserEmail();

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
            result.Tenant.Property.AvailabilityStatus = PropertyAvailabilityStatus.Available;
            result.Tenant.Property.UpdatedAt = DateTime.UtcNow;
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
                t.LeaseStatus == LeaseStatus.Active &&
                t.TenantId != model.TenantId);

            if (propertyOccupied)
            {
                TempData["ErrorMessage"] = "Selected property already has an assigned tenant.";
                return RedirectToAction("TenantDetails", new { id = model.TenantId });
            }

            var oldValue = $"Property: {result.Tenant.Property.PropertyName}";

            result.Tenant.Property.AvailabilityStatus = PropertyAvailabilityStatus.Available;
            result.Tenant.Property.UpdatedAt = DateTime.UtcNow;
            property.AvailabilityStatus = PropertyAvailabilityStatus.Occupied;
            property.UpdatedAt = DateTime.UtcNow;
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
            var userEmail = GetCurrentUserEmail();

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
            var userEmail = GetCurrentUserEmail();

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
                .Include(m => m.Timeline)
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
        public async Task<IActionResult> EditMaintenanceRequest(MaintenanceRequest model, IFormFile? RepairImage)
        {
            var userEmail = GetCurrentUserEmail();

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
                .Include(m => m.Tenant)
                .ThenInclude(t => t.User)
                .FirstOrDefault(m =>
                    m.RequestId == model.RequestId &&
                    m.Property.LandlordId == landlord.Id);

            if (existingRequest == null)
            {
                TempData["ErrorMessage"] = "Maintenance request not found.";
                return RedirectToAction("MaintenanceRequests");
            }

            var oldStatus = existingRequest.Status;
            var oldVendor = existingRequest.AssignedVendor;
            var oldCost = existingRequest.EstimatedRepairCost;

            existingRequest.Priority = model.Priority;
            existingRequest.Status = model.Status;
            existingRequest.LandlordRemarks = model.LandlordRemarks;
            existingRequest.AssignedVendor = model.AssignedVendor;
            existingRequest.EstimatedRepairCost = model.EstimatedRepairCost;
            existingRequest.UpdatedAt = DateTime.UtcNow;

            if (RepairImage != null && RepairImage.Length > 0)
            {
                var uploadError = ValidateRepairImage(RepairImage);
                if (uploadError != null)
                {
                    TempData["ErrorMessage"] = uploadError;
                    return RedirectToAction("EditMaintenanceRequest", new { id = model.RequestId });
                }

                existingRequest.RepairImageKey = await SaveRepairImageAsync(RepairImage, existingRequest);
                AddMaintenanceTimeline(existingRequest, "Repair image uploaded", "Landlord uploaded a repair image.");
            }

            if (model.Status == MaintenanceStatus.Completed)
            {
                existingRequest.ResolvedDate = DateTime.UtcNow;
            }
            else
            {
                existingRequest.ResolvedDate = null;
            }

            if (oldStatus != existingRequest.Status)
            {
                AddMaintenanceTimeline(existingRequest, "Status changed", $"{oldStatus} -> {existingRequest.Status}");
            }

            if (!string.Equals(oldVendor, existingRequest.AssignedVendor, StringComparison.Ordinal))
            {
                AddMaintenanceTimeline(existingRequest, "Vendor assigned", string.IsNullOrWhiteSpace(existingRequest.AssignedVendor)
                    ? "Vendor cleared."
                    : $"Assigned to {existingRequest.AssignedVendor}.");
            }

            if (oldCost != existingRequest.EstimatedRepairCost)
            {
                AddMaintenanceTimeline(existingRequest, "Repair cost estimated", existingRequest.EstimatedRepairCost.HasValue
                    ? $"Estimated cost: RM {existingRequest.EstimatedRepairCost:N2}."
                    : "Estimated cost cleared.");
            }

            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Maintenance request updated successfully.";

            if (oldStatus != existingRequest.Status)
            {
                try
                {
                    await _emailService.SendMaintenanceStatusChangedEmailAsync(existingRequest, landlord.Email);
                    TempData["SuccessMessage"] = "Maintenance request updated successfully and tenant email notification sent.";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Maintenance email failed: {ex.Message}");
                    TempData["SuccessMessage"] = "Maintenance request updated successfully, but tenant email notification failed to send.";
                }
            }

            return RedirectToAction("MaintenanceRequests");
        }

        public IActionResult Payments()
        {
            var userEmail = GetCurrentUserEmail();

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

        public async Task<IActionResult> Documents()
        {
            var landlord = GetCurrentLandlord();

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return View(new LandlordDocumentsViewModel());
            }

            return View(await BuildLandlordDocumentsViewModelAsync(landlord.Id));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDocument(LandlordDocumentsViewModel model)
        {
            var landlord = GetCurrentLandlord();

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction(nameof(Documents));
            }

            if (model.NewDocument.PropertyId == null && model.NewDocument.TenantId == null)
            {
                ModelState.AddModelError("NewDocument.PropertyId", "Please select a property or tenant.");
            }

            Tenant? tenant = null;
            Property? property = null;

            if (model.NewDocument.TenantId.HasValue)
            {
                tenant = await _dbContext.Tenants
                    .Include(t => t.Property)
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t =>
                        t.TenantId == model.NewDocument.TenantId.Value &&
                        t.Property.LandlordId == landlord.Id);

                if (tenant == null)
                {
                    ModelState.AddModelError("NewDocument.TenantId", "Selected tenant is invalid.");
                }
                else
                {
                    property = tenant.Property;
                }
            }

            if (model.NewDocument.PropertyId.HasValue)
            {
                var selectedProperty = await _dbContext.Properties
                    .FirstOrDefaultAsync(p =>
                        p.PropertyId == model.NewDocument.PropertyId.Value &&
                        p.LandlordId == landlord.Id &&
                        !p.IsDeleted);

                if (selectedProperty == null)
                {
                    ModelState.AddModelError("NewDocument.PropertyId", "Selected property is invalid.");
                }
                else if (property != null && selectedProperty.PropertyId != property.PropertyId)
                {
                    ModelState.AddModelError("NewDocument.PropertyId", "Selected property does not match the tenant.");
                }
                else
                {
                    property = selectedProperty;
                }
            }

            var file = model.NewDocument.File;
            if (file is null || file.Length <= 0)
            {
                ModelState.AddModelError("NewDocument.File", "Please choose a valid file.");
            }
            else
            {
                var validationError = ValidateDocumentFile(file);
                if (validationError != null)
                {
                    ModelState.AddModelError("NewDocument.File", validationError);
                }
            }

            if (!ModelState.IsValid)
            {
                var invalidModel = await BuildLandlordDocumentsViewModelAsync(landlord.Id, model.NewDocument);
                return View(nameof(Documents), invalidModel);
            }

            var extension = Path.GetExtension(file!.FileName).ToLowerInvariant();
            var savedFileName = $"{Guid.NewGuid():N}{extension}";
            var key = $"landlord/{landlord.Id}/documents/{savedFileName}";

            var bucketName = _configuration["AWS:BucketName"];
            var region = _configuration["AWS:Region"] ?? "us-east-1";
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                ModelState.AddModelError("NewDocument.File", "S3 bucket is not configured.");
                var invalidModel = await BuildLandlordDocumentsViewModelAsync(landlord.Id, model.NewDocument);
                return View(nameof(Documents), invalidModel);
            }

            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            memory.Position = 0;

            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                InputStream = memory,
                ContentType = file.ContentType
            };

            await _s3Client.PutObjectAsync(putRequest);

            var fileKey = key;
            var s3Url = $"https://{bucketName}.s3.{region}.amazonaws.com/{fileKey}";

            var document = new Document
            {
                UploadedBy = landlord.Id,
                PropertyId = property?.PropertyId,
                TenantId = tenant?.TenantId,
                DocumentName = model.NewDocument.DocumentName.Trim(),
                DocumentType = model.NewDocument.DocumentType ?? DocumentType.Others,
                FileKey = fileKey,
                FileSize = (int)Math.Min(file.Length, int.MaxValue),
                FileType = file.ContentType,
                S3Url = s3Url,
                Notes = model.NewDocument.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Documents.Add(document);
            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Document uploaded successfully.";
            return RedirectToAction(nameof(Documents));
        }

        public async Task<IActionResult> DownloadDocument(int id)
        {
            var landlord = GetCurrentLandlord();

            if (landlord == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var document = await GetManagedDocumentAsync(id, landlord.Id);

            if (document == null)
            {
                return NotFound();
            }

            var bucketName = _configuration["AWS:BucketName"];
            if (!string.IsNullOrWhiteSpace(document.FileKey) && !string.IsNullOrWhiteSpace(bucketName))
            {
                try
                {
                    var presignRequest = new GetPreSignedUrlRequest
                    {
                        BucketName = bucketName,
                        Key = document.FileKey,
                        Expires = DateTime.UtcNow.AddMinutes(15),
                        Verb = HttpVerb.GET
                    };

                    var url = _s3Client.GetPreSignedURL(presignRequest);
                    return Redirect(url);
                }
                catch (Exception)
                {
                    // fall back to S3Url or local file
                }
            }

            if (!string.IsNullOrWhiteSpace(document.S3Url) && Uri.IsWellFormedUriString(document.S3Url, UriKind.Absolute))
            {
                return Redirect(document.S3Url);
            }

            var relativeFileKey = document.FileKey.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var physicalPath = Path.Combine(_environment.WebRootPath, relativeFileKey);

            if (!System.IO.File.Exists(physicalPath))
            {
                return NotFound();
            }

            var provider = new FileExtensionContentTypeProvider();
            var contentType = provider.TryGetContentType(document.FileKey, out var detected)
                ? detected
                : "application/octet-stream";

            return PhysicalFile(physicalPath, contentType, enableRangeProcessing: true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var landlord = GetCurrentLandlord();

            if (landlord == null)
            {
                TempData["ErrorMessage"] = "Landlord account not found.";
                return RedirectToAction(nameof(Documents));
            }

            var document = await GetManagedDocumentAsync(id, landlord.Id);

            if (document == null)
            {
                TempData["ErrorMessage"] = "Document not found.";
                return RedirectToAction(nameof(Documents));
            }

            document.IsDeleted = true;
            document.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Document moved to archive.";
            return RedirectToAction(nameof(Documents));
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
            var userEmail = GetCurrentUserEmail();

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
            var userEmail = GetCurrentUserEmail();

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
                p.LandlordId == landlord.Id &&
                !p.IsDeleted &&
                p.ApprovalStatus == PropertyApprovalStatus.Approved &&
                p.AvailabilityStatus != PropertyAvailabilityStatus.Maintenance &&
                p.AvailabilityStatus != PropertyAvailabilityStatus.Unavailable);

            if (selectedProperty == null)
            {
                ModelState.AddModelError("PropertyId", "Selected property is invalid.");
            }

            var tenantAlreadyAssigned = _dbContext.Tenants.Any(t =>
                t.UserId == model.UserId &&
                t.LeaseStatus == LeaseStatus.Active);

            if (tenantAlreadyAssigned)
            {
                ModelState.AddModelError("UserId", "This tenant has already been assigned to a property.");
            }

            var propertyAlreadyAssigned = _dbContext.Tenants.Any(t =>
                t.PropertyId == model.PropertyId &&
                t.LeaseStatus == LeaseStatus.Active);

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
                    $"Property: {selectedProperty!.PropertyName}; Start: {tenant.LeaseStartDate:yyyy-MM-dd}; End: {tenant.LeaseEndDate:yyyy-MM-dd}; Monthly rent: RM {tenant.MonthlyRent:N2}; Deposit: {tenant.DepositStatus}",
                    tenant.Notes);
                selectedProperty.AvailabilityStatus = PropertyAvailabilityStatus.Occupied;
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
                    !_dbContext.Tenants.Any(t =>
                        t.UserId == u.Id &&
                        t.LeaseStatus == LeaseStatus.Active))
                .Select(u => new SelectListItem
                {
                    Value = u.Id.ToString(),
                    Text = u.Email
                })
                .ToList();

            model.Properties = _dbContext.Properties
                .Where(p =>
                    p.LandlordId == landlordId &&
                    !p.IsDeleted &&
                    p.ApprovalStatus == PropertyApprovalStatus.Approved &&
                    p.AvailabilityStatus != PropertyAvailabilityStatus.Maintenance &&
                    p.AvailabilityStatus != PropertyAvailabilityStatus.Unavailable &&
                    !_dbContext.Tenants.Any(t =>
                        t.PropertyId == p.PropertyId &&
                        t.LeaseStatus == LeaseStatus.Active))
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
                    !p.IsDeleted &&
                    p.ApprovalStatus == PropertyApprovalStatus.Approved &&
                    (p.PropertyId == currentPropertyId ||
                        (p.AvailabilityStatus != PropertyAvailabilityStatus.Maintenance &&
                            p.AvailabilityStatus != PropertyAvailabilityStatus.Unavailable &&
                            !_dbContext.Tenants.Any(t =>
                                t.PropertyId == p.PropertyId &&
                                t.LeaseStatus == LeaseStatus.Active))))
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

        private async Task<LandlordDocumentsViewModel> BuildLandlordDocumentsViewModelAsync(
            int landlordId,
            CreateLandlordDocumentViewModel? newDocument = null)
        {
            var documents = await _dbContext.Documents
                .AsNoTracking()
                .Include(d => d.Property)
                .Include(d => d.Tenant)
                .ThenInclude(t => t!.User)
                .Include(d => d.UploadedByUser)
                .Where(d => !d.IsDeleted &&
                    ((d.Property != null && d.Property.LandlordId == landlordId) ||
                     (d.Tenant != null && d.Tenant.Property.LandlordId == landlordId) ||
                     d.UploadedBy == landlordId))
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            var properties = await _dbContext.Properties
                .AsNoTracking()
                .Where(p => p.LandlordId == landlordId && !p.IsDeleted)
                .OrderBy(p => p.PropertyName)
                .Select(p => new SelectListItem
                {
                    Value = p.PropertyId.ToString(),
                    Text = p.PropertyName
                })
                .ToListAsync();

            var tenants = await _dbContext.Tenants
                .AsNoTracking()
                .Include(t => t.User)
                .Include(t => t.Property)
                .Where(t => t.Property.LandlordId == landlordId)
                .OrderBy(t => t.User.Email)
                .Select(t => new SelectListItem
                {
                    Value = t.TenantId.ToString(),
                    Text = $"{t.User.Email} - {t.Property.PropertyName}"
                })
                .ToListAsync();

            return new LandlordDocumentsViewModel
            {
                Documents = documents,
                NewDocument = newDocument ?? new CreateLandlordDocumentViewModel(),
                PropertyOptions = properties,
                TenantOptions = tenants
            };
        }

        private async Task<Document?> GetManagedDocumentAsync(int documentId, int landlordId)
        {
            return await _dbContext.Documents
                .Include(d => d.Property)
                .Include(d => d.Tenant)
                .ThenInclude(t => t!.Property)
                .FirstOrDefaultAsync(d =>
                    d.DocumentId == documentId &&
                    !d.IsDeleted &&
                    ((d.Property != null && d.Property.LandlordId == landlordId) ||
                     (d.Tenant != null && d.Tenant.Property.LandlordId == landlordId) ||
                     d.UploadedBy == landlordId));
        }

        private static string? ValidateDocumentFile(IFormFile file)
        {
            if (file.Length > MaxDocumentSizeBytes)
            {
                return "File size must not exceed 10MB.";
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            return AllowedDocumentExtensions.Contains(extension)
                ? null
                : "Allowed file types: PDF, JPG, JPEG, PNG, DOC, DOCX, TXT, XLS, XLSX.";
        }

        private static string? ValidatePropertyImage(IFormFile file)
        {
            if (file.Length > MaxPropertyImageSizeBytes)
            {
                return "Property image must be 8MB or smaller.";
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            return AllowedPropertyImageExtensions.Contains(extension)
                ? null
                : "Only JPG, JPEG, PNG, and WEBP property images are allowed.";
        }

        private void NormalizeShortTermSettings(Property property)
        {
            if (!property.AllowShortTerm)
            {
                property.DailyRate = null;
                return;
            }

            if (!property.DailyRate.HasValue || property.DailyRate.Value <= 0)
            {
                ModelState.AddModelError(nameof(Property.DailyRate), "Daily rate is required when short-term stays are enabled.");
            }
        }

        private static string? ValidateRepairImage(IFormFile file)
        {
            if (file.Length > MaxMaintenanceRepairImageSizeBytes)
            {
                return "Repair image must be 8MB or smaller.";
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            return AllowedMaintenanceRepairImageExtensions.Contains(extension)
                ? null
                : "Only JPG, JPEG, PNG, and WEBP repair images are allowed.";
        }

        private async Task<string> SaveRepairImageAsync(IFormFile file, MaintenanceRequest request)
        {
            var uploadsFolder = Path.Combine("wwwroot", "uploads", "landlord", "maintenance", request.RequestId.ToString());
            Directory.CreateDirectory(uploadsFolder);

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var savedFileName = $"{Guid.NewGuid():N}{extension}";
            var physicalPath = Path.Combine(uploadsFolder, savedFileName);

            await using var stream = System.IO.File.Create(physicalPath);
            await file.CopyToAsync(stream);

            return Path.Combine("uploads", "landlord", "maintenance", request.RequestId.ToString(), savedFileName)
                .Replace("\\", "/");
        }

        private void AddMaintenanceTimeline(MaintenanceRequest request, string action, string? details)
        {
            _dbContext.MaintenanceTimelines.Add(new MaintenanceTimeline
            {
                MaintenanceRequest = request,
                RequestId = request.RequestId,
                Action = action,
                Details = details,
                ActorEmail = GetCurrentUserEmail(),
                CreatedAt = DateTime.UtcNow
            });
        }

        private void SyncPropertyAmenities(Property property, string? amenitiesText)
        {
            property.Amenities.Clear();

            if (string.IsNullOrWhiteSpace(amenitiesText))
            {
                return;
            }

            var amenities = amenitiesText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .Select(a => new PropertyAmenity
                {
                    Property = property,
                    AmenityName = a.Length > 100 ? a[..100] : a
                });

            foreach (var amenity in amenities)
            {
                property.Amenities.Add(amenity);
            }
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

