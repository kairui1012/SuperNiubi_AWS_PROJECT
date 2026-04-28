using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models.Admin
{
    public class CreateAnnouncementViewModel
    {
        [Required(ErrorMessage = "Title is required.")]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Body is required.")]
        [MaxLength(2000)]
        public string Body { get; set; } = string.Empty;

        [Required]
        public string VisibleTo { get; set; } = "All";
    }

    public class EditAnnouncementViewModel
    {
        public int SystemAnnouncementId { get; set; }

        [Required(ErrorMessage = "Title is required.")]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Body is required.")]
        [MaxLength(2000)]
        public string Body { get; set; } = string.Empty;

        [Required]
        public string VisibleTo { get; set; } = "All";
    }

    public class AdminDashboardViewModel
    {
        public string ActivePane { get; set; } = "dashboard";
        public string SearchEmail { get; set; } = string.Empty;
        public string RoleFilter { get; set; } = string.Empty;
        public string StatusFilter { get; set; } = string.Empty;
        public string PropertySearch { get; set; } = string.Empty;
        public int PropertyTotalMatches { get; set; }
        public string MaintenanceSearch { get; set; } = string.Empty;
        public string MaintenanceStatusFilter { get; set; } = string.Empty;
        public string MaintenancePriorityFilter { get; set; } = string.Empty;
        public int MaintenanceTotalMatches { get; set; }
        public int MaintenanceHistoryTotalMatches { get; set; }
        public string AuditSearch { get; set; } = string.Empty;
        public string AuditActionFilter { get; set; } = string.Empty;
        public DateTime? AuditFromDate { get; set; }
        public DateTime? AuditToDate { get; set; }
        public int AuditTotalMatches { get; set; }
        public List<string> AuditActions { get; set; } = new();

        public List<AppUser> Users { get; set; } = new();
        public AdminOverviewViewModel Overview { get; set; } = new();
        public AdminUserReportViewModel UserReport { get; set; } = new();
        public AdminPropertyReportViewModel PropertyReport { get; set; } = new();
        public AdminMaintenanceReportViewModel MaintenanceReport { get; set; } = new();
        public AdminPaymentReportViewModel PaymentReport { get; set; } = new();
        public List<AdminMonthlyAnalyticsViewModel> MonthlyAnalytics { get; set; } = new();

        public List<AdminLatestUserViewModel> LatestUsers { get; set; } = new();
        public List<AdminRecentMaintenanceViewModel> RecentMaintenanceRequests { get; set; } = new();
        public List<AdminMaintenanceQueueItemViewModel> MaintenanceQueueItems { get; set; } = new();
        public List<AdminMaintenanceQueueItemViewModel> MaintenanceHistoryItems { get; set; } = new();
        public List<AdminRecentPaymentViewModel> RecentPayments { get; set; } = new();
        public List<AdminPropertySnapshotViewModel> PropertySnapshots { get; set; } = new();
        public List<AdminPasswordResetRequestViewModel> PasswordResetRequests { get; set; } = new();
        public List<AdminAuditLogViewModel> AuditLogs { get; set; } = new();
        public AdminAuditSummaryViewModel AuditSummary { get; set; } = new();
        public List<SystemAnnouncement> Announcements { get; set; } = new();
        public AdminXRayReportViewModel XRayReport { get; set; } = new();
    }

    public class AdminOverviewViewModel
    {
        public int TotalUsers { get; set; }
        public int ApprovedUsers { get; set; }
        public int PendingUsers { get; set; }
        public int DisabledUsers { get; set; }
        public int TotalProperties { get; set; }
        public int OccupiedProperties { get; set; }
        public int VacantProperties { get; set; }
        public int ActiveTenancies { get; set; }
        public int TotalPayments { get; set; }
        public int PendingLandlordApprovals { get; set; }
        public int TotalMaintenanceRequests { get; set; }
        public int OpenMaintenanceRequests { get; set; }
        public int TotalDocuments { get; set; }
        public int OverduePayments { get; set; }
    }

    public class AdminUserReportViewModel
    {
        public int AdminCount { get; set; }
        public int LandlordCount { get; set; }
        public int TenantCount { get; set; }
    }

    public class AdminPropertyReportViewModel
    {
        public int TotalProperties { get; set; }
        public int OccupiedProperties { get; set; }
        public int VacantProperties { get; set; }
        public int ActiveTenancies { get; set; }
        public decimal MonthlyRentRoll { get; set; }
        public decimal AverageListedRent { get; set; }
        public int OpenMaintenanceCount { get; set; }
        public int OverduePaymentCount { get; set; }
        public int DocumentCount { get; set; }
    }

    public class AdminMaintenanceReportViewModel
    {
        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int InProgressCount { get; set; }
        public int CompletedCount { get; set; }
        public int RejectedCount { get; set; }
        public int HighPriorityOpenCount { get; set; }
        public int MediumPriorityOpenCount { get; set; }
        public int LowPriorityOpenCount { get; set; }
        public int AwaitingTenantConfirmationCount { get; set; }
    }

    public class AdminPaymentReportViewModel
    {
        public int PendingCount { get; set; }
        public int SubmittedCount { get; set; }
        public int VerifiedCount { get; set; }
        public int OverdueCount { get; set; }
        public int RejectedCount { get; set; }
        public decimal TotalVerifiedAmount { get; set; }
        public decimal CurrentMonthVerifiedAmount { get; set; }
    }

    public class AdminMonthlyAnalyticsViewModel
    {
        public string MonthLabel { get; set; } = string.Empty;
        public decimal CollectedAmount { get; set; }
        public int NewUsers { get; set; }
        public int MaintenanceRequests { get; set; }
    }

    public class AdminLatestUserViewModel
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public bool IsDisabled { get; set; }
    }

    public class AdminRecentMaintenanceViewModel
    {
        public int RequestId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string TenantEmail { get; set; } = string.Empty;
        public MaintenanceStatus Status { get; set; }
        public MaintenancePriority Priority { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AdminMaintenanceQueueItemViewModel
    {
        public int RequestId { get; set; }
        public string Title { get; set; } = string.Empty;
        public MaintenanceCategory Category { get; set; }
        public MaintenancePriority Priority { get; set; }
        public MaintenanceStatus Status { get; set; }
        public string Description { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string TenantEmail { get; set; } = string.Empty;
        public string LandlordEmail { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime? PreferredDate { get; set; }
        public DateTime? ResolvedDate { get; set; }
        public string LandlordRemarks { get; set; } = string.Empty;
        public bool HasIssueImage { get; set; }
        public DateTime? TenantConfirmedAt { get; set; }
        public int? TenantFeedbackRating { get; set; }
        public string TenantFeedbackComment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AdminRecentPaymentViewModel
    {
        public int PaymentId { get; set; }
        public string TenantEmail { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string PaymentMonth { get; set; } = string.Empty;
        public int PaymentYear { get; set; }
        public decimal Amount { get; set; }
        public PaymentStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AdminPropertySnapshotViewModel
    {
        public int PropertyId { get; set; }
        public string PropertyName { get; set; } = string.Empty;
        public string PropertyType { get; set; } = string.Empty;
        public string AddressLine1 { get; set; } = string.Empty;
        public string AddressLine2 { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public string FloorNumber { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string LandlordEmail { get; set; } = string.Empty;
        public string TenantEmail { get; set; } = string.Empty;
        public bool IsOccupied { get; set; }
        public string LeaseStatus { get; set; } = string.Empty;
        public DateTime? LeaseStartDate { get; set; }
        public DateTime? LeaseEndDate { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal? DepositAmount { get; set; }
        public decimal? SizeSqFt { get; set; }
        public int Bedrooms { get; set; }
        public int Bathrooms { get; set; }
        public string ParkingBay { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int OpenMaintenanceCount { get; set; }
        public int DocumentCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AdminPasswordResetRequestViewModel
    {
        public int PasswordResetRequestId { get; set; }
        public string Email { get; set; } = string.Empty;
        public PasswordResetRequestStatus Status { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    public class AdminAuditLogViewModel
    {
        public int AuditLogId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string ActorEmail { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public int? TargetId { get; set; }
        public string? TargetEmail { get; set; }
        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AdminAuditSummaryViewModel
    {
        public int TotalEvents { get; set; }
        public int EventsLast24Hours { get; set; }
        public int UserManagementEvents { get; set; }
        public int PasswordResetEvents { get; set; }
    }

    public class AdminXRayReportViewModel
    {
        public string ServiceName { get; set; } = "PropEase";
        public string Region { get; set; } = string.Empty;
        public string DaemonAddress { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public string SamplingRuleManifest { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int TotalTraces { get; set; }
        public int ErrorCount { get; set; }
        public int FaultCount { get; set; }
        public int ThrottleCount { get; set; }
        public double SlowestDuration { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime WindowEnd { get; set; }
        public DateTime LastCheckedAt { get; set; }
        public List<AdminXRayTraceItemViewModel> RecentTraces { get; set; } = new();
    }

    public class AdminXRayTraceItemViewModel
    {
        public string TraceId { get; set; } = string.Empty;
        public double Duration { get; set; }
        public bool HasError { get; set; }
        public bool HasFault { get; set; }
        public bool HasThrottle { get; set; }
        public DateTime? StartTime { get; set; }
    }
}
