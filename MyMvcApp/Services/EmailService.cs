using Amazon;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using MyMvcApp.Models; // To access FacilityBooking and MaintenanceRequest
using QRCoder; // For generating the QR code
using System.Net; // For WebUtility.HtmlEncode

namespace MyMvcApp.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        
        public EmailService(IConfiguration config) 
        { 
            _config = config; 
        }

        public async Task SendApprovalEmailAsync(string toEmail)
        {
            var senderEmail = _config["AWS:SesSenderEmail"];
            var region = RegionEndpoint.GetBySystemName(_config["AWS:Region"] ?? "ap-southeast-1"); 
            using var client = new AmazonSimpleEmailServiceClient(region);

            var loginUrl = "http://localhost:5051/"; 
            var subject = "Your Account Has Been Approved!";
            var htmlBody = $"<h3>Hello,</h3><p>Your account has been approved by the system administrator.</p><br><a href='{loginUrl}' style='display:inline-block; padding:12px 24px; background-color:#D9C5B2; color:#14110F; text-decoration:none; border-radius:50px; font-weight:bold;'>Login to Your Account</a>";
            var textBody = $"Hello,\r\n\r\nYour account has been approved by the system administrator.\r\n\r\nLogin to Your Account here: {loginUrl}";

            var sendRequest = CreateSendEmailRequest(senderEmail, toEmail, subject, htmlBody, textBody);
            await client.SendEmailAsync(sendRequest);
        }

        // --- NEW METHOD FOR FACILITY BOOKING QR EMAIL ---
        public async Task SendFacilityPassAsync(string toEmail, FacilityBooking booking, string passCode)
        {
            var senderEmail = _config["AWS:SesSenderEmail"];
            var region = RegionEndpoint.GetBySystemName(_config["AWS:Region"] ?? "ap-southeast-1"); 
            using var client = new AmazonSimpleEmailServiceClient(region);

            // 1. Generate the verification URL for the guard
            // Adjust "https://propease.dev" if testing locally
            var verificationUrl = $"https://propease.dev/FacilityGuard/Verify?code={passCode}";

            // 2. Generate the QR Code as a Base64 string
            string qrCodeBase64;
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            {
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(verificationUrl, QRCodeGenerator.ECCLevel.Q);
                using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
                {
                    byte[] qrCodeImage = qrCode.GetGraphic(20);
                    qrCodeBase64 = Convert.ToBase64String(qrCodeImage);
                }
            }

            var subject = $"Booking Confirmed: {booking.Facility.Name}";
            
            // 3. Create the HTML Body embedding the Base64 QR code
            var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: auto; padding: 20px; border: 1px solid #ddd; border-radius: 10px;'>
                    <h2 style='color: #28a745;'>Booking Confirmed!</h2>
                    <p>Hello,</p>
                    <p>Your payment was successful and your facility booking is confirmed. Please present the QR code below to the security guard upon arrival.</p>
                    
                    <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                        <h3 style='margin-top: 0;'>{booking.Facility.Name}</h3>
                        <p><strong>Date:</strong> {booking.BookingDate:dd MMM yyyy}</p>
                        <p><strong>Time:</strong> {booking.StartTime:hh\:mm} - {booking.EndTime:hh\:mm}</p>
                        <p><strong>Pass Code:</strong> <span style='font-size: 1.2em; font-weight: bold; letter-spacing: 2px;'>{passCode}</span></p>
                    </div>

                    <div style='text-align: center; margin-top: 30px;'>
                        <p><strong>Scan for Access</strong></p>
                        <img src='data:image/png;base64,{qrCodeBase64}' alt='Access QR Code' style='max-width: 250px;' />
                    </div>
                </div>";

            var textBody = $"Booking Confirmed!\r\nFacility: {booking.Facility.Name}\r\nDate: {booking.BookingDate:dd MMM yyyy}\r\nTime: {booking.StartTime:hh\\:mm} - {booking.EndTime:hh\\:mm}\r\nPass Code: {passCode}\r\nVerification Link: {verificationUrl}";

            var sendRequest = CreateSendEmailRequest(senderEmail, toEmail, subject, htmlBody, textBody);
            await client.SendEmailAsync(sendRequest);
        }

        // Helper method to keep code DRY
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