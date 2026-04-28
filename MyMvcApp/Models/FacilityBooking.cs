using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MyMvcApp.Models
{
    public enum BookingStatus { Pending, Confirmed, Cancelled }
    public enum BookingPaymentStatus { Pending, Paid, Failed }

    public class FacilityBooking
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Facility")]
        public int FacilityId { get; set; }

        // Nullable because visitors might book without an account
        [ForeignKey("AppUser")]
        public int? AppUserId { get; set; }

        // Visitor details
        [MaxLength(100)]
        public string? GuestName { get; set; }
        [MaxLength(100)]
        public string? GuestEmail { get; set; }
        [MaxLength(20)]
        public string? GuestPhone { get; set; }

        [Required]
        public DateTime BookingDate { get; set; }
        [Required]
        public TimeSpan StartTime { get; set; }
        [Required]
        public TimeSpan EndTime { get; set; }

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

        // Stripe tracking
        public string? StripeSessionId { get; set; }
        public string? StripePaymentIntentId { get; set; }

        // Digital Pass Code for the Guard
        public string? PassCode { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ValidateNever]
        public Facility Facility { get; set; } = null!;

        [ValidateNever]
        public AppUser? AppUser { get; set; }

        [ValidateNever]
        public PromoCode? PromoCode { get; set; }
    }
}