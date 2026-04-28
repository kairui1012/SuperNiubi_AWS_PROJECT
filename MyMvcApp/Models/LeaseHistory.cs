using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyMvcApp.Models
{
    public class LeaseHistory
    {
        [Key]
        public int LeaseHistoryId { get; set; }

        [ForeignKey("Tenant")]
        public int TenantId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? OldValue { get; set; }

        [MaxLength(1000)]
        public string? NewValue { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        [Required]
        [MaxLength(256)]
        public string ChangedByEmail { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Tenant Tenant { get; set; } = null!;
    }
}
