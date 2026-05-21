using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;

namespace MyMvcApp.Services
{
    public class DocumentUploadService
    {
        private readonly AppDbContext _dbContext;
        private readonly IAmazonS3 _s3Client;
        private readonly IConfiguration _configuration;

        public DocumentUploadService(AppDbContext dbContext, IAmazonS3 s3Client, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _s3Client = s3Client;
            _configuration = configuration;
        }

        public string? ValidateDocumentUploadRequest(
            CreateDirectDocumentUploadRequest? request,
            string[] allowedExtensions,
            long maxFileSizeBytes)
        {
            if (request is null)
            {
                return "Upload request payload is invalid.";
            }

            if (request.DocumentType is null)
            {
                return "Document type is required.";
            }

            if (string.IsNullOrWhiteSpace(request.DocumentName))
            {
                return "Document name is required.";
            }

            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                return "File name is required.";
            }

            if (request.FileSize <= 0)
            {
                return "Please choose a valid file.";
            }

            if (request.FileSize > maxFileSizeBytes)
            {
                return $"File size must not exceed {maxFileSizeBytes / 1024 / 1024}MB.";
            }

            var extension = Path.GetExtension(request.FileName);
            if (string.IsNullOrWhiteSpace(extension) ||
                !allowedExtensions.Contains(extension.ToLowerInvariant()))
            {
                return $"Allowed file types: {string.Join(", ", allowedExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()))}.";
            }

            return null;
        }

        public DirectDocumentUploadResponse CreatePresignedPutUrl(Document document, string contentType)
        {
            var bucketName = _configuration["AWS:BucketName"];
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                throw new InvalidOperationException("S3 bucket is not configured.");
            }

            var expiresAt = DateTime.UtcNow.AddMinutes(
                Math.Clamp(_configuration.GetValue<int?>("AWS:UploadUrlExpiryMinutes") ?? 15, 1, 60));

            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = document.FileKey,
                Verb = HttpVerb.PUT,
                ContentType = contentType,
                Expires = expiresAt
            };

            return new DirectDocumentUploadResponse
            {
                DocumentId = document.DocumentId,
                UploadId = document.UploadId,
                FileKey = document.FileKey,
                UploadUrl = _s3Client.GetPreSignedURL(request),
                ExpiresAtUtc = expiresAt,
                Status = document.UploadStatus.ToString()
            };
        }

        public string BuildS3Url(string fileKey)
        {
            var bucketName = _configuration["AWS:BucketName"] ?? string.Empty;
            var region = _configuration["AWS:Region"] ?? "us-east-1";
            return $"https://{bucketName}.s3.{region}.amazonaws.com/{fileKey}";
        }

        public async Task<DocumentUploadStatusResponse?> ConfirmS3ObjectCreatedAsync(string fileKey, string? bucketName, string? eTag)
        {
            var configuredBucket = _configuration["AWS:BucketName"];
            if (string.IsNullOrWhiteSpace(configuredBucket))
            {
                throw new InvalidOperationException("S3 bucket is not configured.");
            }

            if (!string.IsNullOrWhiteSpace(bucketName) &&
                !string.Equals(bucketName, configuredBucket, StringComparison.Ordinal))
            {
                return null;
            }

            var document = await _dbContext.Documents
                .FirstOrDefaultAsync(d => d.FileKey == fileKey && !d.IsDeleted);

            if (document is null)
            {
                return null;
            }

            if (document.UploadStatus == DocumentUploadStatus.Confirmed)
            {
                return new DocumentUploadStatusResponse
                {
                    DocumentId = document.DocumentId,
                    Status = document.UploadStatus.ToString(),
                    ValidationMessage = document.ValidationMessage
                };
            }

            try
            {
                var metadata = await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = configuredBucket,
                    Key = fileKey
                });

                if (document.FileSize.HasValue && metadata.ContentLength != document.FileSize.Value)
                {
                    document.UploadStatus = DocumentUploadStatus.FailedValidation;
                    document.ValidationMessage = $"S3 object size {metadata.ContentLength} does not match expected size {document.FileSize.Value}.";
                }
                else
                {
                    document.UploadStatus = DocumentUploadStatus.Confirmed;
                    document.ConfirmedAt = DateTime.UtcNow;
                    document.ValidationMessage = "S3 upload confirmed.";
                    document.S3ETag = eTag ?? metadata.ETag;
                }

                document.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                return new DocumentUploadStatusResponse
                {
                    DocumentId = document.DocumentId,
                    Status = document.UploadStatus.ToString(),
                    ValidationMessage = document.ValidationMessage
                };
            }
            catch (AmazonS3Exception ex)
            {
                document.UploadStatus = DocumentUploadStatus.FailedValidation;
                document.ValidationMessage = $"S3 metadata check failed: {ex.Message}";
                document.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                return new DocumentUploadStatusResponse
                {
                    DocumentId = document.DocumentId,
                    Status = document.UploadStatus.ToString(),
                    ValidationMessage = document.ValidationMessage
                };
            }
        }
    }
}
