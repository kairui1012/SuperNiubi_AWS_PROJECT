namespace MyMvcApp.Models
{
    public class LandlordDashboardViewModel
    {
        public string LandlordEmail { get; set; } = string.Empty;
        public int MyPropertiesCount { get; set; }
        public decimal MonthlyRentalIncome { get; set; }
        public int UnpaidTenantsCount { get; set; }
        public int ActiveMaintenanceRequestsCount { get; set; }
        public int TenantCount { get; set; }
        public int VacantPropertiesCount { get; set; }
        public List<Property> RecentProperties { get; set; } = new();
        public List<Payment> RecentPayments { get; set; } = new();
        public List<MaintenanceRequest> RecentMaintenanceRequests { get; set; } = new();
    }
}
