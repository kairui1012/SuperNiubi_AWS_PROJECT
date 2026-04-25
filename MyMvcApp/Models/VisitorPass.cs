using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyMvcApp.Models
{
    public enum VisitorPassStatus
    {
        Active,
        Used,
        Expired,
        Cancelled
    }

    public class VisitorPass
    {
        [Key]
        public int VisitorPassId { get; set; }

        [ForeignKey("Tenant")]
        public int TenantId { get; set; }

        [Required, MaxLength(120)]
        public string VisitorName { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? VisitorPhone { get; set; }

        [Required, MaxLength(160)]
        public string Purpose { get; set; } = string.Empty;

        [Required]
        public DateTime VisitDate { get; set; }

        [Required, MaxLength(64)]
        public string PassCode { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string QrPayload { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Notes { get; set; }

        public VisitorPassStatus Status { get; set; } = VisitorPassStatus.Active;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Tenant Tenant { get; set; } = null!;
    }
}