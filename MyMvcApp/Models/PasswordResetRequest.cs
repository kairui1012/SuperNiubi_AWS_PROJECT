using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyMvcApp.Models
{
    public enum PasswordResetRequestStatus
    {
        Pending,
        Approved,
        Rejected
    }

    public class PasswordResetRequest
    {
        public int PasswordResetRequestId { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public int? AppUserId { get; set; }

        [ForeignKey(nameof(AppUserId))]
        public AppUser? AppUser { get; set; }

        public PasswordResetRequestStatus Status { get; set; } = PasswordResetRequestStatus.Pending;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReviewedAt { get; set; }

        public string? ReviewedByEmail { get; set; }
    }
}
