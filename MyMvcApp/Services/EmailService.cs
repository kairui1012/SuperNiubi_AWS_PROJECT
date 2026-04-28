using Amazon;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using MyMvcApp.Models; // To access FacilityBooking
using QRCoder; // For generating the QR code

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
    }
}