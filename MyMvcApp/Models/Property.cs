using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class Property
    {
        public int Id { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public string Address { get; set; }

        [Required]
        public string City { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal RentPrice { get; set; }

        [Range(0, 20)]
        public int Bedrooms { get; set; }

        [Required]
        public string Status { get; set; }

        public string LandlordEmail { get; set; }
    }
}