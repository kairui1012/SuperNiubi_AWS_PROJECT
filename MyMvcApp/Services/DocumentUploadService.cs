using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;

namespace MyMvcApp.Services
{
    /// <summary>
    /// Coordinates direct-to-S3 document uploads for tenants and landlords.
    /// The MVC app creates a pending database record and a pre-signed S3 PUT URL,
    /// then the S3 object-created Lambda callback confirms the upload through this service.
    /// CloudWatch records the Lambda/callback logs, and SNS can be attached to alarms for failed confirmations.
    /// </summary>
    public class DocumentUploadService
    {
        private readonly AppDbContext _dbContext;
        private readonly IAmazonS3 _s3Client;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Creates a document upload service with database access, S3 access, and AWS configuration.
        /// </summary>
        public DocumentUploadService(AppDbContext dbContext, IAmazonS3 s3Client, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _s3Client = s3Client;
            _configuration = configuration;
        }

        /// <summary>
        /// Wraps the result of creating a direct upload request so controllers can return either a URL or a validation error.
        /// </summary>
        public sealed record DocumentUploadCreateResult(DirectDocumentUploadResponse? Response, string? ErrorMessage)
        {
            public bool Succeeded => Response is not null;

            /// <summary>
            /// Creates a successful result containing the S3 pre-signed upload URL response.
            /// </summary>
            public static DocumentUploadCreateResult Success(DirectDocumentUploadResponse response)
            {
                return new DocumentUploadCreateResult(response, null);
            }

            /// <summary>
            /// Creates a failed result containing the reason the upload request cannot proceed.
            /// </summary>
            public static DocumentUploadCreateResult Failure(string message)
            {
                return new DocumentUploadCreateResult(null, message);
            }
        }

        /// <summary>
        /// Validates metadata before a pre-signed upload URL is created.
        /// This protects S3 from unsupported file types and oversized uploads.
        /// </summary>
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

        /// <summary>
        /// Creates a pending tenant document record and returns a pre-signed S3 PUT URL for browser direct upload.
        /// </summary>
        public async Task<DocumentUploadCreateResult> CreateTenantDirectUploadAsync(
            CreateDirectDocumentUploadRequest? request,
            int uploadedByUserId,
            int tenantId,
            int? propertyId,
            string[] allowedExtensions,
            long maxFileSizeBytes)
        {
            var validationError = ValidateDocumentUploadRequest(request, allowedExtensions, maxFileSizeBytes);
            if (validationError != null)
            {
                return DocumentUploadCreateResult.Failure(validationError);
            }

            var bucketName = _configuration["AWS:BucketName"];
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                return DocumentUploadCreateResult.Failure("S3 bucket is not configured.");
            }

            request ??= new CreateDirectDocumentUploadRequest();
            var document = CreatePendingDocument(
                request,
                uploadedByUserId,
                propertyId,
                tenantId,
                $"tenant/{tenantId}/documents",
                bucketName);

            return DocumentUploadCreateResult.Success(
                await SavePendingDocumentAndCreateUploadAsync(document, request.ContentType));
        }

        /// <summary>
        /// Creates a pending landlord document record after verifying the selected property or tenant belongs to the landlord.
        /// </summary>
        public async Task<DocumentUploadCreateResult> CreateLandlordDirectUploadAsync(
            CreateDirectDocumentUploadRequest? request,
            int landlordId,
            string[] allowedExtensions,
            long maxFileSizeBytes)
        {
            if (request is null)
            {
                return DocumentUploadCreateResult.Failure("Upload request payload is invalid.");
            }

            if (request.PropertyId == null && request.TenantId == null)
            {
                return DocumentUploadCreateResult.Failure("Please select a property or tenant.");
            }

            Tenant? tenant = null;
            Property? property = null;

            if (request.TenantId.HasValue)
            {
                tenant = await _dbContext.Tenants
                    .Include(t => t.Property)
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t =>
                        t.TenantId == request.TenantId.Value &&
                        t.Property.LandlordId == landlordId);

                if (tenant is null)
                {
                    return DocumentUploadCreateResult.Failure("Selected tenant is invalid.");
                }

                property = tenant.Property;
            }

            if (request.PropertyId.HasValue)
            {
                var selectedProperty = await _dbContext.Properties
                    .FirstOrDefaultAsync(p =>
                        p.PropertyId == request.PropertyId.Value &&
                        p.LandlordId == landlordId &&
                        !p.IsDeleted);

                if (selectedProperty is null)
                {
                    return DocumentUploadCreateResult.Failure("Selected property is invalid.");
                }

                if (property != null && selectedProperty.PropertyId != property.PropertyId)
                {
                    return DocumentUploadCreateResult.Failure("Selected property does not match the tenant.");
                }

                property = selectedProperty;
            }

            var validationError = ValidateDocumentUploadRequest(request, allowedExtensions, maxFileSizeBytes);
            if (validationError != null)
            {
                return DocumentUploadCreateResult.Failure(validationError);
            }

            var bucketName = _configuration["AWS:BucketName"];
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                return DocumentUploadCreateResult.Failure("S3 bucket is not configured.");
            }

            var document = CreatePendingDocument(
                request,
                landlordId,
                property?.PropertyId,
                tenant?.TenantId,
                $"landlord/{landlordId}/documents",
                bucketName);

            return DocumentUploadCreateResult.Success(
                await SavePendingDocumentAndCreateUploadAsync(document, request.ContentType));
        }

        /// <summary>
        /// Builds the temporary S3 PUT URL used by the browser to upload the file directly to S3.
        /// </summary>
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

        /// <summary>
        /// Builds the permanent S3 object URL stored in document metadata.
        /// </summary>
        public string BuildS3Url(string fileKey)
        {
            var bucketName = _configuration["AWS:BucketName"] ?? string.Empty;
            var region = _configuration["AWS:Region"] ?? "us-east-1";
            return $"https://{bucketName}.s3.{region}.amazonaws.com/{fileKey}";
        }

        /// <summary>
        /// Returns the upload status visible to a tenant while Lambda confirmation is still pending.
        /// </summary>
        public async Task<DocumentUploadStatusResponse?> GetTenantUploadStatusAsync(int documentId, int tenantId)
        {
            var document = await _dbContext.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DocumentId == documentId && d.TenantId == tenantId && !d.IsDeleted);

            return document is null ? null : ToStatusResponse(document);
        }

        /// <summary>
        /// Converts a document entity into the lightweight status response used by AJAX polling.
        /// </summary>
        public DocumentUploadStatusResponse ToStatusResponse(Document document)
        {
            return new DocumentUploadStatusResponse
            {
                DocumentId = document.DocumentId,
                Status = document.UploadStatus.ToString(),
                ValidationMessage = document.ValidationMessage
            };
        }

        /// <summary>
        /// Creates the database record before the file exists in S3 so the later Lambda callback has a key to match.
        /// </summary>
        private Document CreatePendingDocument(
            CreateDirectDocumentUploadRequest request,
            int uploadedByUserId,
            int? propertyId,
            int? tenantId,
            string keyPrefix,
            string bucketName)
        {
            var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
            var uploadId = Guid.NewGuid().ToString("N");
            var fileKey = $"{keyPrefix}/{uploadId}{extension}";

            return new Document
            {
                UploadedBy = uploadedByUserId,
                PropertyId = propertyId,
                TenantId = tenantId,
                DocumentName = request.DocumentName.Trim(),
                DocumentType = request.DocumentType!.Value,
                FileKey = fileKey,
                FileSize = (int)Math.Min(request.FileSize, int.MaxValue),
                FileType = request.ContentType,
                S3BucketName = bucketName,
                S3Url = BuildS3Url(fileKey),
                Notes = request.Notes,
                UploadStatus = DocumentUploadStatus.PendingUpload,
                UploadId = uploadId,
                UploadUrlExpiresAt = DateTime.UtcNow.AddMinutes(15),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Saves the pending document first, then creates the S3 upload URL tied to that document key.
        /// </summary>
        private async Task<DirectDocumentUploadResponse> SavePendingDocumentAndCreateUploadAsync(Document document, string contentType)
        {
            _dbContext.Documents.Add(document);
            await _dbContext.SaveChangesAsync();

            var response = CreatePresignedPutUrl(document, contentType);
            document.UploadUrlExpiresAt = response.ExpiresAtUtc;
            await _dbContext.SaveChangesAsync();

            return response;
        }

        /// <summary>
        /// Confirms an S3 object-created event reported by Lambda.
        /// The service checks the bucket, finds the pending document by object key,
        /// verifies S3 metadata, then marks the upload as confirmed or failed.
        /// </summary>
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
