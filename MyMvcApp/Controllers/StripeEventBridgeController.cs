using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.XRay.Recorder.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyMvcApp.Services;

namespace MyMvcApp.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/stripe-eventbridge")]
    public class StripeEventBridgeController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<StripeEventBridgeController> _logger;
        private readonly StripeEventBridgeProcessingService _eventProcessor;

        public StripeEventBridgeController(
            IConfiguration configuration,
            ILogger<StripeEventBridgeController> logger,
            StripeEventBridgeProcessingService eventProcessor)
        {
            _configuration = configuration;
            _logger = logger;
            _eventProcessor = eventProcessor;
        }

        [HttpPost]
        public async Task<IActionResult> Receive([FromBody] JsonElement payload)
        {
            if (!IsAuthorizedEventBridgeRequest())
            {
                return Unauthorized();
            }

            var summary = StripeEventBridgeProcessingService.ReadEventSummary(payload);
            AWSXRayRecorder.Instance.BeginSubsegment("ProcessStripeEventBridgeWebhook");

            try
            {
                if (!string.IsNullOrEmpty(summary.EventId))
                {
                    AWSXRayRecorder.Instance.AddAnnotation("StripeEventId", summary.EventId);
                }

                if (!string.IsNullOrEmpty(summary.EventType))
                {
                    AWSXRayRecorder.Instance.AddAnnotation("EventType", summary.EventType);
                }

                var result = await _eventProcessor.ProcessEventBridgeEventAsync(payload);
                return ToActionResult(result);
            }
            catch (Exception ex)
            {
                AWSXRayRecorder.Instance.AddAnnotation("WebhookStatus", "Failed");
                AWSXRayRecorder.Instance.AddMetadata("WebhookError", "ExceptionMessage", ex.Message);
                AWSXRayRecorder.Instance.AddMetadata("WebhookError", "StackTrace", ex.StackTrace);

                _logger.LogError(ex, "Failed to process Stripe EventBridge event {EventType} {EventId}.", summary.EventType, summary.EventId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            finally
            {
                AWSXRayRecorder.Instance.EndSubsegment();
            }
        }

        [HttpPost("stripe-confirm")]
        public async Task<IActionResult> StripeConfirm([FromBody] StripePaymentConfirmedRequest request)
        {
            if (!IsAuthorizedLambdaRequest())
            {
                return Unauthorized("Invalid internal API key");
            }

            var result = await _eventProcessor.ConfirmPaymentAsync(request);
            return ToActionResult(result);
        }

        private IActionResult ToActionResult(StripeEventProcessResult result)
        {
            if (result.Body is not null)
            {
                return StatusCode(result.StatusCode, result.Body);
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                return StatusCode(result.StatusCode, result.Message);
            }

            return StatusCode(result.StatusCode);
        }

        private bool IsAuthorizedEventBridgeRequest()
        {
            var expectedSecret = _configuration["EventBridge:SharedSecret"];
            if (string.IsNullOrWhiteSpace(expectedSecret))
            {
                return true;
            }

            if (!Request.Headers.TryGetValue("X-EventBridge-Secret", out var providedSecret))
            {
                return false;
            }

            return FixedTimeEquals(expectedSecret, providedSecret.ToString());
        }

        private bool IsAuthorizedLambdaRequest()
        {
            var expectedKey = _configuration["InternalApi:Key"];
            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                expectedKey = _configuration["EventBridge:SharedSecret"];
            }

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                _logger.LogWarning("Stripe Lambda confirmation endpoint is missing InternalApi:Key.");
                return false;
            }

            if (!Request.Headers.TryGetValue("X-Internal-Api-Key", out var providedKey))
            {
                return false;
            }

            return FixedTimeEquals(expectedKey, providedKey.ToString());
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
