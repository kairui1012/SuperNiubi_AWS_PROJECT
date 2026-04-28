using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MyMvcApp.Models
{

    public enum BookingStatus { Pending, Confirmed, Cancelled }
    public enum BookingPaymentStatus { Pending, Paid, Failed }
    
    public class PropertyBooking
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Property")]
        public int PropertyId { get; set; }

        [Required, MaxLength(100)]
        public string GuestName { get; set; } = string.Empty;
        [Required, MaxLength(100)]
        public string GuestEmail { get; set; } = string.Empty;
        [Required, MaxLength(20)]
        public string GuestPhone { get; set; } = string.Empty;

        [Required]
        public DateTime CheckInDate { get; set; } // Implicitly 15:00 (3 PM)
        [Required]
        public DateTime CheckOutDate { get; set; } // Implicitly 11:00 (11 AM)

        [ForeignKey("PromoCode")]
        public int? PromoCodeId { get; set; }

        [Required]
        public decimal TotalAmount { get; set; }
        public decimal DiscountAmount { get; set; } = 0;
        [Required]
        public decimal FinalAmount { get; set; }

        [Required]
        public BookingStatus Status { get; set; } = BookingStatus.Pending;

        [Required]
        public BookingPaymentStatus PaymentStatus { get; set; } = BookingPaymentStatus.Pending;

        public string? StripeSessionId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        
        // Multi-day access pass for the guard
        public string? PassCode { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ValidateNever]
        public Property Property { get; set; } = null!;
        [ValidateNever]
        public PromoCode? PromoCode { get; set; }
    }
}