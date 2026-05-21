using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using MyMvcApp.Models;
using MyMvcApp.Services;
using System.Text.Json;

namespace MyMvcApp.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/document-uploads")]
    public class DocumentUploadEventsController : ControllerBase
    {
        private readonly DocumentUploadService _documentUploadService;
        private readonly IConfiguration _configuration;
        private readonly IAmazonSecretsManager _secretsManager;

        public DocumentUploadEventsController(
            DocumentUploadService documentUploadService,
            IConfiguration configuration,
            IAmazonSecretsManager secretsManager)
        {
            _documentUploadService = documentUploadService;
            _configuration = configuration;
            _secretsManager = secretsManager;
        }

        [HttpPost("s3-object-created")]
        public async Task<IActionResult> S3ObjectCreated([FromBody] S3ObjectCreatedUploadNotification notification)
        {
            var configuredKey = await ResolveInternalApiKeyAsync();
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

        private async Task<string?> ResolveInternalApiKeyAsync()
        {
            var configuredKey = _configuration["InternalApi:Key"];
            if (!string.IsNullOrWhiteSpace(configuredKey))
            {
                return configuredKey;
            }

            var secretId = _configuration["InternalApi:SecretId"];
            if (string.IsNullOrWhiteSpace(secretId))
            {
                return null;
            }

            var response = await _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = secretId
            });

            return ExtractInternalApiKey(response.SecretString);
        }

        private static string? ExtractInternalApiKey(string? secretString)
        {
            if (string.IsNullOrWhiteSpace(secretString))
            {
                return null;
            }

            var trimmed = secretString.Trim();
            if (!trimmed.StartsWith('{'))
            {
                return trimmed;
            }

            using var document = JsonDocument.Parse(trimmed);
            foreach (var key in new[] { "INTERNAL_API_KEY", "InternalApi__Key", "InternalApi:Key", "InternalApiKey", "Key" })
            {
                if (document.RootElement.TryGetProperty(key, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }

            return null;
        }
    }
}
