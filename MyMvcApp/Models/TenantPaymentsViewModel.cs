namespace MyMvcApp.Models
{
    public class TenantPaymentsViewModel
    {
        public List<Payment> Payments { get; set; } = new();
        public CreatePaymentViewModel NewPayment { get; set; } = new();
        public decimal TotalVerifiedAmount { get; set; }
        public int SubmittedCount { get; set; }
        public int PendingCount { get; set; }
        public decimal MonthlyRent { get; set; }
        public DateTime? NextDueDate { get; set; }
    }
}
