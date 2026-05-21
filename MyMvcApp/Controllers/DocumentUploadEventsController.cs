using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyMvcApp.Models;
using MyMvcApp.Services;

namespace MyMvcApp.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/document-uploads")]
    public class DocumentUploadEventsController : ControllerBase
    {
        private readonly DocumentUploadService _documentUploadService;
        private readonly IConfiguration _configuration;

        public DocumentUploadEventsController(DocumentUploadService documentUploadService, IConfiguration configuration)
        {
            _documentUploadService = documentUploadService;
            _configuration = configuration;
        }

        [HttpPost("s3-object-created")]
        public async Task<IActionResult> S3ObjectCreated([FromBody] S3ObjectCreatedUploadNotification notification)
        {
            var configuredKey = _configuration["InternalApi:Key"] ?? _configuration["EventBridge:SharedSecret"];
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Internal API key is not configured." });
            }

            if (!Request.Headers.TryGetValue("X-Internal-Api-Key", out var suppliedKey) ||
                !string.Equals(suppliedKey.ToString(), configuredKey, StringComparison.Ordinal))
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
    }
}
