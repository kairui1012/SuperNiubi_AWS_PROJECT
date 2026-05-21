using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MyMvcApp.Models;

namespace MyMvcApp.Models
{
    public enum DocumentType { TenancyAgreement, IdentityCard, PaymentReceipt, InspectionReport, Others }
    public enum DocumentUploadStatus { PendingUpload, Confirmed, FailedValidation, Expired }

    public class Document
    {
        [Key]
        public int DocumentId { get; set; }

        [ForeignKey("UploadedByUser")]
        public int UploadedBy { get; set; }

        [ForeignKey("Property")]
        public int? PropertyId { get; set; }

        [ForeignKey("Tenant")]
        public int? TenantId { get; set; }

        [Required, MaxLength(200)]
        public string DocumentName { get; set; } = string.Empty;

        [Required]
        public DocumentType DocumentType { get; set; }

        [Required, MaxLength(500)]
        public string FileKey { get; set; } = string.Empty;

        public int? FileSize { get; set; }

        [MaxLength(50)]
        public string? FileType { get; set; }

        [MaxLength(100)]
        public string? S3BucketName { get; set; }

        [MaxLength(1000)]
        public string? S3Url { get; set; }

        [Required]
        public DocumentUploadStatus UploadStatus { get; set; } = DocumentUploadStatus.Confirmed;

        [Required, MaxLength(64)]
        public string UploadId { get; set; } = Guid.NewGuid().ToString("N");

        public DateTime? UploadUrlExpiresAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }

        [MaxLength(200)]
        public string? S3ETag { get; set; }

        [MaxLength(1000)]
        public string? ValidationMessage { get; set; }

        public string? Notes { get; set; }
        public bool IsDeleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public AppUser UploadedByUser { get; set; } = null!;
        public Property? Property { get; set; }
        public Tenant? Tenant { get; set; }
    }
}
