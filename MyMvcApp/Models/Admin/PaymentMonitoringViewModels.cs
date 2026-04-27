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
        public decimal TotalVerifiedAmount { get; set; }
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
        public string? LandlordRemarks { get; set; }
        public string PaymentPeriod { get; set; } = string.Empty;
        public string? ReturnUrl { get; set; }
    }
}
