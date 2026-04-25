namespace MyMvcApp.Models
{
    public class TenantVisitorsViewModel
    {
        public string PropertyName { get; set; } = string.Empty;
        public List<VisitorPass> Visitors { get; set; } = new();
        public CreateVisitorViewModel NewVisitor { get; set; } = new();
        public VisitorPass? LatestPass { get; set; }
        public string? GeneratedQrCodeDataUrl { get; set; }
    }
}