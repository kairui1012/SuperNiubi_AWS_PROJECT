using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class CreateMaintenanceViewModel
    {
        [Required(ErrorMessage = "Please key in issue title")]
        [StringLength(200, ErrorMessage = "Title cannot exceed 200 words")]
        [Display(Name = "Issue Title")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please choose a category")]
        public MaintenanceCategory Category { get; set; }

        [Required(ErrorMessage = "Please choose the priority")]
        public MaintenancePriority Priority { get; set; }

        [Required(ErrorMessage = "Please describe choose your preferred date")]
        [Display(Name = "Preferred Date")]
        [DataType(DataType.Date)]
        public DateTime? PreferredDate { get; set; }

        [Required(ErrorMessage = "Please describe actual problem")]
        public string Description { get; set; } = string.Empty;
    }
}
