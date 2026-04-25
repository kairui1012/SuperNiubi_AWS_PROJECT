using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class CreatePaymentViewModel
    {
        [Required]
        [Range(1, 12)]
        public int PaymentMonth { get; set; }

        [Required]
        [Range(2000, 2100)]
        public int PaymentYear { get; set; }

        [Required]
        [Range(typeof(decimal), "0.01", "999999999")]
        public decimal Amount { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime? PaymentDate { get; set; }

        public PaymentMethod? PaymentMethod { get; set; }
    }
}
