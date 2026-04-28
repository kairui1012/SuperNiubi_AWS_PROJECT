using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MyMvcApp.Models
{
    public enum PropertyType { Apartment, House, Condo, Studio, Commercial }
    public enum PropertyAvailabilityStatus { Available, Occupied, Maintenance, Unavailable }
    public enum PropertyApprovalStatus { Pending, Approved, Rejected }

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

        // --- Short-Term Rental (Airbnb) Fields ---
        public decimal? DailyRate { get; set; }
        public bool AllowShortTerm { get; set; } = false;

        [MaxLength(20)]
        public string? ParkingBay { get; set; }

        public string? Description { get; set; }

        [MaxLength(1000)]
        public string? ImageUrl { get; set; }

        public PropertyAvailabilityStatus AvailabilityStatus { get; set; } = PropertyAvailabilityStatus.Available;

        public PropertyApprovalStatus ApprovalStatus { get; set; } = PropertyApprovalStatus.Pending;

        public bool IsDeleted { get; set; } = false;

        public DateTime? DeletedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [ValidateNever]
        public AppUser? Landlord { get; set; }

        [ValidateNever]
        public Tenant? Tenant { get; set; }

        [ValidateNever]
        public ICollection<PropertyAmenity> Amenities { get; set; } = new List<PropertyAmenity>();

        [ValidateNever]
        public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();

        [ValidateNever]
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();

        [ValidateNever]
        public ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}
