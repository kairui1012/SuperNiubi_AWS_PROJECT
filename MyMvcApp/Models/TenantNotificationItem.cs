namespace MyMvcApp.Models
{
    public class TenantNotificationItem
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? ActionText { get; set; }
        public string? ActionUrl { get; set; }
    }
}
