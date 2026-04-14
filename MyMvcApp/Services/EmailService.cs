using System.Net;
using System.Net.Mail;

namespace MyMvcApp.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        public EmailService(IConfiguration config) { _config = config; }

        // Removed the nickname parameter
        public async Task SendApprovalEmailAsync(string toEmail)
        {
            var smtpServer = _config["EmailSettings:SmtpServer"];
            var port = int.Parse(_config["EmailSettings:Port"]);
            var senderEmail = _config["EmailSettings:SenderEmail"];
            var password = _config["EmailSettings:Password"];

            var client = new SmtpClient(smtpServer, port)
            {
                Credentials = new NetworkCredential(senderEmail, password),
                EnableSsl = true
            };

            // Fixed: Changed from https to http
            var loginUrl = "http://localhost:5051/"; 
            
            var mailMessage = new MailMessage
            {
                From = new MailAddress(senderEmail, "Super Niubi Admin"),
                Subject = "Your Account Has Been Approved!",
                // Fixed: Added display:inline-block and increased border-radius to 50px for a perfectly round pill shape
                Body = $"<h3>Hello,</h3><p>Your account has been approved by the system administrator.</p><br><a href='{loginUrl}' style='display:inline-block; padding:12px 24px; background-color:#D9C5B2; color:#14110F; text-decoration:none; border-radius:50px; font-weight:bold;'>Login to Your Account</a>",
                IsBodyHtml = true,
            };
            mailMessage.To.Add(toEmail);

            await client.SendMailAsync(mailMessage);
        }
    }
}