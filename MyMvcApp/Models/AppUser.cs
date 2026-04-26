using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class AppUser
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Email { get; set; } = string.Empty;

        public bool IsApproved { get; set; }

        [Required]
        public string Role { get; set; } = "Tenant";

        public bool IsDisabled { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
