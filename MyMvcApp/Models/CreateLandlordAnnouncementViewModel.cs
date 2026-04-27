using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class CreateLandlordAnnouncementViewModel
    {
        [Required(ErrorMessage = "Title is required.")]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Message is required.")]
        [MaxLength(2000)]
        public string Body { get; set; } = string.Empty;

        [Required]
        public string VisibleTo { get; set; } = "All";
    }
}
