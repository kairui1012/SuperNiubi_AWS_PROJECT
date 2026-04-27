using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class RenewLeaseViewModel
    {
        public int TenantId { get; set; }

        [Required]
        public DateTime LeaseStartDate { get; set; }

        [Required]
        public DateTime LeaseEndDate { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }

    public class AdjustRentViewModel
    {
        public int TenantId { get; set; }

        [Range(0.01, 999999.99, ErrorMessage = "Monthly rent must be greater than 0.")]
        public decimal MonthlyRent { get; set; }

        [Range(1, 31, ErrorMessage = "Rent due day must be between 1 and 31.")]
        public int RentDueDay { get; set; }
    }

    public class ChangeTenantPropertyViewModel
    {
        public int TenantId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Please select a property.")]
        public int PropertyId { get; set; }
    }

    public class TerminateLeaseViewModel
    {
        public int TenantId { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }

    public class ChangeDepositStatusViewModel
    {
        public int TenantId { get; set; }

        [Required]
        public DepositStatus DepositStatus { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }
}
