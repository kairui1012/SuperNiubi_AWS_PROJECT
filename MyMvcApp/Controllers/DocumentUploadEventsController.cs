using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyMvcApp.Models;
using MyMvcApp.Services;
using System.Security.Cryptography;
using System.Text;

namespace MyMvcApp.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/document-uploads")]
    public class DocumentUploadEventsController : ControllerBase
    {
        private readonly DocumentUploadService _documentUploadService;
        private readonly InternalApiKeyProvider _internalApiKeyProvider;

        public DocumentUploadEventsController(
            DocumentUploadService documentUploadService,
            InternalApiKeyProvider internalApiKeyProvider)
        {
            _documentUploadService = documentUploadService;
            _internalApiKeyProvider = internalApiKeyProvider;
        }

        [HttpPost("s3-object-created")]
        public async Task<IActionResult> S3ObjectCreated([FromBody] S3ObjectCreatedUploadNotification notification)
        {
            var configuredKey = await _internalApiKeyProvider.GetInternalApiKeyAsync();
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Internal API key or secret id is not configured." });
            }

            if (!Request.Headers.TryGetValue("X-Internal-Api-Key", out var suppliedKey) ||
                !FixedTimeEquals(configuredKey, suppliedKey.ToString()))
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

        private static bool FixedTimeEquals(string expected, string provided)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var providedBytes = Encoding.UTF8.GetBytes(provided);

            return expectedBytes.Length == providedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }
    }
}
