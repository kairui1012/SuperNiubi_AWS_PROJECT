using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MyMvcApp.Models
{
    public class Facility
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public string? ImageUrl { get; set; }

        [Required]
        public decimal HourlyRate { get; set; }

        public bool IsPubliclyAvailable { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ValidateNever]
        public ICollection<FacilityBooking> Bookings { get; set; } = new List<FacilityBooking>();
    }
}