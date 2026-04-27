using Amazon.Extensions.CognitoAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;

namespace MyMvcApp.Controllers

{
    [Authorize] // Ensures only logged-in users can reach this page
    public class TenantController : Controller
    {
        private readonly UserManager<CognitoUser> _userManager;
        private readonly AppDbContext _context;

        public TenantController(AppDbContext context, UserManager<CognitoUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Tenant()
        {
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? User.Identity?.Name;

            var tenantData = await _context.Tenants
                .Include(t => t.User)     // Link 'User' Table
                .Include(t => t.Property) // Link 'Property' Table
                .Include(t => t.MaintenanceRequests) // Link 'Property' Table
                .Include(t => t.Documents)
                .Include(t => t.Payments)
                .FirstOrDefaultAsync(t => t.User.Email == email);

            if (tenantData == null) return NotFound();

            var viewModel = new TenantDashboardViewModel
            {
                TenantEmail = tenantData.User.Email,
                PropertyName = tenantData.Property.PropertyName,
                PropertyAddress = tenantData.Property.AddressLine1,
                LeaseStartDate = tenantData.LeaseStartDate,
                LeaseEndDate = tenantData.LeaseEndDate,
                LeaseStatus = tenantData.LeaseStatus.ToString(),
                MaintenanceRequest = tenantData.MaintenanceRequests.ToList(),
                PaymentRecord = tenantData.Payments.Count,  
                DocumentQuantity = tenantData.Documents.Count, 
                MonthlyRent = tenantData.MonthlyRent
            };

            return View(viewModel);
        }

        public async Task<IActionResult> MyProperty()
        {

			return View();
        }

        public async Task<IActionResult> MaintenanceRequest()
        {
			var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
				?? User.Identity?.Name;

			// Get all maintenance requests
			var requests = await _context.MaintenanceRequests
				.Include(r => r.Property)
				.Where(r => r.Tenant.User.Email == email)
				.OrderByDescending(r => r.CreatedAt)
				.ToListAsync();

			var viewModel = new MaintenanceRequestViewModel
			{
				Requests = requests,
			};

			return View(viewModel);
		}

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMaintenance(MaintenanceRequestViewModel viewModel)
        {
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

            if (!ModelState.IsValid)
            {
                viewModel.Requests = await _context.MaintenanceRequests
                    .Where(r => r.Tenant.User.Email == email)
                    .OrderByDescending(r => r.CreatedAt)
                    .ToListAsync();

                return View("MaintenanceRequest", viewModel);
            }

            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.User.Email == email);

            if (tenant == null) return NotFound();

            var newRequest = new MaintenanceRequest
            {
                TenantId = tenant.TenantId,
                PropertyId = tenant.PropertyId,
                Title = viewModel.NewRequest.Title,
                Category = viewModel.NewRequest.Category,
                Priority = viewModel.NewRequest.Priority,
                Description = viewModel.NewRequest.Description,
                PreferredDate = DateTime.SpecifyKind(viewModel.NewRequest.PreferredDate.Value, DateTimeKind.Utc),
                Status = MaintenanceStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.MaintenanceRequests.Add(newRequest);
            await _context.SaveChangesAsync();

            return RedirectToAction("Tenant"); 
        }
        public async Task<IActionResult> Documents()
        { 
            return View();
        }

        public async Task<IActionResult> Payments()
        {
            return View();
        }
    }
}