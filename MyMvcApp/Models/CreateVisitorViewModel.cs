using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class CreateVisitorViewModel
    {
        [Required]
        [StringLength(120)]
        public string VisitorName { get; set; } = string.Empty;

        [StringLength(30)]
        public string? VisitorPhone { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime? VisitDate { get; set; }

        [Required]
        [StringLength(160)]
        public string Purpose { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Notes { get; set; }
    }
}