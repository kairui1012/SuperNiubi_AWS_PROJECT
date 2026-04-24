using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using MyMvcApp.Models.Admin;
using MyMvcApp.Services;
using Amazon.CognitoIdentityProvider; // ADD THIS
using Amazon.CognitoIdentityProvider.Model; // ADD THIS
using System.Security.Claims;

namespace MyMvcApp.Controllers
{
    [Authorize(Roles = "Admin")] // Optional: secures this controller
    public class AdminController : Controller
    {
        private static readonly string[] AllowedRoles = { "Tenant", "Landlord", "Admin" };
        private static readonly string[] AllowedStatuses = { "Pending", "Approved", "Disabled" };

        private readonly AppDbContext _dbContext;
        private readonly EmailService _emailService;
        private readonly IAmazonCognitoIdentityProvider _cognitoClient;
        private readonly IConfiguration _config;

        // Inject the Cognito Client and Configuration
        public AdminController(AppDbContext dbContext, EmailService emailService, IAmazonCognitoIdentityProvider cognitoClient, IConfiguration config)
        {
            _dbContext = dbContext;
            _emailService = emailService;
            _cognitoClient = cognitoClient;
            _config = config;
        }

        public async Task<IActionResult> Admin(string? searchEmail, string? roleFilter, string? statusFilter)
        {
            var normalizedSearchEmail = searchEmail?.Trim() ?? string.Empty;
            var normalizedRoleFilter = NormalizeFilter(roleFilter, AllowedRoles);
            var normalizedStatusFilter = NormalizeFilter(statusFilter, AllowedStatuses);

            var usersQuery = _dbContext.Users.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(normalizedSearchEmail))
            {
                usersQuery = usersQuery.Where(u => u.Email.Contains(normalizedSearchEmail));
            }

            if (!string.IsNullOrWhiteSpace(normalizedRoleFilter))
            {
                usersQuery = usersQuery.Where(u => u.Role == normalizedRoleFilter);
            }

            usersQuery = normalizedStatusFilter switch
            {
                "Pending" => usersQuery.Where(u => !u.IsApproved && !u.IsDisabled),
                "Approved" => usersQuery.Where(u => u.IsApproved && !u.IsDisabled),
                "Disabled" => usersQuery.Where(u => u.IsDisabled),
                _ => usersQuery
            };

            var utcNow = DateTime.UtcNow;
            var currentMonthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var nextMonthStart = currentMonthStart.AddMonths(1);

            var totalProperties = await _dbContext.Properties.AsNoTracking().CountAsync();
            var occupiedProperties = await _dbContext.Tenants.AsNoTracking()
                .Select(t => t.PropertyId)
                .Distinct()
                .CountAsync();
            var vacantProperties = Math.Max(totalProperties - occupiedProperties, 0);

            var totalUsers = await _dbContext.Users.AsNoTracking().CountAsync();
            var approvedUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => u.IsApproved && !u.IsDisabled);
            var pendingUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => !u.IsApproved && !u.IsDisabled);
            var disabledUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => u.IsDisabled);
            var activeTenancies = await _dbContext.Tenants.AsNoTracking().CountAsync(t => t.LeaseStatus == LeaseStatus.Active);
            var totalMaintenanceRequests = await _dbContext.MaintenanceRequests.AsNoTracking().CountAsync();
            var openMaintenanceRequests = await _dbContext.MaintenanceRequests.AsNoTracking()
                .CountAsync(m => m.Status == MaintenanceStatus.Pending
                    || m.Status == MaintenanceStatus.Approved
                    || m.Status == MaintenanceStatus.InProgress);
            var totalDocuments = await _dbContext.Documents.AsNoTracking().CountAsync(d => !d.IsDeleted);
            var overduePayments = await _dbContext.Payments.AsNoTracking().CountAsync(p => p.Status == PaymentStatus.Overdue);

            var model = new AdminDashboardViewModel
            {
                SearchEmail = normalizedSearchEmail,
                RoleFilter = normalizedRoleFilter ?? string.Empty,
                StatusFilter = normalizedStatusFilter ?? string.Empty,
                Users = await usersQuery
                    .OrderBy(u => u.IsDisabled)
                    .ThenBy(u => u.IsApproved)
                    .ThenBy(u => u.Email)
                    .ToListAsync(),
                Overview = new AdminOverviewViewModel
                {
                    TotalUsers = totalUsers,
                    ApprovedUsers = approvedUsers,
                    PendingUsers = pendingUsers,
                    DisabledUsers = disabledUsers,
                    TotalProperties = totalProperties,
                    OccupiedProperties = occupiedProperties,
                    VacantProperties = vacantProperties,
                    ActiveTenancies = activeTenancies,
                    TotalMaintenanceRequests = totalMaintenanceRequests,
                    OpenMaintenanceRequests = openMaintenanceRequests,
                    TotalDocuments = totalDocuments,
                    OverduePayments = overduePayments
                },
                UserReport = new AdminUserReportViewModel
                {
                    AdminCount = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Role == "Admin"),
                    LandlordCount = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Role == "Landlord"),
                    TenantCount = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Role == "Tenant")
                },
                PropertyReport = new AdminPropertyReportViewModel
                {
                    TotalProperties = totalProperties,
                    OccupiedProperties = occupiedProperties,
                    VacantProperties = vacantProperties,
                    ActiveTenancies = activeTenancies
                },
                MaintenanceReport = new AdminMaintenanceReportViewModel
                {
                    PendingCount = await _dbContext.MaintenanceRequests.AsNoTracking().CountAsync(m => m.Status == MaintenanceStatus.Pending),
                    ApprovedCount = await _dbContext.MaintenanceRequests.AsNoTracking().CountAsync(m => m.Status == MaintenanceStatus.Approved),
                    InProgressCount = await _dbContext.MaintenanceRequests.AsNoTracking().CountAsync(m => m.Status == MaintenanceStatus.InProgress),
                    CompletedCount = await _dbContext.MaintenanceRequests.AsNoTracking().CountAsync(m => m.Status == MaintenanceStatus.Completed),
                    RejectedCount = await _dbContext.MaintenanceRequests.AsNoTracking().CountAsync(m => m.Status == MaintenanceStatus.Rejected),
                    HighPriorityOpenCount = await _dbContext.MaintenanceRequests.AsNoTracking()
                        .CountAsync(m => m.Priority == MaintenancePriority.High
                            && (m.Status == MaintenanceStatus.Pending
                                || m.Status == MaintenanceStatus.Approved
                                || m.Status == MaintenanceStatus.InProgress))
                },
                PaymentReport = new AdminPaymentReportViewModel
                {
                    PendingCount = await _dbContext.Payments.AsNoTracking().CountAsync(p => p.Status == PaymentStatus.Pending),
                    SubmittedCount = await _dbContext.Payments.AsNoTracking().CountAsync(p => p.Status == PaymentStatus.Submitted),
                    VerifiedCount = await _dbContext.Payments.AsNoTracking().CountAsync(p => p.Status == PaymentStatus.Verified),
                    OverdueCount = overduePayments,
                    RejectedCount = await _dbContext.Payments.AsNoTracking().CountAsync(p => p.Status == PaymentStatus.Rejected),
                    TotalVerifiedAmount = await _dbContext.Payments.AsNoTracking()
                        .Where(p => p.Status == PaymentStatus.Verified)
                        .SumAsync(p => (decimal?)p.Amount) ?? 0m,
                    CurrentMonthVerifiedAmount = await _dbContext.Payments.AsNoTracking()
                        .Where(p => p.Status == PaymentStatus.Verified
                            && p.PaymentDate >= currentMonthStart
                            && p.PaymentDate < nextMonthStart)
                        .SumAsync(p => (decimal?)p.Amount) ?? 0m
                },
                LatestUsers = await _dbContext.Users.AsNoTracking()
                    .OrderByDescending(u => u.Id)
                    .Take(5)
                    .Select(u => new AdminLatestUserViewModel
                    {
                        Id = u.Id,
                        Email = u.Email,
                        Role = u.Role,
                        IsApproved = u.IsApproved,
                        IsDisabled = u.IsDisabled
                    })
                    .ToListAsync(),
                RecentMaintenanceRequests = await _dbContext.MaintenanceRequests.AsNoTracking()
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(5)
                    .Select(m => new AdminRecentMaintenanceViewModel
                    {
                        RequestId = m.RequestId,
                        Title = m.Title,
                        PropertyName = m.Property.PropertyName,
                        TenantEmail = m.Tenant.User.Email,
                        Status = m.Status,
                        Priority = m.Priority,
                        CreatedAt = m.CreatedAt
                    })
                    .ToListAsync(),
                RecentPayments = await _dbContext.Payments.AsNoTracking()
                    .OrderByDescending(p => p.CreatedAt)
                    .Take(5)
                    .Select(p => new AdminRecentPaymentViewModel
                    {
                        PaymentId = p.PaymentId,
                        TenantEmail = p.Tenant.User.Email,
                        PropertyName = p.Property.PropertyName,
                        PaymentMonth = p.PaymentMonth,
                        PaymentYear = p.PaymentYear,
                        Amount = p.Amount,
                        Status = p.Status,
                        CreatedAt = p.CreatedAt
                    })
                    .ToListAsync()
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user != null && !user.IsApproved)
            {
                try
                {
                    var userPoolId = _config["AWS:UserPoolId"];
                    var confirmRequest = new AdminConfirmSignUpRequest
                    {
                        UserPoolId = userPoolId,
                        Username = user.Email
                    };
                    await _cognitoClient.AdminConfirmSignUpAsync(confirmRequest);
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Failed to confirm in Cognito: {ex.Message}";
                    return RedirectToAction(nameof(Admin));
                }

                user.IsApproved = true;
                await _dbContext.SaveChangesAsync();
                
                try 
                {
                    // Updated: Removed the nickname parameter
                    await _emailService.SendApprovalEmailAsync(user.Email);
                    TempData["SuccessMessage"] = "User approved and email sent!";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Email Failed: {ex.Message}");
                    TempData["SuccessMessage"] = "User approved, but the notification email failed to send.";
                }
            }
            return RedirectToAction(nameof(Admin));
        }

        [HttpPost]
        public async Task<IActionResult> DisableUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user != null && !user.IsDisabled)
            {
                if (IsCurrentUser(user.Email))
                {
                    TempData["ErrorMessage"] = "You cannot disable your own admin account.";
                    return RedirectToAction(nameof(Admin));
                }

                try
                {
                    // Disable the user inside AWS Cognito to completely revoke access
                    var userPoolId = _config["AWS:UserPoolId"];
                    var disableRequest = new AdminDisableUserRequest
                    {
                        UserPoolId = userPoolId,
                        Username = user.Email
                    };
                    await _cognitoClient.AdminDisableUserAsync(disableRequest);
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Failed to disable in Cognito: {ex.Message}";
                    return RedirectToAction(nameof(Admin));
                }

                // Mark as disabled in Neon DB
                user.IsDisabled = true;
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = "User has been disabled successfully.";
            }
            return RedirectToAction(nameof(Admin));
        }

        [HttpPost]
        public async Task<IActionResult> EnableUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user != null && user.IsDisabled)
            {
                try
                {
                    // 1. Enable the user inside AWS Cognito
                    var userPoolId = _config["AWS:UserPoolId"];
                    var enableRequest = new AdminEnableUserRequest
                    {
                        UserPoolId = userPoolId,
                        Username = user.Email
                    };
                    await _cognitoClient.AdminEnableUserAsync(enableRequest);
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Failed to enable in Cognito: {ex.Message}";
                    return RedirectToAction(nameof(Admin));
                }

                // 2. Mark as enabled in Neon DB
                user.IsDisabled = false;
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = "User has been enabled successfully.";
            }
            return RedirectToAction(nameof(Admin));
        }

        [HttpPost]
        public async Task<IActionResult> ChangeRole(int id, string? newRole)
        {
            var user = await _dbContext.Users.FindAsync(id);
            var normalizedRole = NormalizeFilter(newRole, AllowedRoles);

            if (user != null)
            {
                if (string.IsNullOrWhiteSpace(normalizedRole))
                {
                    TempData["ErrorMessage"] = "Invalid role selected.";
                    return RedirectToAction(nameof(Admin));
                }

                if (IsCurrentUser(user.Email) && !string.Equals(normalizedRole, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["ErrorMessage"] = "You cannot remove your own admin role.";
                    return RedirectToAction(nameof(Admin));
                }

                user.Role = normalizedRole;
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Role updated to {normalizedRole}.";
            }

            return RedirectToAction(nameof(Admin));
        }

        private static string? NormalizeFilter(string? value, IEnumerable<string> allowedValues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return allowedValues.FirstOrDefault(allowedValue =>
                string.Equals(allowedValue, value.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private bool IsCurrentUser(string email)
        {
            var currentUserEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
            return !string.IsNullOrWhiteSpace(currentUserEmail)
                && string.Equals(currentUserEmail, email, StringComparison.OrdinalIgnoreCase);
        }
    }
}
