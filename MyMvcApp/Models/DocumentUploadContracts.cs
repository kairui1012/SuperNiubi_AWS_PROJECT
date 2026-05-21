using System.ComponentModel.DataAnnotations;

namespace MyMvcApp.Models
{
    public class CreateDirectDocumentUploadRequest
    {
        [Required, StringLength(200)]
        public string DocumentName { get; set; } = string.Empty;

        [Required]
        public DocumentType? DocumentType { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Required, StringLength(260)]
        public string FileName { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string ContentType { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long FileSize { get; set; }

        public int? PropertyId { get; set; }
        public int? TenantId { get; set; }
    }

    public class DirectDocumentUploadResponse
    {
        public int DocumentId { get; set; }
        public string UploadId { get; set; } = string.Empty;
        public string FileKey { get; set; } = string.Empty;
        public string UploadUrl { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class DocumentUploadStatusResponse
    {
        public int DocumentId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ValidationMessage { get; set; }
    }

    public class S3ObjectCreatedUploadNotification
    {
        public string? BucketName { get; set; }
        public string Key { get; set; } = string.Empty;
        public string? ETag { get; set; }
        public long? Size { get; set; }
    }
}
