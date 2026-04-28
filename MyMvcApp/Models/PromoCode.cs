using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class PromoCode
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(20)]
        public string Code { get; set; } = string.Empty;

        // E.g., 20.00 for 20% off
        public decimal? DiscountPercentage { get; set; }
        
        // E.g., 10.00 for RM10 off
        public decimal? FlatDiscount { get; set; }

        public DateTime ValidFrom { get; set; }
        public DateTime ValidUntil { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}