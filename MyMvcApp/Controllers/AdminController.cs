using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using MyMvcApp.Models.Admin;
using MyMvcApp.Services;
using Amazon.CognitoIdentityProvider; // ADD THIS
using Amazon.CognitoIdentityProvider.Model; // ADD THIS
using System.Globalization;
using System.Security.Claims;

namespace MyMvcApp.Controllers
{
    [Authorize(Roles = "Admin")] // Optional: secures this controller
    public class AdminController : Controller
    {
        private static readonly string[] AllowedRoles = { "Tenant", "Landlord", "Security", "Admin" };
        private static readonly string[] AllowedStatuses = { "Pending", "Approved", "Disabled" };
        private static readonly string[] AllowedAdminPanes = { "dashboard", "users", "properties", "maintenance", "payments", "audit", "announcements" };
        private static readonly string[] AllowedMaintenanceStatuses = { "Pending", "Approved", "InProgress", "Completed", "Rejected" };
        private static readonly string[] AllowedMaintenancePriorities = { "High", "Medium", "Low" };
        private static readonly string[] AllowedVisibleTo = { "All", "Tenant", "Landlord" };
        private static readonly string[] AllowedAuditActions =
        {
            "ApproveUser",
            "DisableUser",
            "EnableUser",
            "ChangeRole",
            "ApprovePasswordReset",
            "RejectPasswordReset",
            "CreateAnnouncement",
            "EditAnnouncement",
            "DeleteAnnouncement",
            "VerifyPayment",
            "RejectPayment",
            "ExportPaymentReport",
            "ApproveProperty",
            "RejectProperty"
        };

        private readonly AppDbContext _dbContext;
        private readonly EmailService _emailService;
        private readonly IAmazonCognitoIdentityProvider _cognitoClient;
        private readonly IConfiguration _config;
        private readonly ILogger<AdminController> _logger;

        // Inject the Cognito Client and Configuration
        public AdminController(
            AppDbContext dbContext,
            EmailService emailService,
            IAmazonCognitoIdentityProvider cognitoClient,
            IConfiguration config,
            ILogger<AdminController> logger)
        {
            _dbContext = dbContext;
            _emailService = emailService;
            _cognitoClient = cognitoClient;
            _config = config;
            _logger = logger;
        }

        private string GetConfiguredUserPoolId()
        {
            return _config["AWS:UserPoolId"]
                ?? throw new InvalidOperationException("AWS:UserPoolId is not configured.");
        }

        private async Task<UserStatusType> EnsureCognitoEmailCanReceiveAccountRecoveryAsync(string userPoolId, string email)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var cognitoUser = await _cognitoClient.AdminGetUserAsync(new AdminGetUserRequest
            {
                UserPoolId = userPoolId,
                Username = normalizedEmail
            });

            var existingEmail = cognitoUser.UserAttributes
                .FirstOrDefault(attribute => attribute.Name == "email")?.Value;
            var emailVerified = string.Equals(
                cognitoUser.UserAttributes.FirstOrDefault(attribute => attribute.Name == "email_verified")?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase);

            var attributesToUpdate = new List<AttributeType>();

            if (!string.Equals(existingEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                attributesToUpdate.Add(new AttributeType
                {
                    Name = "email",
                    Value = normalizedEmail
                });
            }

            if (!emailVerified)
            {
                attributesToUpdate.Add(new AttributeType
                {
                    Name = "email_verified",
                    Value = "true"
                });
            }

            if (attributesToUpdate.Count == 0)
            {
                return cognitoUser.UserStatus;
            }

            await _cognitoClient.AdminUpdateUserAttributesAsync(new AdminUpdateUserAttributesRequest
            {
                UserPoolId = userPoolId,
                Username = normalizedEmail,
                UserAttributes = attributesToUpdate
            });

            return cognitoUser.UserStatus;
        }

        public Task<IActionResult> Admin(
            string? searchEmail,
            string? roleFilter,
            string? statusFilter,
            string? propertySearch,
            string? maintenanceSearch,
            string? maintenanceStatusFilter,
            string? maintenancePriorityFilter,
            string? activePane,
            string? auditSearch,
            string? auditActionFilter,
            DateTime? auditFromDate,
            DateTime? auditToDate)
        {
            return Dashboard(searchEmail, roleFilter, statusFilter, propertySearch, maintenanceSearch, maintenanceStatusFilter, maintenancePriorityFilter, activePane, auditSearch, auditActionFilter, auditFromDate, auditToDate);
        }

        public async Task<IActionResult> Dashboard(
            string? searchEmail,
            string? roleFilter,
            string? statusFilter,
            string? propertySearch,
            string? maintenanceSearch,
            string? maintenanceStatusFilter,
            string? maintenancePriorityFilter,
            string? activePane,
            string? auditSearch,
            string? auditActionFilter,
            DateTime? auditFromDate,
            DateTime? auditToDate)
        {
            var normalizedSearchEmail = searchEmail?.Trim() ?? string.Empty;
            var normalizedRoleFilter = NormalizeFilter(roleFilter, AllowedRoles);
            var normalizedStatusFilter = NormalizeFilter(statusFilter, AllowedStatuses);
            var normalizedPropertySearch = propertySearch?.Trim() ?? string.Empty;
            var normalizedMaintenanceSearch = maintenanceSearch?.Trim() ?? string.Empty;
            var normalizedMaintenanceStatusFilter = NormalizeFilter(maintenanceStatusFilter, AllowedMaintenanceStatuses);
            var normalizedMaintenancePriorityFilter = NormalizeFilter(maintenancePriorityFilter, AllowedMaintenancePriorities);
            var normalizedAuditSearch = auditSearch?.Trim() ?? string.Empty;
            var normalizedAuditActionFilter = NormalizeFilter(auditActionFilter, AllowedAuditActions);
            var hasAuditFilter = !string.IsNullOrWhiteSpace(normalizedAuditSearch)
                || !string.IsNullOrWhiteSpace(normalizedAuditActionFilter)
                || auditFromDate.HasValue
                || auditToDate.HasValue;
            var hasPropertyFilter = !string.IsNullOrWhiteSpace(normalizedPropertySearch);
            var hasMaintenanceFilter = !string.IsNullOrWhiteSpace(normalizedMaintenanceSearch)
                || !string.IsNullOrWhiteSpace(normalizedMaintenanceStatusFilter)
                || !string.IsNullOrWhiteSpace(normalizedMaintenancePriorityFilter);
            var normalizedActivePane = NormalizeFilter(activePane, AllowedAdminPanes)
                ?? (hasAuditFilter ? "audit" : hasMaintenanceFilter ? "maintenance" : hasPropertyFilter ? "properties" : "dashboard");

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
            var analyticsStartMonth = currentMonthStart.AddMonths(-5);
            var analyticsMonths = Enumerable.Range(0, 6)
                .Select(offset => analyticsStartMonth.AddMonths(offset))
                .ToList();
            var auditLast24HoursStart = utcNow.AddHours(-24);

            var monthlyPaymentRows = await _dbContext.Payments.AsNoTracking()
                .Where(p => p.Status == PaymentStatus.Verified
                    && p.PaymentDate.HasValue
                    && p.PaymentDate.Value >= analyticsStartMonth
                    && p.PaymentDate.Value < nextMonthStart)
                .GroupBy(p => new
                {
                    p.PaymentDate!.Value.Year,
                    p.PaymentDate!.Value.Month
                })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Amount = g.Sum(p => p.Amount)
                })
                .ToListAsync();

            var monthlyUserRows = await _dbContext.Users.AsNoTracking()
                .Where(u => u.CreatedAt >= analyticsStartMonth
                    && u.CreatedAt < nextMonthStart)
                .GroupBy(u => new
                {
                    u.CreatedAt.Year,
                    u.CreatedAt.Month
                })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Count = g.Count()
                })
                .ToListAsync();

            var monthlyMaintenanceRows = await _dbContext.MaintenanceRequests.AsNoTracking()
                .Where(m => m.CreatedAt >= analyticsStartMonth
                    && m.CreatedAt < nextMonthStart)
                .GroupBy(m => new
                {
                    m.CreatedAt.Year,
                    m.CreatedAt.Month
                })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Count = g.Count()
                })
                .ToListAsync();

            var monthlyPaymentLookup = monthlyPaymentRows.ToDictionary(
                row => (row.Year, row.Month),
                row => row.Amount);
            var monthlyUserLookup = monthlyUserRows.ToDictionary(
                row => (row.Year, row.Month),
                row => row.Count);
            var monthlyMaintenanceLookup = monthlyMaintenanceRows.ToDictionary(
                row => (row.Year, row.Month),
                row => row.Count);

            var monthlyAnalytics = analyticsMonths
                .Select(month => new AdminMonthlyAnalyticsViewModel
                {
                    MonthLabel = month.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                    CollectedAmount = monthlyPaymentLookup.TryGetValue((month.Year, month.Month), out var amount) ? amount : 0m,
                    NewUsers = monthlyUserLookup.TryGetValue((month.Year, month.Month), out var newUsers) ? newUsers : 0,
                    MaintenanceRequests = monthlyMaintenanceLookup.TryGetValue((month.Year, month.Month), out var maintenanceRequests) ? maintenanceRequests : 0
                })
                .ToList();

            var activePropertiesQuery = _dbContext.Properties.AsNoTracking().Where(p => !p.IsDeleted);
            var totalProperties = await activePropertiesQuery.CountAsync();
            var occupiedProperties = await _dbContext.Tenants.AsNoTracking()
                .Where(t => !t.Property.IsDeleted && t.LeaseStatus == LeaseStatus.Active)
                .Select(t => t.PropertyId)
                .Distinct()
                .CountAsync();
            var vacantProperties = Math.Max(totalProperties - occupiedProperties, 0);
            var monthlyRentRoll = await _dbContext.Tenants.AsNoTracking()
                .Where(t => t.LeaseStatus == LeaseStatus.Active)
                .SumAsync(t => (decimal?)t.MonthlyRent) ?? 0m;
            var averageListedRent = await activePropertiesQuery
                .AverageAsync(p => (decimal?)p.MonthlyRent) ?? 0m;

            var totalUsers = await _dbContext.Users.AsNoTracking().CountAsync();
            var approvedUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => u.IsApproved && !u.IsDisabled);
            var pendingUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => !u.IsApproved && !u.IsDisabled);
            var pendingLandlordApprovals = await _dbContext.Users.AsNoTracking()
                .CountAsync(u => u.Role == "Landlord" && !u.IsApproved && !u.IsDisabled);
            var disabledUsers = await _dbContext.Users.AsNoTracking().CountAsync(u => u.IsDisabled);
            var activeTenancies = await _dbContext.Tenants.AsNoTracking().CountAsync(t => t.LeaseStatus == LeaseStatus.Active);
            var totalPayments = await _dbContext.Payments.AsNoTracking().CountAsync();
            var totalMaintenanceRequests = await _dbContext.MaintenanceRequests.AsNoTracking().CountAsync();
            var openMaintenanceRequests = await _dbContext.MaintenanceRequests.AsNoTracking()
                .CountAsync(m => m.Status == MaintenanceStatus.Pending
                    || m.Status == MaintenanceStatus.Approved
                    || m.Status == MaintenanceStatus.InProgress);
            var totalDocuments = await _dbContext.Documents.AsNoTracking().CountAsync(d => !d.IsDeleted);
            var overduePayments = await _dbContext.Payments.AsNoTracking().CountAsync(p => p.Status == PaymentStatus.Overdue);

            var propertyDirectoryQuery = activePropertiesQuery;

            if (!string.IsNullOrWhiteSpace(normalizedPropertySearch))
            {
                propertyDirectoryQuery = propertyDirectoryQuery.Where(p =>
                    p.PropertyName.Contains(normalizedPropertySearch)
                    || p.AddressLine1.Contains(normalizedPropertySearch)
                    || (p.AddressLine2 != null && p.AddressLine2.Contains(normalizedPropertySearch))
                    || p.City.Contains(normalizedPropertySearch)
                    || p.State.Contains(normalizedPropertySearch)
                    || p.PostalCode.Contains(normalizedPropertySearch)
                    || (p.UnitNumber != null && p.UnitNumber.Contains(normalizedPropertySearch))
                    || (p.FloorNumber != null && p.FloorNumber.Contains(normalizedPropertySearch))
                    || (p.Landlord != null && p.Landlord.Email.Contains(normalizedPropertySearch))
                    || p.Tenants.Any(t => t.LeaseStatus == LeaseStatus.Active
                        && t.User.Email.Contains(normalizedPropertySearch)));
            }

            var propertyTotalMatches = await propertyDirectoryQuery.CountAsync();

            var maintenanceQueueQuery = _dbContext.MaintenanceRequests.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(normalizedMaintenanceSearch))
            {
                maintenanceQueueQuery = maintenanceQueueQuery.Where(m =>
                    m.Title.Contains(normalizedMaintenanceSearch)
                    || m.Description.Contains(normalizedMaintenanceSearch)
                    || m.Property.PropertyName.Contains(normalizedMaintenanceSearch)
                    || m.Property.City.Contains(normalizedMaintenanceSearch)
                    || m.Property.State.Contains(normalizedMaintenanceSearch)
                    || (m.Property.UnitNumber != null && m.Property.UnitNumber.Contains(normalizedMaintenanceSearch))
                    || m.Tenant.User.Email.Contains(normalizedMaintenanceSearch)
                    || (m.Property.Landlord != null && m.Property.Landlord.Email.Contains(normalizedMaintenanceSearch))
                    || (m.LandlordRemarks != null && m.LandlordRemarks.Contains(normalizedMaintenanceSearch)));
            }

            if (!string.IsNullOrWhiteSpace(normalizedMaintenanceStatusFilter)
                && Enum.TryParse<MaintenanceStatus>(normalizedMaintenanceStatusFilter, out var maintenanceStatus))
            {
                maintenanceQueueQuery = maintenanceQueueQuery.Where(m => m.Status == maintenanceStatus);
            }

            if (!string.IsNullOrWhiteSpace(normalizedMaintenancePriorityFilter)
                && Enum.TryParse<MaintenancePriority>(normalizedMaintenancePriorityFilter, out var maintenancePriority))
            {
                maintenanceQueueQuery = maintenanceQueueQuery.Where(m => m.Priority == maintenancePriority);
            }

            var activeMaintenanceQuery = maintenanceQueueQuery.Where(m =>
                m.Status == MaintenanceStatus.Pending
                || m.Status == MaintenanceStatus.Approved
                || m.Status == MaintenanceStatus.InProgress);
            var maintenanceHistoryQuery = maintenanceQueueQuery.Where(m =>
                m.Status == MaintenanceStatus.Completed
                || m.Status == MaintenanceStatus.Rejected);
            var maintenanceTotalMatches = await activeMaintenanceQuery.CountAsync();
            var maintenanceHistoryTotalMatches = await maintenanceHistoryQuery.CountAsync();

            var auditQuery = _dbContext.AuditLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(normalizedAuditSearch))
            {
                auditQuery = auditQuery.Where(a =>
                    a.Action.Contains(normalizedAuditSearch)
                    || a.ActorEmail.Contains(normalizedAuditSearch)
                    || a.TargetType.Contains(normalizedAuditSearch)
                    || (a.TargetEmail != null && a.TargetEmail.Contains(normalizedAuditSearch))
                    || (a.Details != null && a.Details.Contains(normalizedAuditSearch)));
            }

            if (!string.IsNullOrWhiteSpace(normalizedAuditActionFilter))
            {
                auditQuery = auditQuery.Where(a => a.Action == normalizedAuditActionFilter);
            }

            if (auditFromDate.HasValue)
            {
                var auditFromUtc = DateTime.SpecifyKind(auditFromDate.Value.Date, DateTimeKind.Utc);
                auditQuery = auditQuery.Where(a => a.CreatedAt >= auditFromUtc);
            }

            if (auditToDate.HasValue)
            {
                var auditToUtcExclusive = DateTime.SpecifyKind(auditToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
                auditQuery = auditQuery.Where(a => a.CreatedAt < auditToUtcExclusive);
            }

            var auditTotalMatches = await auditQuery.CountAsync();

            var model = new AdminDashboardViewModel
            {
                ActivePane = normalizedActivePane,
                SearchEmail = normalizedSearchEmail,
                RoleFilter = normalizedRoleFilter ?? string.Empty,
                StatusFilter = normalizedStatusFilter ?? string.Empty,
                PropertySearch = normalizedPropertySearch,
                PropertyTotalMatches = propertyTotalMatches,
                MaintenanceSearch = normalizedMaintenanceSearch,
                MaintenanceStatusFilter = normalizedMaintenanceStatusFilter ?? string.Empty,
                MaintenancePriorityFilter = normalizedMaintenancePriorityFilter ?? string.Empty,
                MaintenanceTotalMatches = maintenanceTotalMatches,
                MaintenanceHistoryTotalMatches = maintenanceHistoryTotalMatches,
                AuditSearch = normalizedAuditSearch,
                AuditActionFilter = normalizedAuditActionFilter ?? string.Empty,
                AuditFromDate = auditFromDate,
                AuditToDate = auditToDate,
                AuditTotalMatches = auditTotalMatches,
                AuditActions = AllowedAuditActions.ToList(),
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
                    TotalPayments = totalPayments,
                    PendingLandlordApprovals = pendingLandlordApprovals,
                    TotalMaintenanceRequests = totalMaintenanceRequests,
                    OpenMaintenanceRequests = openMaintenanceRequests,
                    TotalDocuments = totalDocuments,
                    OverduePayments = overduePayments
                },
                UserReport = new AdminUserReportViewModel
                {
                    AdminCount = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Role == "Admin"),
                    LandlordCount = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Role == "Landlord"),
                    SecurityCount = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Role == "Security"),
                    TenantCount = await _dbContext.Users.AsNoTracking().CountAsync(u => u.Role == "Tenant")
                },
                PropertyReport = new AdminPropertyReportViewModel
                {
                    TotalProperties = totalProperties,
                    OccupiedProperties = occupiedProperties,
                    VacantProperties = vacantProperties,
                    ActiveTenancies = activeTenancies,
                    MonthlyRentRoll = monthlyRentRoll,
                    AverageListedRent = averageListedRent,
                    OpenMaintenanceCount = openMaintenanceRequests,
                    OverduePaymentCount = overduePayments,
                    DocumentCount = totalDocuments,
                    PendingApprovalCount = await activePropertiesQuery.CountAsync(p => p.ApprovalStatus == PropertyApprovalStatus.Pending),
                    ApprovedCount = await activePropertiesQuery.CountAsync(p => p.ApprovalStatus == PropertyApprovalStatus.Approved),
                    RejectedCount = await activePropertiesQuery.CountAsync(p => p.ApprovalStatus == PropertyApprovalStatus.Rejected)
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
                                || m.Status == MaintenanceStatus.InProgress)),
                    MediumPriorityOpenCount = await _dbContext.MaintenanceRequests.AsNoTracking()
                        .CountAsync(m => m.Priority == MaintenancePriority.Medium
                            && (m.Status == MaintenanceStatus.Pending
                                || m.Status == MaintenanceStatus.Approved
                                || m.Status == MaintenanceStatus.InProgress)),
                    LowPriorityOpenCount = await _dbContext.MaintenanceRequests.AsNoTracking()
                        .CountAsync(m => m.Priority == MaintenancePriority.Low
                            && (m.Status == MaintenanceStatus.Pending
                                || m.Status == MaintenanceStatus.Approved
                                || m.Status == MaintenanceStatus.InProgress)),
                    AwaitingTenantConfirmationCount = await _dbContext.MaintenanceRequests.AsNoTracking()
                        .CountAsync(m => m.Status == MaintenanceStatus.Completed && !m.TenantConfirmedAt.HasValue)
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
                MonthlyAnalytics = monthlyAnalytics,
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
                MaintenanceQueueItems = await activeMaintenanceQuery
                    .OrderBy(m => m.Priority == MaintenancePriority.High ? 0 : m.Priority == MaintenancePriority.Medium ? 1 : 2)
                    .ThenByDescending(m => m.CreatedAt)
                    .Select(m => new AdminMaintenanceQueueItemViewModel
                    {
                        RequestId = m.RequestId,
                        Title = m.Title,
                        Category = m.Category,
                        Priority = m.Priority,
                        Status = m.Status,
                        Description = m.Description,
                        PropertyName = m.Property.PropertyName,
                        TenantEmail = m.Tenant.User.Email,
                        LandlordEmail = m.Property.Landlord != null ? m.Property.Landlord.Email : "Unassigned landlord",
                        UnitNumber = m.Property.UnitNumber ?? string.Empty,
                        Location = m.Property.City + ", " + m.Property.State,
                        PreferredDate = m.PreferredDate,
                        ResolvedDate = m.ResolvedDate,
                        LandlordRemarks = m.LandlordRemarks ?? string.Empty,
                        HasIssueImage = !string.IsNullOrWhiteSpace(m.IssueImageKey),
                        TenantConfirmedAt = m.TenantConfirmedAt,
                        TenantFeedbackRating = m.TenantFeedbackRating,
                        TenantFeedbackComment = m.TenantFeedbackComment ?? string.Empty,
                        CreatedAt = m.CreatedAt,
                        UpdatedAt = m.UpdatedAt
                    })
                    .ToListAsync(),
                MaintenanceHistoryItems = await maintenanceHistoryQuery
                    .OrderByDescending(m => m.ResolvedDate ?? m.UpdatedAt)
                    .ThenByDescending(m => m.CreatedAt)
                    .Select(m => new AdminMaintenanceQueueItemViewModel
                    {
                        RequestId = m.RequestId,
                        Title = m.Title,
                        Category = m.Category,
                        Priority = m.Priority,
                        Status = m.Status,
                        Description = m.Description,
                        PropertyName = m.Property.PropertyName,
                        TenantEmail = m.Tenant.User.Email,
                        LandlordEmail = m.Property.Landlord != null ? m.Property.Landlord.Email : "Unassigned landlord",
                        UnitNumber = m.Property.UnitNumber ?? string.Empty,
                        Location = m.Property.City + ", " + m.Property.State,
                        PreferredDate = m.PreferredDate,
                        ResolvedDate = m.ResolvedDate,
                        LandlordRemarks = m.LandlordRemarks ?? string.Empty,
                        HasIssueImage = !string.IsNullOrWhiteSpace(m.IssueImageKey),
                        TenantConfirmedAt = m.TenantConfirmedAt,
                        TenantFeedbackRating = m.TenantFeedbackRating,
                        TenantFeedbackComment = m.TenantFeedbackComment ?? string.Empty,
                        CreatedAt = m.CreatedAt,
                        UpdatedAt = m.UpdatedAt
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
                    .ToListAsync(),
                PaymentRecords = await _dbContext.Payments.AsNoTracking()
                    .OrderByDescending(p => p.DueDate)
                    .ThenByDescending(p => p.CreatedAt)
                    .Take(50)
                    .Select(p => new PaymentListItemViewModel
                    {
                        PaymentId = p.PaymentId,
                        TenantEmail = p.Tenant.User.Email,
                        PropertyName = p.Property.PropertyName,
                        UnitNumber = p.Property.UnitNumber,
                        LandlordEmail = p.Property.Landlord != null ? p.Property.Landlord.Email : null,
                        Amount = p.Amount,
                        Status = p.Status,
                        IsComputedOverdue = p.DueDate < utcNow
                            && p.Status != PaymentStatus.Verified
                            && p.Status != PaymentStatus.Rejected,
                        DueDate = p.DueDate,
                        SubmittedDate = p.PaymentDate,
                        VerifiedDate = p.Status == PaymentStatus.Verified ? p.UpdatedAt : (DateTime?)null,
                        PaymentMethod = p.PaymentMethod,
                        ReferenceNo = p.ReferenceNo,
                        StripeSessionId = p.StripeSessionId,
                        StripePaymentIntentId = p.StripePaymentIntentId,
                        PaymentPeriod = p.PaymentMonth + " " + p.PaymentYear
                    })
                    .ToListAsync(),
                PropertySnapshots = await propertyDirectoryQuery
                    .OrderBy(p => p.PropertyName)
                    .ThenBy(p => p.UnitNumber)
                    .Select(p => new AdminPropertySnapshotViewModel
                    {
                        PropertyId = p.PropertyId,
                        PropertyName = p.PropertyName,
                        PropertyType = p.PropertyType.ToString(),
                        AddressLine1 = p.AddressLine1,
                        AddressLine2 = p.AddressLine2 ?? string.Empty,
                        PostalCode = p.PostalCode,
                        UnitNumber = p.UnitNumber ?? string.Empty,
                        FloorNumber = p.FloorNumber ?? string.Empty,
                        Location = p.City + ", " + p.State,
                        LandlordEmail = p.Landlord != null ? p.Landlord.Email : "Unassigned landlord",
                        TenantEmail = p.Tenants
                            .Where(t => t.LeaseStatus == LeaseStatus.Active)
                            .Select(t => t.User.Email)
                            .FirstOrDefault() ?? string.Empty,
                        IsOccupied = p.Tenants.Any(t => t.LeaseStatus == LeaseStatus.Active),
                        LeaseStatus = p.Tenants
                            .Where(t => t.LeaseStatus == LeaseStatus.Active)
                            .Select(t => t.LeaseStatus.ToString())
                            .FirstOrDefault() ?? "Vacant",
                        LeaseStartDate = p.Tenants
                            .Where(t => t.LeaseStatus == LeaseStatus.Active)
                            .Select(t => (DateTime?)t.LeaseStartDate)
                            .FirstOrDefault(),
                        LeaseEndDate = p.Tenants
                            .Where(t => t.LeaseStatus == LeaseStatus.Active)
                            .Select(t => (DateTime?)t.LeaseEndDate)
                            .FirstOrDefault(),
                        MonthlyRent = p.Tenants
                            .Where(t => t.LeaseStatus == LeaseStatus.Active)
                            .Select(t => (decimal?)t.MonthlyRent)
                            .FirstOrDefault() ?? p.MonthlyRent,
                        DepositAmount = p.DepositAmount,
                        SizeSqFt = p.SizeSqFt,
                        Bedrooms = p.Bedrooms,
                        Bathrooms = p.Bathrooms,
                        ParkingBay = p.ParkingBay ?? string.Empty,
                        Description = p.Description ?? string.Empty,
                        OpenMaintenanceCount = p.MaintenanceRequests.Count(m =>
                            m.Status == MaintenanceStatus.Pending
                            || m.Status == MaintenanceStatus.Approved
                            || m.Status == MaintenanceStatus.InProgress),
                        DocumentCount = p.Documents.Count(d => !d.IsDeleted),
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt
                    })
                    .ToListAsync(),
                PasswordResetRequests = await _dbContext.PasswordResetRequests.AsNoTracking()
                    .Where(r => r.Status == PasswordResetRequestStatus.Pending)
                    .OrderByDescending(r => r.RequestedAt)
                    .Take(10)
                    .Select(r => new AdminPasswordResetRequestViewModel
                    {
                        PasswordResetRequestId = r.PasswordResetRequestId,
                        Email = r.Email,
                        Status = r.Status,
                        RequestedAt = r.RequestedAt
                    })
                    .ToListAsync(),
                AuditSummary = new AdminAuditSummaryViewModel
                {
                    TotalEvents = await _dbContext.AuditLogs.AsNoTracking().CountAsync(),
                    EventsLast24Hours = await _dbContext.AuditLogs.AsNoTracking()
                        .CountAsync(a => a.CreatedAt >= auditLast24HoursStart),
                    UserManagementEvents = await _dbContext.AuditLogs.AsNoTracking()
                        .CountAsync(a => a.TargetType == "User"),
                    PasswordResetEvents = await _dbContext.AuditLogs.AsNoTracking()
                        .CountAsync(a => a.TargetType == "PasswordResetRequest")
                },
                AuditLogs = await auditQuery
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(200)
                    .Select(a => new AdminAuditLogViewModel
                    {
                        AuditLogId = a.AuditLogId,
                        Action = a.Action,
                        ActorEmail = a.ActorEmail,
                        TargetType = a.TargetType,
                        TargetId = a.TargetId,
                        TargetEmail = a.TargetEmail,
                        Details = a.Details,
                        CreatedAt = a.CreatedAt
                    })
                    .ToListAsync(),
                Announcements = await _dbContext.SystemAnnouncements.AsNoTracking()
                    .OrderByDescending(a => a.CreatedAt)
                    .ToListAsync(),
                PropertyApprovals = await _dbContext.Properties.AsNoTracking()
                    .Include(p => p.Landlord)
                    .Where(p => !p.IsDeleted && p.ApprovalStatus != PropertyApprovalStatus.Approved)
                    .OrderBy(p => p.ApprovalStatus)
                    .ThenByDescending(p => p.UpdatedAt)
                    .Select(p => new AdminPropertyApprovalViewModel
                    {
                        PropertyId = p.PropertyId,
                        PropertyName = p.PropertyName,
                        LandlordEmail = p.Landlord != null ? p.Landlord.Email : "-",
                        Address = p.AddressLine1 + " " + (p.AddressLine2 ?? "") + ", " + p.City + ", " + p.State,
                        MonthlyRent = p.MonthlyRent,
                        AvailabilityStatus = p.AvailabilityStatus,
                        ApprovalStatus = p.ApprovalStatus,
                        UpdatedAt = p.UpdatedAt
                    })
                    .ToListAsync()
            };

            return View("Admin", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveProperty(int id)
        {
            var property = await _dbContext.Properties
                .Include(p => p.Landlord)
                .FirstOrDefaultAsync(p => p.PropertyId == id && !p.IsDeleted);

            if (property == null)
            {
                TempData["ErrorMessage"] = "Property not found.";
                return RedirectToAction(nameof(Dashboard), new { activePane = "properties" });
            }

            property.ApprovalStatus = PropertyApprovalStatus.Approved;
            property.UpdatedAt = DateTime.UtcNow;
            AddAuditLog(
                "ApproveProperty",
                "Property",
                property.PropertyId,
                property.Landlord?.Email,
                $"Approved property '{property.PropertyName}'.");
            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Property approved.";
            return RedirectToAction(nameof(Dashboard), new { activePane = "properties" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectProperty(int id)
        {
            var property = await _dbContext.Properties
                .Include(p => p.Landlord)
                .FirstOrDefaultAsync(p => p.PropertyId == id && !p.IsDeleted);

            if (property == null)
            {
                TempData["ErrorMessage"] = "Property not found.";
                return RedirectToAction(nameof(Dashboard), new { activePane = "properties" });
            }

            property.ApprovalStatus = PropertyApprovalStatus.Rejected;
            property.UpdatedAt = DateTime.UtcNow;
            AddAuditLog(
                "RejectProperty",
                "Property",
                property.PropertyId,
                property.Landlord?.Email,
                $"Rejected property '{property.PropertyName}'.");
            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Property rejected.";
            return RedirectToAction(nameof(Dashboard), new { activePane = "properties" });
        }

        [HttpPost]
        public async Task<IActionResult> ApproveUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user != null && !user.IsApproved)
            {
                try
                {
                    var userPoolId = GetConfiguredUserPoolId();
                    var cognitoStatus = await EnsureCognitoEmailCanReceiveAccountRecoveryAsync(userPoolId, user.Email);

                    if (cognitoStatus == UserStatusType.UNCONFIRMED)
                    {
                        var confirmRequest = new AdminConfirmSignUpRequest
                        {
                            UserPoolId = userPoolId,
                            Username = user.Email
                        };
                        await _cognitoClient.AdminConfirmSignUpAsync(confirmRequest);
                    }
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Failed to confirm in Cognito: {ex.Message}";
                    return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
                }

                user.IsApproved = true;
                AddAuditLog(
                    "ApproveUser",
                    "User",
                    user.Id,
                    user.Email,
                    "Approved user registration.");
                await _dbContext.SaveChangesAsync();
                
                try 
                {
                    // Updated: Removed the nickname parameter
                    await _emailService.SendApprovalEmailAsync(user.Email);
                    TempData["SuccessMessage"] = "User approved and email sent!";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send approval email to {Email}.", user.Email);
                    TempData["SuccessMessage"] = $"User approved, but the notification email failed to send: {ex.Message}";
                }
            }
            return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApprovePasswordResetRequest(int id)
        {
            var request = await _dbContext.PasswordResetRequests.FindAsync(id);

            if (request == null || request.Status != PasswordResetRequestStatus.Pending)
            {
                TempData["ErrorMessage"] = "Password reset request is no longer available.";
                return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
            }

            try
            {
                var userPoolId = GetConfiguredUserPoolId();
                await EnsureCognitoEmailCanReceiveAccountRecoveryAsync(userPoolId, request.Email);

                await _cognitoClient.AdminResetUserPasswordAsync(new AdminResetUserPasswordRequest
                {
                    UserPoolId = userPoolId,
                    Username = request.Email
                });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to send reset email through Cognito: {ex.Message}";
                return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
            }

            request.Status = PasswordResetRequestStatus.Approved;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewedByEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
            AddAuditLog(
                "ApprovePasswordReset",
                "PasswordResetRequest",
                request.PasswordResetRequestId,
                request.Email,
                "Approved password reset request and triggered Cognito reset email.");
            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Password reset approved. Cognito has sent the reset email.";
            return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectPasswordResetRequest(int id)
        {
            var request = await _dbContext.PasswordResetRequests.FindAsync(id);

            if (request == null || request.Status != PasswordResetRequestStatus.Pending)
            {
                TempData["ErrorMessage"] = "Password reset request is no longer available.";
                return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
            }

            request.Status = PasswordResetRequestStatus.Rejected;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewedByEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
            AddAuditLog(
                "RejectPasswordReset",
                "PasswordResetRequest",
                request.PasswordResetRequestId,
                request.Email,
                "Rejected password reset request.");
            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Password reset request rejected.";
            return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
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
                    return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
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
                    return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
                }

                // Mark as disabled in Neon DB
                user.IsDisabled = true;
                AddAuditLog(
                    "DisableUser",
                    "User",
                    user.Id,
                    user.Email,
                    "Disabled user account in Cognito and local database.");
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = "User has been disabled successfully.";
            }
            return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
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
                    return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
                }

                // 2. Mark as enabled in Neon DB
                user.IsDisabled = false;
                AddAuditLog(
                    "EnableUser",
                    "User",
                    user.Id,
                    user.Email,
                    "Enabled user account in Cognito and local database.");
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = "User has been enabled successfully.";
            }
            return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
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
                    return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
                }

                if (IsCurrentUser(user.Email) && !string.Equals(normalizedRole, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["ErrorMessage"] = "You cannot remove your own admin role.";
                    return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
                }

                var previousRole = user.Role;
                user.Role = normalizedRole;
                AddAuditLog(
                    "ChangeRole",
                    "User",
                    user.Id,
                    user.Email,
                    $"Changed role from {previousRole} to {normalizedRole}.");
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Role updated to {normalizedRole}.";
            }

            return RedirectToAction(nameof(Dashboard), new { activePane = "users" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAnnouncement(CreateAnnouncementViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please fill in all required fields.";
                return RedirectToAction(nameof(Dashboard), new { activePane = "announcements" });
            }

            var normalizedVisibleTo = NormalizeFilter(vm.VisibleTo, AllowedVisibleTo) ?? "All";

            var announcement = new SystemAnnouncement
            {
                Title = vm.Title.Trim(),
                Body = vm.Body.Trim(),
                VisibleTo = normalizedVisibleTo,
                CreatedAt = DateTime.UtcNow,
                CreatedByEmail = GetCurrentUserEmail()
            };

            _dbContext.SystemAnnouncements.Add(announcement);
            await _dbContext.SaveChangesAsync();

            AddAuditLog(
                "CreateAnnouncement",
                "SystemAnnouncement",
                announcement.SystemAnnouncementId,
                null,
                $"Created announcement '{announcement.Title}' visible to {announcement.VisibleTo}.");
            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Announcement created successfully.";
            return RedirectToAction(nameof(Dashboard), new { activePane = "announcements" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAnnouncement(EditAnnouncementViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please fill in all required fields.";
                return RedirectToAction(nameof(Dashboard), new { activePane = "announcements" });
            }

            var announcement = await _dbContext.SystemAnnouncements.FindAsync(vm.SystemAnnouncementId);
            if (announcement == null)
            {
                TempData["ErrorMessage"] = "Announcement not found.";
                return RedirectToAction(nameof(Dashboard), new { activePane = "announcements" });
            }

            var normalizedVisibleTo = NormalizeFilter(vm.VisibleTo, AllowedVisibleTo) ?? "All";

            announcement.Title = vm.Title.Trim();
            announcement.Body = vm.Body.Trim();
            announcement.VisibleTo = normalizedVisibleTo;
            announcement.UpdatedAt = DateTime.UtcNow;

            AddAuditLog(
                "EditAnnouncement",
                "SystemAnnouncement",
                announcement.SystemAnnouncementId,
                null,
                $"Edited announcement '{announcement.Title}' — visibility: {announcement.VisibleTo}.");
            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Announcement updated successfully.";
            return RedirectToAction(nameof(Dashboard), new { activePane = "announcements" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAnnouncement(int id)
        {
            var announcement = await _dbContext.SystemAnnouncements.FindAsync(id);
            if (announcement == null)
            {
                TempData["ErrorMessage"] = "Announcement not found.";
                return RedirectToAction(nameof(Dashboard), new { activePane = "announcements" });
            }

            var title = announcement.Title;
            _dbContext.SystemAnnouncements.Remove(announcement);

            AddAuditLog(
                "DeleteAnnouncement",
                "SystemAnnouncement",
                id,
                null,
                $"Deleted announcement '{title}'.");
            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = "Announcement deleted.";
            return RedirectToAction(nameof(Dashboard), new { activePane = "announcements" });
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

        private void AddAuditLog(string action, string targetType, int? targetId, string? targetEmail, string? details)
        {
            _dbContext.AuditLogs.Add(new AuditLog
            {
                Action = action,
                ActorEmail = GetCurrentUserEmail(),
                TargetType = targetType,
                TargetId = targetId,
                TargetEmail = targetEmail,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });
        }

        private string GetCurrentUserEmail()
        {
            return User.FindFirstValue(ClaimTypes.Email)
                ?? User.Identity?.Name
                ?? "Unknown admin";
        }
    }
}
