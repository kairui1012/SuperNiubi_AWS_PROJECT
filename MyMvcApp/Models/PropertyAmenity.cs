using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyMvcApp.Models
{
    public class PropertyAmenity
    {
        [Key]
        public int AmenityId { get; set; }

        [ForeignKey("Property")]
        public int PropertyId { get; set; }

        [Required, MaxLength(100)]
        public string AmenityName { get; set; } = string.Empty;

        // Navigation
        public Property Property { get; set; } = null!;
    }
}