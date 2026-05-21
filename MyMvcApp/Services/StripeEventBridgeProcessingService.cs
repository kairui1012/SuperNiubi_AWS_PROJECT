using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using Stripe;

namespace MyMvcApp.Services
{
    public class StripeEventBridgeProcessingService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StripeEventBridgeProcessingService> _logger;
        private readonly EmailService _emailService;

        public StripeEventBridgeProcessingService(
            AppDbContext context,
            IConfiguration configuration,
            ILogger<StripeEventBridgeProcessingService> logger,
            EmailService emailService)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _emailService = emailService;
        }

        public async Task<StripeEventProcessResult> ProcessEventBridgeEventAsync(JsonElement payload)
        {
            var summary = ReadEventSummary(payload);

            if (string.IsNullOrWhiteSpace(summary.EventType))
            {
                return StripeEventProcessResult.BadRequest("Stripe event type is missing.", summary);
            }

            var stripeEvent = GetStripeEvent(payload);

            return summary.EventType switch
            {
                "checkout.session.completed" => await HandleCheckoutSessionCompletedAsync(stripeEvent, summary.EventId, summary),
                "checkout.session.async_payment_failed" => await HandleCheckoutSessionRejectedAsync(stripeEvent, summary.EventId, PaymentStatus.Failed, "Stripe async payment failed.", summary),
                "checkout.session.expired" => await HandleCheckoutSessionRejectedAsync(stripeEvent, summary.EventId, PaymentStatus.Cancelled, "Stripe Checkout session expired.", summary),
                "payment_intent.succeeded" => await HandlePaymentIntentSucceededAsync(stripeEvent, summary.EventId, summary),
                "payment_intent.payment_failed" => await HandlePaymentIntentFailedAsync(stripeEvent, summary.EventId, summary),
                "charge.refunded" => await HandleChargeRefundedAsync(stripeEvent, summary.EventId, summary),
                "refund.created" => await HandleRefundCreatedAsync(stripeEvent, summary.EventId, summary),
                "refund.updated" => await HandleRefundCreatedAsync(stripeEvent, summary.EventId, summary),
                _ => StripeEventProcessResult.Ok(new { ignored = true, eventType = summary.EventType }, summary)
            };
        }

        public async Task<StripeEventProcessResult> ConfirmPaymentAsync(StripePaymentConfirmedRequest request)
        {
            var payment = await FindPaymentFromConfirmationAsync(request);
            if (payment is null)
            {
                return StripeEventProcessResult.NotFound("Matching local payment was not found.", StripeEventSummary.Empty);
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

            return StripeEventProcessResult.Ok(new
            {
                message = "Payment verified",
                payment.PaymentId,
                payment.Status
            }, StripeEventSummary.Empty);
        }

        public static StripeEventSummary ReadEventSummary(JsonElement payload)
        {
            var stripeEvent = GetStripeEvent(payload);
            return new StripeEventSummary(
                ReadString(stripeEvent, "id") ?? ReadString(payload, "id"),
                ReadString(stripeEvent, "type") ?? ReadString(payload, "detail-type"));
        }

        private async Task<StripeEventProcessResult> HandleCheckoutSessionCompletedAsync(
            JsonElement stripeEvent,
            string? eventId,
            StripeEventSummary summary)
        {
            var dataObject = GetDataObject(stripeEvent);
            if (dataObject.ValueKind != JsonValueKind.Object)
            {
                return StripeEventProcessResult.BadRequest("Stripe checkout session object is missing.", summary);
            }

            var sessionId = ReadString(dataObject, "id");
            var paymentIntentId = ReadExpandableString(dataObject, "payment_intent");

            var transactionType = ReadString(dataObject, "metadata", "TransactionType");
            if (transactionType == "PropertyBooking")
            {
                var bookingIdStr = ReadString(dataObject, "metadata", "BookingId");
                if (int.TryParse(bookingIdStr, out var bookingId))
                {
                    var booking = await _context.PropertyBookings
                        .Include(b => b.Property)
                        .FirstOrDefaultAsync(b => b.Id == bookingId);

                    if (booking != null)
                    {
                        booking.PaymentStatus = BookingPaymentStatus.Paid;
                        booking.Status = BookingStatus.Confirmed;
                        booking.StripePaymentIntentId = paymentIntentId;
                        booking.StripeSessionId = sessionId;
                        booking.PassCode = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();

                        await _context.SaveChangesAsync();

                        if (!string.IsNullOrEmpty(booking.GuestEmail))
                        {
                            try
                            {
                                await _emailService.SendPropertyAccessPassAsync(booking.GuestEmail, booking, booking.PassCode);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to send QR pass email for property booking {BookingId}.", booking.Id);
                            }
                        }
                    }
                }

                return StripeEventProcessResult.Ok(new { processed = true, type = "PropertyBooking" }, summary);
            }

            var payment = await FindPaymentAsync(dataObject, sessionId, paymentIntentId);
            if (payment is null)
            {
                return StripeEventProcessResult.NotFound("Matching local payment was not found.", summary);
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
            return StripeEventProcessResult.Ok(new { processed = true, payment.PaymentId, payment.Status }, summary);
        }

        private async Task<StripeEventProcessResult> HandlePaymentIntentSucceededAsync(
            JsonElement stripeEvent,
            string? eventId,
            StripeEventSummary summary)
        {
            var dataObject = GetDataObject(stripeEvent);
            var paymentIntentId = ReadString(dataObject, "id");
            var payment = await FindPaymentAsync(dataObject, null, paymentIntentId);

            if (payment is null)
            {
                return StripeEventProcessResult.NotFound("Matching local payment was not found.", summary);
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
            return StripeEventProcessResult.Ok(new { processed = true, payment.PaymentId, payment.Status }, summary);
        }

        private async Task<StripeEventProcessResult> HandlePaymentIntentFailedAsync(
            JsonElement stripeEvent,
            string? eventId,
            StripeEventSummary summary)
        {
            var dataObject = GetDataObject(stripeEvent);
            var paymentIntentId = ReadString(dataObject, "id");
            var payment = await FindPaymentAsync(dataObject, null, paymentIntentId);

            if (payment is null)
            {
                return StripeEventProcessResult.NotFound("Matching local payment was not found.", summary);
            }

            var errorMessage = ReadString(dataObject, "last_payment_error", "message");
            await RejectPaymentAsync(
                payment,
                eventId,
                paymentIntentId,
                PaymentStatus.Failed,
                string.IsNullOrWhiteSpace(errorMessage)
                    ? "Stripe payment intent failed."
                    : $"Stripe payment intent failed: {errorMessage}");

            return StripeEventProcessResult.Ok(new { processed = true, payment.PaymentId, payment.Status }, summary);
        }

        private async Task<StripeEventProcessResult> HandleCheckoutSessionRejectedAsync(
            JsonElement stripeEvent,
            string? eventId,
            PaymentStatus status,
            string message,
            StripeEventSummary summary)
        {
            var dataObject = GetDataObject(stripeEvent);
            var sessionId = ReadString(dataObject, "id");
            var paymentIntentId = ReadExpandableString(dataObject, "payment_intent");
            var payment = await FindPaymentAsync(dataObject, sessionId, paymentIntentId);

            if (payment is null)
            {
                return StripeEventProcessResult.NotFound("Matching local payment was not found.", summary);
            }

            await RejectPaymentAsync(payment, eventId, paymentIntentId, status, message);
            return StripeEventProcessResult.Ok(new { processed = true, payment.PaymentId, payment.Status }, summary);
        }

        private async Task<StripeEventProcessResult> HandleChargeRefundedAsync(
            JsonElement stripeEvent,
            string? eventId,
            StripeEventSummary summary)
        {
            var dataObject = GetDataObject(stripeEvent);
            var paymentIntentId = ReadExpandableString(dataObject, "payment_intent") ?? ReadString(dataObject, "payment_intent");
            var payment = await FindPaymentAsync(dataObject, null, paymentIntentId);

            if (payment is null)
            {
                return StripeEventProcessResult.NotFound("Matching local payment was not found.", summary);
            }

            var refundId = ReadString(dataObject, "refunds", "data", "0", "id");
            var refundReason = ReadString(dataObject, "refunds", "data", "0", "reason");
            var refundedAmount = ReadDecimalFromMinorUnit(dataObject, "amount_refunded");

            await MarkPaymentRefundedAsync(payment, eventId, refundId, refundedAmount, refundReason);
            return StripeEventProcessResult.Ok(new { processed = true, payment.PaymentId, payment.Status }, summary);
        }

        private async Task<StripeEventProcessResult> HandleRefundCreatedAsync(
            JsonElement stripeEvent,
            string? eventId,
            StripeEventSummary summary)
        {
            var dataObject = GetDataObject(stripeEvent);
            var paymentIntentId = ReadExpandableString(dataObject, "payment_intent") ?? ReadString(dataObject, "payment_intent");
            var payment = await FindPaymentAsync(dataObject, null, paymentIntentId);

            if (payment is null)
            {
                return StripeEventProcessResult.NotFound("Matching local payment was not found.", summary);
            }

            var refundId = ReadString(dataObject, "id");
            var refundReason = ReadString(dataObject, "reason");
            var refundedAmount = ReadDecimalFromMinorUnit(dataObject, "amount");

            await MarkPaymentRefundedAsync(payment, eventId, refundId, refundedAmount, refundReason);
            return StripeEventProcessResult.Ok(new { processed = true, payment.PaymentId, payment.Status }, summary);
        }

        private async Task RejectPaymentAsync(Payment payment, string? eventId, string? paymentIntentId, PaymentStatus status, string message)
        {
            payment.StripePaymentIntentId ??= paymentIntentId;
            payment.StripeEventId = eventId ?? payment.StripeEventId;
            payment.ReferenceNo = paymentIntentId ?? payment.ReferenceNo;
            payment.Status = status;
            payment.LandlordRemarks = message;
            payment.UpdatedAt = DateTime.UtcNow;

            AddAuditLog(status == PaymentStatus.Cancelled ? "StripePaymentCancelled" : "StripePaymentFailed", payment.PaymentId, message);
            await _context.SaveChangesAsync();
        }

        private async Task MarkPaymentRefundedAsync(Payment payment, string? eventId, string? refundId, decimal? refundAmount, string? refundReason)
        {
            payment.StripeEventId = eventId ?? payment.StripeEventId;
            payment.StripeRefundId = refundId ?? payment.StripeRefundId;
            payment.RefundAmount = refundAmount ?? payment.RefundAmount ?? payment.Amount;
            payment.RefundDate ??= DateTime.UtcNow;
            payment.RefundReason = refundReason ?? payment.RefundReason;
            payment.Status = PaymentStatus.Refunded;
            payment.LandlordRemarks = string.IsNullOrWhiteSpace(refundReason)
                ? "Stripe refund recorded through Amazon EventBridge."
                : $"Stripe refund recorded through Amazon EventBridge. Reason: {refundReason}";
            payment.UpdatedAt = DateTime.UtcNow;

            AddAuditLog("StripePaymentRefunded", payment.PaymentId, $"Stripe refund recorded. Refund={payment.StripeRefundId}, Amount={payment.RefundAmount:N2}.");
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

        private static decimal? ReadDecimalFromMinorUnit(JsonElement element, params string[] path)
        {
            var value = ReadString(element, path);
            return decimal.TryParse(value, out var parsed) ? parsed / 100m : null;
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

    public sealed record StripeEventSummary(string? EventId, string? EventType)
    {
        public static StripeEventSummary Empty { get; } = new(null, null);
    }

    public sealed record StripeEventProcessResult(
        int StatusCode,
        object? Body,
        string? Message,
        StripeEventSummary Summary)
    {
        public bool IsSuccessStatusCode => StatusCode >= 200 && StatusCode < 300;

        public static StripeEventProcessResult Ok(object body, StripeEventSummary summary)
            => new(200, body, null, summary);

        public static StripeEventProcessResult BadRequest(string message, StripeEventSummary summary)
            => new(400, null, message, summary);

        public static StripeEventProcessResult NotFound(string message, StripeEventSummary summary)
            => new(404, null, message, summary);
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
