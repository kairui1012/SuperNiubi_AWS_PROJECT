using Amazon;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using MyMvcApp.Models;
using System.Net;

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
