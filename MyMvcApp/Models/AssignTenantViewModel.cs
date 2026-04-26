using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MyMvcApp.Models
{
    public class AssignTenantViewModel
    {
        [Required(ErrorMessage = "Please select a tenant.")]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Please select a property.")]
        public int PropertyId { get; set; }

        [Required(ErrorMessage = "Please enter lease start date.")]
        public DateTime LeaseStartDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Please enter lease end date.")]
        public DateTime LeaseEndDate { get; set; } = DateTime.Today.AddYears(1);

        [Required(ErrorMessage = "Please enter monthly rent.")]
        public decimal MonthlyRent { get; set; }

        public decimal DepositPaid { get; set; } = 0;

        public DepositStatus DepositStatus { get; set; } = DepositStatus.Pending;

        public int RentDueDay { get; set; } = 1;

        public string? Notes { get; set; }

        public List<SelectListItem> TenantUsers { get; set; } = new List<SelectListItem>();

        public List<SelectListItem> Properties { get; set; } = new List<SelectListItem>();
    }
}