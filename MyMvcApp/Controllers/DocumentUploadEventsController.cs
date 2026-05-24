using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using MyMvcApp.Models;
using MyMvcApp.Services;
using System.Security.Cryptography;
using System.Text;

namespace MyMvcApp.Controllers
{
    /// <summary>
    /// Receives internal document upload events and confirms completed S3 uploads.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/document-uploads")]
    public class DocumentUploadEventsController : ControllerBase
    {
        /// <summary>
        /// Confirms and finalizes direct document uploads.
        /// </summary>
        private readonly DocumentUploadService _documentUploadService;

        /// <summary>
        /// Provides the internal API key used to authenticate upload event callbacks.
        /// </summary>
        private readonly InternalApiKeyProvider _internalApiKeyProvider;

        /// <summary>
        /// Creates a controller instance with document upload and internal API key services.
        /// </summary>
        public DocumentUploadEventsController(
            DocumentUploadService documentUploadService,
            InternalApiKeyProvider internalApiKeyProvider)
        {
            _documentUploadService = documentUploadService;
            _internalApiKeyProvider = internalApiKeyProvider;
        }

        /// <summary>
        /// Confirms a pending direct document upload after S3 reports that the object was created.
        /// </summary>
        [HttpPost("s3-object-created")]
        public async Task<IActionResult> S3ObjectCreated([FromBody] S3ObjectCreatedUploadNotification notification)
        {
            var keyLookup = await _internalApiKeyProvider.GetInternalApiKeyLookupAsync();
            if (!keyLookup.HasKey)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = keyLookup.Message });
            }

            if (!Request.Headers.TryGetValue("X-Internal-Api-Key", out var suppliedKey) ||
                !FixedTimeEquals(keyLookup.Key!, suppliedKey.ToString()))
            {
                return Unauthorized(new { message = "Invalid internal API key." });
            }

            if (string.IsNullOrWhiteSpace(notification.Key))
            {
                return BadRequest(new { message = "S3 object key is required." });
            }

            var result = await _documentUploadService.ConfirmS3ObjectCreatedAsync(
                notification.Key,
                notification.BucketName,
                notification.ETag);

            if (result is null)
            {
                return Accepted(new { message = "No matching pending document was found for this object." });
            }

            return Ok(result);
        }

        /// <summary>
        /// Compares API keys using fixed-time comparison to reduce timing leaks.
        /// </summary>
        private static bool FixedTimeEquals(string expected, string provided)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var providedBytes = Encoding.UTF8.GetBytes(provided);

            return expectedBytes.Length == providedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }
    }
}
