using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MyMvcApp.Models;

namespace MyMvcApp.Models
{
    public enum PropertyType { Apartment, House, Condo, Studio, Commercial }

    public class Property
    {
        [Key]
        public int PropertyId { get; set; }

        [ForeignKey("Landlord")]
        public int LandlordId { get; set; }

        [Required, MaxLength(150)]
        public string PropertyName { get; set; } = string.Empty;

        [Required]
        public PropertyType PropertyType { get; set; }

        [Required, MaxLength(255)]
        public string AddressLine1 { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? AddressLine2 { get; set; }

        [Required, MaxLength(100)]
        public string City { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string State { get; set; } = string.Empty;

        [MaxLength(10)]
        public string PostalCode { get; set; } = string.Empty;

        [MaxLength(10)]
        public string? FloorNumber { get; set; }

        [MaxLength(20)]
        public string? UnitNumber { get; set; }

        public decimal? SizeSqFt { get; set; }
        public int Bedrooms { get; set; } = 0;
        public int Bathrooms { get; set; } = 0;

        [Required]
        public decimal MonthlyRent { get; set; }

        public decimal? DepositAmount { get; set; }

        [MaxLength(20)]
        public string? ParkingBay { get; set; }

        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public AppUser Landlord { get; set; } = null!;
        public Tenant? Tenant { get; set; }
        public ICollection<PropertyAmenity> Amenities { get; set; } = new List<PropertyAmenity>();
        public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}
