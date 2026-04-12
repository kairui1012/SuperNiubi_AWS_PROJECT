using System.ComponentModel.DataAnnotations;

namespace YourAppName.Models
{
    public class RegisterViewModel
    {
        [Required]
        [Display(Name = "Nickname")]
        public string Nickname { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }
}