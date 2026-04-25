namespace MyMvcApp.Models
{
    public class TenantPropertyViewModel
    {
        public string TenantEmail { get; set; } = string.Empty;
        public DateTime LeaseStartDate { get; set; }
        public DateTime LeaseEndDate { get; set; }
        public LeaseStatus LeaseStatus { get; set; }
        public decimal MonthlyRent { get; set; }
        public int RentDueDay { get; set; }
        public decimal DepositPaid { get; set; }
        public DepositStatus DepositStatus { get; set; }

        public Property Property { get; set; } = null!;
        public IReadOnlyList<PropertyAmenity> Amenities { get; set; } = new List<PropertyAmenity>();
    }
}
