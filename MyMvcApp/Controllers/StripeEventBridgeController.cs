using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using MyMvcApp.Services;
using Stripe;

namespace MyMvcApp.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/stripe-eventbridge")]
    public class StripeEventBridgeController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StripeEventBridgeController> _logger;
        private readonly EmailService _emailService;

        public StripeEventBridgeController(
            AppDbContext context,
            IConfiguration configuration,
            ILogger<StripeEventBridgeController> logger,
            EmailService emailService)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _emailService = emailService;
        }

        [HttpPost]
        public async Task<IActionResult> Receive([FromBody] JsonElement payload)
        {
            if (!IsAuthorizedEventBridgeRequest())
            {
                return Unauthorized();
            }

            var stripeEvent = GetStripeEvent(payload);
            var eventId = ReadString(stripeEvent, "id") ?? ReadString(payload, "id");
            var eventType = ReadString(stripeEvent, "type") ?? ReadString(payload, "detail-type");

            if (string.IsNullOrWhiteSpace(eventType))
            {
                return BadRequest("Stripe event type is missing.");
            }

            try
            {
                return eventType switch
                {
                    "checkout.session.completed" => await HandleCheckoutSessionCompletedAsync(stripeEvent, eventId),
                    "checkout.session.async_payment_failed" => await HandleCheckoutSessionRejectedAsync(stripeEvent, eventId, "Stripe async payment failed."),
                    "checkout.session.expired" => await HandleCheckoutSessionRejectedAsync(stripeEvent, eventId, "Stripe Checkout session expired."),
                    "payment_intent.succeeded" => await HandlePaymentIntentSucceededAsync(stripeEvent, eventId),
                    "payment_intent.payment_failed" => await HandlePaymentIntentFailedAsync(stripeEvent, eventId),
                    _ => Ok(new { ignored = true, eventType })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process Stripe EventBridge event {EventType} {EventId}.", eventType, eventId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("stripe-confirm")]
        public async Task<IActionResult> StripeConfirm([FromBody] StripePaymentConfirmedRequest request)
        {
            if (!IsAuthorizedLambdaRequest())
            {
                return Unauthorized("Invalid internal API key");
            }

            var payment = await FindPaymentFromConfirmationAsync(request);
            if (payment is null)
            {
                return NotFound("Matching local payment was not found.");
            }

            payment.Status = PaymentStatus.Verified;
            payment.StripeSessionId = request.StripeSessionId ?? payment.StripeSessionId;
            payment.StripePaymentIntentId = request.StripePaymentIntentId ?? payment.StripePaymentIntentId;
            payment.StripeEventId = request.StripeEventId ?? payment.StripeEventId;
            payment.StripeReceiptUrl = request.StripeReceiptUrl ?? payment.StripeReceiptUrl;
            payment.ReferenceNo = request.StripePaymentIntentId
                ?? request.StripeSessionId
                ?? payment.ReferenceNo;
            payment.PaymentDate ??= request.PaidAt ?? DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;
            payment.LandlordRemarks = "Payment confirmed via Amazon EventBridge and AWS Lambda.";

            AddAuditLog(
                "StripeLambdaPaymentVerified",
                payment.PaymentId,
                $"Lambda confirmed Stripe payment. Session={payment.StripeSessionId}, PaymentIntent={payment.StripePaymentIntentId}, Event={payment.StripeEventId}.");

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Payment verified",
                payment.PaymentId,
                payment.Status
            });
        }

        private async Task<IActionResult> HandleCheckoutSessionCompletedAsync(JsonElement stripeEvent, string? eventId)
        {
            var dataObject = GetDataObject(stripeEvent);
            if (dataObject.ValueKind != JsonValueKind.Object)
            {
                return BadRequest("Stripe checkout session object is missing.");
            }

            var sessionId = ReadString(dataObject, "id");
            var paymentIntentId = ReadExpandableString(dataObject, "payment_intent");

            // 1. Read the metadata to determine what kind of payment this is
            var transactionType = ReadString(dataObject, "metadata", "TransactionType");

            if (transactionType == "FacilityBooking")
            {
                // ==========================================
                // NEW LOGIC: FACILITY BOOKING
                // ==========================================
                var bookingIdStr = ReadString(dataObject, "metadata", "BookingId");
                if (int.TryParse(bookingIdStr, out int bookingId))
                {
                    // Notice we are Including the Facility so we have its Name for the email
                    var booking = await _context.FacilityBookings
                        .Include(b => b.AppUser)
                        .Include(b => b.Facility) 
                        .FirstOrDefaultAsync(b => b.Id == bookingId);

                    if (booking != null)
                    {
                        booking.PaymentStatus = BookingPaymentStatus.Paid;
                        booking.Status = BookingStatus.Confirmed;
                        booking.StripePaymentIntentId = paymentIntentId;
                        booking.StripeSessionId = sessionId;

                        // Generate a secure 8-character alphanumeric Pass Code for the guard validation
                        booking.PassCode = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

                        AddAuditLog("StripeFacilityBookingVerified", booking.Id, $"Facility booking {booking.Id} paid via Stripe.");
                        
                        await _context.SaveChangesAsync();

                        // --- TRIGGER THE QR EMAIL HERE ---
                        var recipientEmail = booking.GuestEmail ?? booking.AppUser?.Email;
                        if (!string.IsNullOrEmpty(recipientEmail))
                        {
                            try
                            {
                                await _emailService.SendFacilityPassAsync(recipientEmail, booking, booking.PassCode);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Failed to send QR pass email for booking {booking.Id}");
                            }
                        }
                    }
                }
                return Ok(new { processed = true, type = "FacilityBooking" });
            }
            else
            {
                // ==========================================
                // EXISTING LOGIC: RENT / DEPOSIT PAYMENT
                // ==========================================
                var payment = await FindPaymentAsync(dataObject, sessionId, paymentIntentId);

                if (payment is null)
                {
                    return NotFound("Matching local payment was not found.");
                }

                payment.StripeSessionId ??= sessionId;
                payment.StripePaymentIntentId ??= paymentIntentId;
                payment.StripeEventId = eventId ?? payment.StripeEventId;
                payment.ReferenceNo = paymentIntentId ?? sessionId ?? payment.ReferenceNo;
                payment.PaymentDate ??= DateTime.UtcNow;

                var paymentStatus = ReadString(dataObject, "payment_status");
                payment.Status = string.Equals(paymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                    ? PaymentStatus.Verified
                    : PaymentStatus.Submitted;

                var receiptUrl = await ResolveReceiptUrlAsync(paymentIntentId, dataObject);
                if (!string.IsNullOrWhiteSpace(receiptUrl))
                {
                    payment.StripeReceiptUrl = receiptUrl;
                }

                payment.LandlordRemarks = payment.Status == PaymentStatus.Verified
                    ? "Confirmed by Stripe through Amazon EventBridge."
                    : "Stripe Checkout completed; awaiting final Stripe payment confirmation.";
                payment.UpdatedAt = DateTime.UtcNow;

                AddAuditLog(
                    payment.Status == PaymentStatus.Verified ? "StripePaymentVerified" : "StripePaymentSubmitted",
                    payment.PaymentId,
                    $"Processed {ReadString(stripeEvent, "type")} from Amazon EventBridge. Session={sessionId}, PaymentIntent={paymentIntentId}.");

                await _context.SaveChangesAsync();
                return Ok(new { processed = true, payment.PaymentId, payment.Status });
            }
        }

        private async Task<IActionResult> HandlePaymentIntentSucceededAsync(JsonElement stripeEvent, string? eventId)
        {
            var dataObject = GetDataObject(stripeEvent);
            var paymentIntentId = ReadString(dataObject, "id");
            var payment = await FindPaymentAsync(dataObject, null, paymentIntentId);

            if (payment is null)
            {
                return NotFound("Matching local payment was not found.");
            }

            payment.StripePaymentIntentId ??= paymentIntentId;
            payment.StripeEventId = eventId ?? payment.StripeEventId;
            payment.ReferenceNo = paymentIntentId ?? payment.ReferenceNo;
            payment.PaymentDate ??= DateTime.UtcNow;
            payment.Status = PaymentStatus.Verified;

            var receiptUrl = await ResolveReceiptUrlAsync(paymentIntentId, dataObject);
            if (!string.IsNullOrWhiteSpace(receiptUrl))
            {
                payment.StripeReceiptUrl = receiptUrl;
            }

            payment.LandlordRemarks = "Payment intent succeeded through Stripe and was delivered by Amazon EventBridge.";
            payment.UpdatedAt = DateTime.UtcNow;

            AddAuditLog(
                "StripePaymentIntentSucceeded",
                payment.PaymentId,
                $"Processed payment_intent.succeeded from Amazon EventBridge. PaymentIntent={paymentIntentId}.");

            await _context.SaveChangesAsync();
            return Ok(new { processed = true, payment.PaymentId, payment.Status });
        }

        private async Task<IActionResult> HandlePaymentIntentFailedAsync(JsonElement stripeEvent, string? eventId)
        {
            var dataObject = GetDataObject(stripeEvent);
            var paymentIntentId = ReadString(dataObject, "id");
            var payment = await FindPaymentAsync(dataObject, null, paymentIntentId);

            if (payment is null)
            {
                return NotFound("Matching local payment was not found.");
            }

            var errorMessage = ReadString(dataObject, "last_payment_error", "message");
            await RejectPaymentAsync(
                payment,
                eventId,
                paymentIntentId,
                string.IsNullOrWhiteSpace(errorMessage)
                    ? "Stripe payment intent failed."
                    : $"Stripe payment intent failed: {errorMessage}");

            return Ok(new { processed = true, payment.PaymentId, payment.Status });
        }

        private async Task<IActionResult> HandleCheckoutSessionRejectedAsync(JsonElement stripeEvent, string? eventId, string message)
        {
            var dataObject = GetDataObject(stripeEvent);
            var sessionId = ReadString(dataObject, "id");
            var paymentIntentId = ReadExpandableString(dataObject, "payment_intent");
            var payment = await FindPaymentAsync(dataObject, sessionId, paymentIntentId);

            if (payment is null)
            {
                return NotFound("Matching local payment was not found.");
            }

            await RejectPaymentAsync(payment, eventId, paymentIntentId, message);
            return Ok(new { processed = true, payment.PaymentId, payment.Status });
        }

        private async Task RejectPaymentAsync(Payment payment, string? eventId, string? paymentIntentId, string message)
        {
            payment.StripePaymentIntentId ??= paymentIntentId;
            payment.StripeEventId = eventId ?? payment.StripeEventId;
            payment.ReferenceNo = paymentIntentId ?? payment.ReferenceNo;
            payment.Status = PaymentStatus.Rejected;
            payment.LandlordRemarks = message;
            payment.UpdatedAt = DateTime.UtcNow;

            AddAuditLog("StripePaymentRejected", payment.PaymentId, message);
            await _context.SaveChangesAsync();
        }

        private async Task<Payment?> FindPaymentAsync(JsonElement dataObject, string? sessionId, string? paymentIntentId)
        {
            var paymentId = ReadIntFromMetadata(dataObject, "paymentId");

            if (paymentId.HasValue)
            {
                var payment = await _context.Payments.FindAsync(paymentId.Value);
                if (payment is not null)
                {
                    return payment;
                }
            }

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var payment = await _context.Payments.FirstOrDefaultAsync(p => p.StripeSessionId == sessionId);
                if (payment is not null)
                {
                    return payment;
                }
            }

            if (!string.IsNullOrWhiteSpace(paymentIntentId))
            {
                return await _context.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId);
            }

            return null;
        }

        private async Task<string?> ResolveReceiptUrlAsync(string? paymentIntentId, JsonElement dataObject)
        {
            var receiptUrl = ReadString(dataObject, "latest_charge", "receipt_url")
                ?? ReadString(dataObject, "charges", "data", "0", "receipt_url");

            if (!string.IsNullOrWhiteSpace(receiptUrl) || string.IsNullOrWhiteSpace(paymentIntentId))
            {
                return receiptUrl;
            }

            var stripeSecretKey = _configuration["Stripe:SecretKey"];
            if (string.IsNullOrWhiteSpace(stripeSecretKey) ||
                stripeSecretKey.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            StripeConfiguration.ApiKey = stripeSecretKey;

            var service = new PaymentIntentService();
            var paymentIntent = await service.GetAsync(paymentIntentId, new PaymentIntentGetOptions
            {
                Expand = new List<string> { "latest_charge" }
            });

            return paymentIntent.LatestCharge?.ReceiptUrl;
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

            var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);
            var providedBytes = Encoding.UTF8.GetBytes(providedSecret.ToString());

            return expectedBytes.Length == providedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
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

            var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
            var providedBytes = Encoding.UTF8.GetBytes(providedKey.ToString());

            return expectedBytes.Length == providedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }

        private async Task<Payment?> FindPaymentFromConfirmationAsync(StripePaymentConfirmedRequest request)
        {
            if (request.PaymentId.HasValue)
            {
                var payment = await _context.Payments.FindAsync(request.PaymentId.Value);
                if (payment is not null)
                {
                    return payment;
                }
            }

            if (!string.IsNullOrWhiteSpace(request.StripeSessionId))
            {
                var payment = await _context.Payments.FirstOrDefaultAsync(p => p.StripeSessionId == request.StripeSessionId);
                if (payment is not null)
                {
                    return payment;
                }
            }

            if (!string.IsNullOrWhiteSpace(request.StripePaymentIntentId))
            {
                return await _context.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == request.StripePaymentIntentId);
            }

            return null;
        }

        private void AddAuditLog(string action, int paymentId, string details)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                Action = action,
                ActorEmail = "Amazon EventBridge",
                TargetType = "Payment",
                TargetId = paymentId,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });
        }

        private static JsonElement GetStripeEvent(JsonElement payload)
        {
            return payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("detail", out var detail) &&
                detail.ValueKind == JsonValueKind.Object
                    ? detail
                    : payload;
        }

        private static JsonElement GetDataObject(JsonElement stripeEvent)
        {
            if (stripeEvent.ValueKind == JsonValueKind.Object &&
                stripeEvent.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("object", out var dataObject))
            {
                return dataObject;
            }

            return default;
        }

        private static string? ReadExpandableString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Object => ReadString(property, "id"),
                _ => null
            };
        }

        private static int? ReadIntFromMetadata(JsonElement dataObject, string key)
        {
            var value = ReadString(dataObject, "metadata", key);
            return int.TryParse(value, out var parsed) ? parsed : null;
        }

        private static string? ReadString(JsonElement element, params string[] path)
        {
            var current = element;

            foreach (var segment in path)
            {
                if (current.ValueKind == JsonValueKind.Array &&
                    int.TryParse(segment, out var index) &&
                    index >= 0 &&
                    index < current.GetArrayLength())
                {
                    current = current[index];
                    continue;
                }

                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(segment, out current))
                {
                    return null;
                }
            }

            return current.ValueKind == JsonValueKind.String
                ? current.GetString()
                : current.ValueKind == JsonValueKind.Number
                    ? current.GetRawText()
                    : null;
        }
    }

    public class StripePaymentConfirmedRequest
    {
        public int? PaymentId { get; set; }
        public string? StripeSessionId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public string? StripeEventId { get; set; }
        public string? StripeReceiptUrl { get; set; }
        public DateTime? PaidAt { get; set; }
    }
}
