using System.Collections.Generic;

namespace MyMvcApp.Models.Admin
{
    public class AdminDashboardViewModel
    {
        public string SearchEmail { get; set; } = string.Empty;
        public string RoleFilter { get; set; } = string.Empty;
        public string StatusFilter { get; set; } = string.Empty;

        public List<AppUser> Users { get; set; } = new();
        public AdminOverviewViewModel Overview { get; set; } = new();
        public AdminUserReportViewModel UserReport { get; set; } = new();
        public AdminPropertyReportViewModel PropertyReport { get; set; } = new();
        public AdminMaintenanceReportViewModel MaintenanceReport { get; set; } = new();
        public AdminPaymentReportViewModel PaymentReport { get; set; } = new();

        public List<AdminLatestUserViewModel> LatestUsers { get; set; } = new();
        public List<AdminRecentMaintenanceViewModel> RecentMaintenanceRequests { get; set; } = new();
        public List<AdminRecentPaymentViewModel> RecentPayments { get; set; } = new();
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
    }

    public class AdminMaintenanceReportViewModel
    {
        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int InProgressCount { get; set; }
        public int CompletedCount { get; set; }
        public int RejectedCount { get; set; }
        public int HighPriorityOpenCount { get; set; }
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
}
