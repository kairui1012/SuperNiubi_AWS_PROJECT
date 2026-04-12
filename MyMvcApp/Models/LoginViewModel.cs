using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class LoginViewModel
    {
        [Required]
        [Display(Name = "Nickname")]
        public string Nickname { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        public bool RememberMe { get; set; }
    }
}