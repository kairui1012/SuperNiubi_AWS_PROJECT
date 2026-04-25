namespace MyMvcApp.Models
{
    public class TenantDashboardViewModel
    {
        public string TenantEmail { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string PropertyAddress { get; set; } = string.Empty;
        public DateTime LeaseStartDate { get; set; }

        public DateTime LeaseEndDate { get; set; }
        public string LeaseStatus { get; set; } = string.Empty;
        public List<MaintenanceRequest> MaintenanceRequest { get; set; } = new();
        public int PaymentRecord { get; set; }
        public int DocumentQuantity { get; set; }
        public int VisitorPassCount { get; set; }
        public decimal MonthlyRent { get; set; }
    }
}
