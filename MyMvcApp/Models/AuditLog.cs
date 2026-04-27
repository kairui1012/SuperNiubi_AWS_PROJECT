using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class AuditLog
    {
        [Key]
        public int AuditLogId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string ActorEmail { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string TargetType { get; set; } = string.Empty;

        public int? TargetId { get; set; }

        [MaxLength(256)]
        public string? TargetEmail { get; set; }

        public string? Details { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
