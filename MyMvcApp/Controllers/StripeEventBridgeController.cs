using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.XRay.Recorder.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyMvcApp.Services;

namespace MyMvcApp.Controllers
{
    /// <summary>
    /// Receives Stripe payment events from EventBridge and internal Lambda callbacks.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/stripe-eventbridge")]
    public class StripeEventBridgeController : ControllerBase
    {
        /// <summary>
        /// Reads shared secrets and internal API keys from application configuration.
        /// </summary>
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Writes Stripe EventBridge processing diagnostics.
        /// </summary>
        private readonly ILogger<StripeEventBridgeController> _logger;

        /// <summary>
        /// Processes Stripe EventBridge payloads and internal confirmation requests.
        /// </summary>
        private readonly StripeEventBridgeProcessingService _eventProcessor;

        /// <summary>
        /// Creates a controller instance with configuration, logging, and Stripe event processing services.
        /// </summary>
        public StripeEventBridgeController(
            IConfiguration configuration,
            ILogger<StripeEventBridgeController> logger,
            StripeEventBridgeProcessingService eventProcessor)
        {
            _configuration = configuration;
            _logger = logger;
            _eventProcessor = eventProcessor;
        }

        /// <summary>
        /// Processes an EventBridge-delivered Stripe webhook payload.
        /// </summary>
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

        /// <summary>
        /// Confirms a Stripe payment through the internal Lambda confirmation endpoint.
        /// </summary>
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

        /// <summary>
        /// Converts the Stripe event processing result into the matching HTTP response.
        /// </summary>
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

        /// <summary>
        /// Validates the shared secret for incoming EventBridge webhook requests.
        /// </summary>
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

        /// <summary>
        /// Validates the internal API key for Lambda-originated requests.
        /// </summary>
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

        /// <summary>
        /// Compares two secrets using fixed-time comparison to reduce timing leaks.
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
