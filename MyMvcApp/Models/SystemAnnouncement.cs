using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class SystemAnnouncement
    {
        [Key]
        public int SystemAnnouncementId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Body { get; set; } = string.Empty;

        // "Tenant", "Landlord", "All"
        [Required]
        [MaxLength(20)]
        public string VisibleTo { get; set; } = "All";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [Required]
        [MaxLength(256)]
        public string CreatedByEmail { get; set; } = string.Empty;
    }
}
