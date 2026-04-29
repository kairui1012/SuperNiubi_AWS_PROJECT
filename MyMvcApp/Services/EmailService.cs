using Amazon;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon.S3;
using Amazon.S3.Model;
using MyMvcApp.Models;
using QRCoder;
using System.Net;
using System.IO;
using Microsoft.Extensions.Logging;

namespace MyMvcApp.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger) 
        { 
            _config = config; 
            _logger = logger;
        }

        public async Task SendApprovalEmailAsync(string toEmail)
        {
            var senderEmail = _config["AWS:SesSenderEmail"];
            var region = RegionEndpoint.GetBySystemName(_config["AWS:Region"] ?? "ap-southeast-1"); 
            using var client = new AmazonSimpleEmailServiceClient(region);

            var loginUrl = "https://propease.dev/Account/Login"; 
            var subject = "Your Account Has Been Approved!";
            var htmlBody = $"<h3>Hello,</h3><p>Your account has been approved by the system administrator.</p><br><a href='{loginUrl}' style='display:inline-block; padding:12px 24px; background-color:#D9C5B2; color:#14110F; text-decoration:none; border-radius:50px; font-weight:bold;'>Login to Your Account</a>";
            var textBody = $"Hello,\r\n\r\nYour account has been approved by the system administrator.\r\n\r\nLogin to Your Account here: {loginUrl}";

            var sendRequest = CreateSendEmailRequest(senderEmail, toEmail, subject, htmlBody, textBody);
            await client.SendEmailAsync(sendRequest);
        }

        // --- UPDATED METHOD: S3 QR CODE HOSTING ---
        // --- UPDATED METHOD: S3 QR CODE WITH BUTTON FALLBACK ---
        public async Task SendPropertyAccessPassAsync(string toEmail, PropertyBooking booking, string passCode)
        {
            var senderEmail = _config["AWS:SesSenderEmail"];
            var region = Amazon.RegionEndpoint.GetBySystemName(_config["AWS:Region"] ?? "ap-southeast-1"); 
            using var sesClient = new Amazon.SimpleEmail.AmazonSimpleEmailServiceClient(region);

            // 1. Generate the verification URL for the guard
            var verificationUrl = $"https://propease.dev/PropertyGuard/Verify?code={passCode}";

            // 2. Generate the QR Code bytes locally
            byte[] qrCodeBytes;
            using (QRCoder.QRCodeGenerator qrGenerator = new QRCoder.QRCodeGenerator())
            {
                QRCoder.QRCodeData qrCodeData = qrGenerator.CreateQrCode(verificationUrl, QRCoder.QRCodeGenerator.ECCLevel.Q);
                using (QRCoder.PngByteQRCode qrCode = new QRCoder.PngByteQRCode(qrCodeData))
                {
                    qrCodeBytes = qrCode.GetGraphic(20);
                }
            }

            // 3. Upload to AWS S3 and get a Presigned URL
            string qrImageUrl = "";
            var bucketName = _config["AWS:BucketName"]; 

            if (!string.IsNullOrEmpty(bucketName))
            {
                using var s3Client = new Amazon.S3.AmazonS3Client(region);
                var fileName = $"qrcodes/booking-{booking.Id}-{passCode}.png";

                using (var stream = new MemoryStream(qrCodeBytes))
                {
                    var putRequest = new Amazon.S3.Model.PutObjectRequest
                    {
                        BucketName = bucketName,
                        Key = fileName,
                        InputStream = stream,
                        ContentType = "image/png"
                    };
                    await s3Client.PutObjectAsync(putRequest);
                }

                var urlRequest = new Amazon.S3.Model.GetPreSignedUrlRequest
                {
                    BucketName = bucketName,
                    Key = fileName,
                    Expires = DateTime.UtcNow.AddDays(30)
                };
                qrImageUrl = s3Client.GetPreSignedURL(urlRequest);
                _logger.LogInformation($"[S3 SUCCESS] QR uploaded. Link generated.");
            }

            var subject = $"Stay Confirmed: {booking.Property.PropertyName}";
            
            // 4. Create the Bulletproof HTML Body
            var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: auto; padding: 20px; border: 1px solid #ddd; border-radius: 10px;'>
                    
                    <div style='text-align: center; padding-bottom: 20px;'>
                        <h2 style='color: #28a745; margin-bottom: 5px;'>Stay Confirmed!</h2>
                        <p style='color: #6c757d; margin-top: 0;'>Your payment was successful.</p>
                    </div>
                    
                    <p>Hello {booking.GuestName},</p>
                    <p>Your dates are locked in. Please present your Digital Access Pass to the security guard upon arrival to check in.</p>
                    
                    <div style='background-color: #f8f9fa; padding: 20px; border-radius: 8px; margin: 25px 0; text-align: center; border: 1px solid #e9ecef;'>
                        <h3 style='margin-top: 0; color: #333;'>{booking.Property.PropertyName}</h3>
                        <p style='margin: 5px 0;'><strong>Check-In:</strong> {booking.CheckInDate:dd MMM yyyy} at 3:00 PM</p>
                        <p style='margin: 5px 0;'><strong>Check-Out:</strong> {booking.CheckOutDate:dd MMM yyyy} at 11:00 AM</p>
                        
                        <div style='margin-top: 20px; padding-top: 20px; border-top: 2px dashed #dee2e6;'>
                            <p style='font-size: 14px; color: #6c757d; margin-bottom: 5px; text-transform: uppercase;'>Your Pass Code</p>
                            <div style='font-size: 32px; font-weight: bold; letter-spacing: 4px; color: #000;'>{passCode}</div>
                        </div>
                    </div>

                    <div style='text-align: center; margin-top: 35px; padding: 20px; background-color: #fff; border-radius: 8px;'>
                        
                        <a href='{qrImageUrl}' target='_blank' style='display: inline-block; margin-top: 10px; padding: 14px 28px; background-color: #0d6efd; color: #ffffff; text-decoration: none; border-radius: 6px; font-weight: bold; font-size: 16px;'>
                            View Digital Pass
                        </a>
                    </div>
                    
                    <div style='text-align: center; margin-top: 30px; border-top: 1px solid #eee; padding-top: 20px;'>
                        <p style='font-size: 12px; color: #999;'>Address: {booking.Property.AddressLine1}, {booking.Property.City}</p>
                    </div>
                </div>";

            var textBody = $"Stay Confirmed!\r\nProperty: {booking.Property.PropertyName}\r\nCheck-in: {booking.CheckInDate:dd MMM yyyy}\r\nCheck-out: {booking.CheckOutDate:dd MMM yyyy}\r\nPass Code: {passCode}\r\nView your QR Pass here: {qrImageUrl}";

            var sendRequest = CreateSendEmailRequest(senderEmail, toEmail, subject, htmlBody, textBody);
            await sesClient.SendEmailAsync(sendRequest);
        }

        private SendEmailRequest CreateSendEmailRequest(string sender, string to, string subject, string html, string text)
        {
            return new SendEmailRequest
            {
                Source = sender,
                Destination = new Destination { ToAddresses = new List<string> { to } },
                Message = new Message
                {
                    Subject = new Content(subject),
                    Body = new Body
                    {
                        Html = new Content { Charset = "UTF-8", Data = html },
                        Text = new Content { Charset = "UTF-8", Data = text }
                    }
                }
            };
        }

        public async Task SendMaintenanceStatusChangedEmailAsync(MaintenanceRequest request, string? landlordEmail)
        {
            var tenantEmail = request.Tenant?.User?.Email;

            if (string.IsNullOrWhiteSpace(tenantEmail))
            {
                throw new InvalidOperationException("Tenant email is missing.");
            }

            var propertyName = request.Property?.PropertyName ?? "your property";
            var loginUrl = _config["App:BaseUrl"] ?? "http://localhost:5051/";
            var subject = $"Maintenance Request #{request.RequestId} Status Updated";

            var encodedTitle = WebUtility.HtmlEncode(request.Title);
            var encodedPropertyName = WebUtility.HtmlEncode(propertyName);
            var encodedRemarks = WebUtility.HtmlEncode(request.LandlordRemarks ?? "-");
            var encodedVendor = WebUtility.HtmlEncode(request.AssignedVendor ?? "-");
            var encodedLandlordEmail = WebUtility.HtmlEncode(landlordEmail ?? "-");
            var estimatedCost = request.EstimatedRepairCost.HasValue
                ? $"RM {request.EstimatedRepairCost.Value:N2}"
                : "-";

            var htmlBody = $@"
                <h3>Maintenance request updated</h3>
                <p>Your maintenance request status has been updated.</p>
                <table cellpadding='8' cellspacing='0' style='border-collapse:collapse;'>
                    <tr><td><strong>Request</strong></td><td>#{request.RequestId} - {encodedTitle}</td></tr>
                    <tr><td><strong>Property</strong></td><td>{encodedPropertyName}</td></tr>
                    <tr><td><strong>Status</strong></td><td>{request.Status}</td></tr>
                    <tr><td><strong>Priority</strong></td><td>{request.Priority}</td></tr>
                    <tr><td><strong>Assigned vendor</strong></td><td>{encodedVendor}</td></tr>
                    <tr><td><strong>Estimated repair cost</strong></td><td>{estimatedCost}</td></tr>
                    <tr><td><strong>Landlord remarks</strong></td><td>{encodedRemarks}</td></tr>
                    <tr><td><strong>Updated by</strong></td><td>{encodedLandlordEmail}</td></tr>
                </table>
                <p><a href='{loginUrl}' style='display:inline-block; padding:12px 20px; background-color:#14110F; color:#ffffff; text-decoration:none; border-radius:6px;'>View maintenance request</a></p>";

            var textBody =
                $"Maintenance request updated\r\n\r\n" +
                $"Request: #{request.RequestId} - {request.Title}\r\n" +
                $"Property: {propertyName}\r\n" +
                $"Status: {request.Status}\r\n" +
                $"Priority: {request.Priority}\r\n" +
                $"Assigned vendor: {request.AssignedVendor ?? "-"}\r\n" +
                $"Estimated repair cost: {estimatedCost}\r\n" +
                $"Landlord remarks: {request.LandlordRemarks ?? "-"}\r\n" +
                $"Updated by: {landlordEmail ?? "-"}\r\n\r\n" +
                $"View maintenance request: {loginUrl}";

            await SendEmailAsync(tenantEmail, subject, htmlBody, textBody);
        }

        private async Task SendEmailAsync(string toEmail, string subject, string htmlBody, string textBody)
        {
            var senderEmail = _config["AWS:SesSenderEmail"];

            if (string.IsNullOrWhiteSpace(senderEmail))
            {
                throw new InvalidOperationException("AWS SES sender email is not configured.");
            }

            var region = RegionEndpoint.GetBySystemName(_config["AWS:Region"] ?? "ap-southeast-1");
            using var client = new AmazonSimpleEmailServiceClient(region);

            var sendRequest = new SendEmailRequest
            {
                Source = senderEmail,
                Destination = new Destination
                {
                    ToAddresses = new List<string> { toEmail }
                },
                Message = new Message
                {
                    Subject = new Content
                    {
                        Charset = "UTF-8",
                        Data = subject
                    },
                    Body = new Body
                    {
                        Html = new Content
                        {
                            Charset = "UTF-8",
                            Data = htmlBody
                        },
                        Text = new Content
                        {
                            Charset = "UTF-8",
                            Data = textBody
                        }
                    }
                }
            };

            await client.SendEmailAsync(sendRequest);
        }
    }
}