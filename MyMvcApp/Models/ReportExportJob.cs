using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public enum ReportExportStatus { Pending, Processing, Completed, Failed }

    public class ReportExportJob
    {
        [Key]
        public int ReportExportJobId { get; set; }

        [Required, MaxLength(100)]
        public string ReportType { get; set; } = "PaymentCsv";

        [Required, MaxLength(256)]
        public string RequestedByEmail { get; set; } = string.Empty;

        public ReportExportStatus Status { get; set; } = ReportExportStatus.Pending;

        public string? FilterJson { get; set; }

        [MaxLength(255)]
        public string? S3Bucket { get; set; }

        [MaxLength(500)]
        public string? S3Key { get; set; }

        [MaxLength(255)]
        public string? FileName { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
