using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MyMvcApp.Models;

namespace MyMvcApp.Models
{
    public enum DepositStatus { Pending, Paid, Refunded }
    public enum LeaseStatus { Active, Expired, Terminated }

    public class Tenant
    {
        [Key]
        public int TenantId { get; set; }

        [ForeignKey("AppUser")]
        public int UserId { get; set; }

        [ForeignKey("Property")]
        public int PropertyId { get; set; }

        [Required]
        public DateTime LeaseStartDate { get; set; }

        [Required]
        public DateTime LeaseEndDate { get; set; }

        [Required]
        public decimal MonthlyRent { get; set; }

        public decimal DepositPaid { get; set; } = 0;
        public DepositStatus DepositStatus { get; set; } = DepositStatus.Pending;
        public int RentDueDay { get; set; } = 1;
        public LeaseStatus LeaseStatus { get; set; } = LeaseStatus.Active;


        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public AppUser User { get; set; } = null!;
        public Property Property { get; set; } = null!;
        public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}