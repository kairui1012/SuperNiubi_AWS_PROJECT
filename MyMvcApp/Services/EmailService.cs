using Amazon;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;

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
            // Pulling region from your config, default to ap-southeast-1
            var region = RegionEndpoint.GetBySystemName(_config["AWS:Region"] ?? "ap-southeast-1"); 

            // The client will automatically pick up credentials from the EC2 IAM role
            using var client = new AmazonSimpleEmailServiceClient(region);

            var loginUrl = "http://localhost:5051/"; 
            var subject = "Your Account Has Been Approved!";
            var htmlBody = $"<h3>Hello,</h3><p>Your account has been approved by the system administrator.</p><br><a href='{loginUrl}' style='display:inline-block; padding:12px 24px; background-color:#D9C5B2; color:#14110F; text-decoration:none; border-radius:50px; font-weight:bold;'>Login to Your Account</a>";
            var textBody = $"Hello,\r\n\r\nYour account has been approved by the system administrator.\r\n\r\nLogin to Your Account here: {loginUrl}";

            var sendRequest = new SendEmailRequest
            {
                Source = senderEmail,
                Destination = new Destination
                {
                    ToAddresses = new List<string> { toEmail }
                },
                Message = new Message
                {
                    Subject = new Content(subject),
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

            try
            {
                await client.SendEmailAsync(sendRequest);
            }
            catch (Exception ex)
            {
                // Handle or log the exception appropriately 
                Console.WriteLine($"Failed to send email via SES: {ex.Message}");
                throw;
            }
        }
    }
}