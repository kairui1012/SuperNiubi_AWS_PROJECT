using MyMvcApp.Models;

namespace MyMvcApp.Models.Admin
{
    public class PaymentFilterViewModel
    {
        public string? Search { get; set; }
        public string? Status { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string SortBy { get; set; } = "due";
        public string SortDir { get; set; } = "desc";
        public int Page { get; set; } = 1;
        public const int PageSize = 20;
    }

    public class PaymentListItemViewModel
    {
        public int PaymentId { get; set; }
        public string TenantEmail { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string? UnitNumber { get; set; }
        public string? LandlordEmail { get; set; }
        public decimal Amount { get; set; }
        public PaymentStatus Status { get; set; }
        public bool IsComputedOverdue { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? SubmittedDate { get; set; }
        public DateTime? VerifiedDate { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public string? ReferenceNo { get; set; }
        public string? StripeSessionId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public string? StripeReceiptUrl { get; set; }
        public string? StripeRefundId { get; set; }
        public string PaymentPeriod { get; set; } = string.Empty;
    }

    public class AdminPaymentListViewModel
    {
        public PaymentFilterViewModel Filter { get; set; } = new();
        public List<PaymentListItemViewModel> Payments { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PendingCount { get; set; }
        public int SubmittedCount { get; set; }
        public int VerifiedCount { get; set; }
        public int ComputedOverdueCount { get; set; }
        public int RejectedCount { get; set; }
        public int FailedCount { get; set; }
        public int CancelledCount { get; set; }
        public int RefundedCount { get; set; }
        public decimal TotalVerifiedAmount { get; set; }
        public decimal CurrentMonthRevenue { get; set; }
        public decimal PreviousMonthRevenue { get; set; }
        public List<MonthlyRevenueReportItem> MonthlyRevenueReport { get; set; } = new();
        public List<OverdueTenantReportItem> OverdueTenantReport { get; set; } = new();
        public List<TenantPaymentReliabilityItem> TenantReliabilityReport { get; set; } = new();
    }

    public class PaymentDetailViewModel
    {
        public int PaymentId { get; set; }
        public string TenantEmail { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string? UnitNumber { get; set; }
        public string? LandlordEmail { get; set; }
        public decimal Amount { get; set; }
        public PaymentStatus Status { get; set; }
        public bool IsComputedOverdue { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? SubmittedDate { get; set; }
        public DateTime? VerifiedDate { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public string? ReferenceNo { get; set; }
        public string? ReceiptFileUrl { get; set; }
        public string? StripeSessionId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public string? StripeReceiptUrl { get; set; }
        public string? StripeEventId { get; set; }
        public string? StripeRefundId { get; set; }
        public decimal? RefundAmount { get; set; }
        public DateTime? RefundDate { get; set; }
        public string? RefundReason { get; set; }
        public string? LandlordRemarks { get; set; }
        public string PaymentPeriod { get; set; } = string.Empty;
        public string? ReturnUrl { get; set; }
    }

    public class MonthlyRevenueReportItem
    {
        public string MonthLabel { get; set; } = string.Empty;
        public decimal VerifiedAmount { get; set; }
        public int VerifiedCount { get; set; }
        public decimal RefundedAmount { get; set; }
        public decimal NetRevenue => VerifiedAmount - RefundedAmount;
    }

    public class OverdueTenantReportItem
    {
        public int TenantId { get; set; }
        public string TenantEmail { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string? UnitNumber { get; set; }
        public int OverdueCount { get; set; }
        public decimal OverdueAmount { get; set; }
        public DateTime OldestDueDate { get; set; }
        public int DaysOverdue { get; set; }
    }

    public class TenantPaymentReliabilityItem
    {
        public int TenantId { get; set; }
        public string TenantEmail { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public int TotalPayments { get; set; }
        public int VerifiedPayments { get; set; }
        public int LateOrProblemPayments { get; set; }
        public double ReliabilityScore { get; set; }
        public double OnTimeRate { get; set; }
    }
}
