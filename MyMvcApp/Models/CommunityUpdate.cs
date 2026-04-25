using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public enum UpdateType
    {
        Event,
        Promotion,
        Notice
    }

    public class CommunityUpdate
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public UpdateType Type { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty; // Will store Rich Text (HTML)

        public string? ImageUrl { get; set; } // URL pointing to your S3 Bucket

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime EndDate { get; set; } // When this should disappear from the landing page

        // Optional fields for the Call-To-Action button (e.g., "RSVP Now", "Claim Code")
        [StringLength(50)]
        public string? CallToActionText { get; set; } 
        
        [Url]
        public string? CallToActionUrl { get; set; }
    }
}