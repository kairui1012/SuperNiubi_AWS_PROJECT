using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MyMvcApp.Models;

namespace MyMvcApp.Models
{
    public enum MaintenanceCategory { Plumbing, Electrical, AirConditioning, Structural, Appliances, PestControl, Others }
    public enum MaintenancePriority { High, Medium, Low }
    public enum MaintenanceStatus { Pending, Approved, InProgress, Completed, Rejected }

    public class MaintenanceRequest
    {
        [Key]
        public int RequestId { get; set; }

        [ForeignKey("Tenant")]
        public int TenantId { get; set; }

        [ForeignKey("Property")]
        public int PropertyId { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public MaintenanceCategory Category { get; set; }

        public MaintenancePriority Priority { get; set; } = MaintenancePriority.Medium;

        [Required]
        public string Description { get; set; } = string.Empty;

        public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Pending;
        public DateTime? PreferredDate { get; set; }
        public DateTime? ResolvedDate { get; set; }
        public string? LandlordRemarks { get; set; }
        [MaxLength(200)]
        public string? AssignedVendor { get; set; }
        public decimal? EstimatedRepairCost { get; set; }
        [MaxLength(500)]
        public string? IssueImageKey { get; set; }
        [MaxLength(500)]
        public string? RepairImageKey { get; set; }
        public DateTime? TenantConfirmedAt { get; set; }
        [Range(1, 5)]
        public int? TenantFeedbackRating { get; set; }
        [MaxLength(1000)]
        public string? TenantFeedbackComment { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Tenant Tenant { get; set; } = null!;
        public Property Property { get; set; } = null!;
        public ICollection<MaintenanceTimeline> Timeline { get; set; } = new List<MaintenanceTimeline>();
    }

    public class MaintenanceTimeline
    {
        [Key]
        public int MaintenanceTimelineId { get; set; }

        [ForeignKey("MaintenanceRequest")]
        public int RequestId { get; set; }

        [Required, MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Details { get; set; }

        [Required, MaxLength(256)]
        public string ActorEmail { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public MaintenanceRequest MaintenanceRequest { get; set; } = null!;
    }
}
