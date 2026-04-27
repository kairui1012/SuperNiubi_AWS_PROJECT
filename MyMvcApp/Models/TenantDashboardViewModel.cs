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
        public DateTime NextPaymentDue { get; set; }
        public string MaintenanceStatusSummary { get; set; } = string.Empty;
        public int OpenMaintenanceCount { get; set; }
        public List<TenantNotificationItem> Notifications { get; set; } = new();
        public List<string> PaymentChartLabels { get; set; } = new();
        public List<decimal> PaymentChartAmounts { get; set; } = new();
        public List<int> MaintenanceStatusCounts { get; set; } = new();
    }
}
