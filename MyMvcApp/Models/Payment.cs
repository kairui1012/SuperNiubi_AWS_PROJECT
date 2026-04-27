using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MyMvcApp.Models;

namespace MyMvcApp.Models
{
    public enum PaymentMethod { OnlineTransfer, Cash, Cheque, DuitNow, Others }
    public enum PaymentStatus { Pending, Submitted, Verified, Overdue, Rejected }

    public class Payment
    {
        [Key]
        public int PaymentId { get; set; }

        [ForeignKey("Tenant")]
        public int TenantId { get; set; }

        [ForeignKey("Property")]
        public int PropertyId { get; set; }

        [Required, MaxLength(20)]
        public string PaymentMonth { get; set; } = string.Empty;

        [Required]
        public int PaymentYear { get; set; }

        [Required]
        public decimal Amount { get; set; }

        public DateTime? PaymentDate { get; set; }

        [Required]
        public DateTime DueDate { get; set; }

        public PaymentMethod? PaymentMethod { get; set; }

        [MaxLength(100)]
        public string? ReferenceNo { get; set; }

        [MaxLength(500)]
        public string? ReceiptFileKey { get; set; }

        [MaxLength(200)]
        public string? StripeSessionId { get; set; }

        [MaxLength(200)]
        public string? StripePaymentIntentId { get; set; }

        [MaxLength(500)]
        public string? StripeReceiptUrl { get; set; }

        [MaxLength(200)]
        public string? StripeEventId { get; set; }

        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
        public string? LandlordRemarks { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Tenant Tenant { get; set; } = null!;
        public Property Property { get; set; } = null!;
    }
}
