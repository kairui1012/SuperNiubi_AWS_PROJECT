using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyMvcApp.Serverless
{
    public class ReportExportFunction
    {
        private static readonly PaymentStatus[] ClosedPaymentStatuses =
        {
            PaymentStatus.Verified,
            PaymentStatus.Rejected,
            PaymentStatus.Failed,
            PaymentStatus.Cancelled,
            PaymentStatus.Refunded
        };

        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public ReportExportFunction()
        {
            _configuration = BuildConfiguration();

            var services = new ServiceCollection();
            services.AddSingleton(_configuration);
            services.AddLogging(logging => logging.AddConsole());
            services.AddDbContext<StripeWorkerDbContext>(options =>
                options.UseNpgsql(_configuration.GetConnectionString("DefaultConnection")));

            _serviceProvider = services.BuildServiceProvider();
        }

        public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StripeWorkerDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ReportExportFunction>>();

            foreach (var record in sqsEvent.Records)
            {
                var message = JsonSerializer.Deserialize<ReportExportMessage>(record.Body);
                if (message is null)
                {
                    logger.LogWarning("Skipping report export message with invalid body.");
                    continue;
                }

                await ProcessJobAsync(db, message.JobId, logger);
            }
        }

        private async Task ProcessJobAsync(StripeWorkerDbContext db, int jobId, ILogger logger)
        {
            var job = await db.ReportExportJobs.FindAsync(jobId);
            if (job is null)
            {
                logger.LogWarning("Report export job {JobId} was not found.", jobId);
                return;
            }

            try
            {
                job.Status = ReportExportStatus.Processing;
                job.StartedAt = DateTime.UtcNow;
                job.ErrorMessage = null;
                await db.SaveChangesAsync();

                var filter = string.IsNullOrWhiteSpace(job.FilterJson)
                    ? new ReportExportFilter()
                    : JsonSerializer.Deserialize<ReportExportFilter>(job.FilterJson) ?? new ReportExportFilter();

                var csv = await BuildPaymentCsvAsync(db, filter);
                var bucketName = _configuration["ReportExport:BucketName"] ?? _configuration["AWS:BucketName"];
                if (string.IsNullOrWhiteSpace(bucketName))
                {
                    throw new InvalidOperationException("Report export bucket is not configured.");
                }

                var fileName = $"payment-report-{DateTime.UtcNow:yyyy-MM-dd}-job-{job.ReportExportJobId}.csv";
                var key = $"reports/payment/{DateTime.UtcNow:yyyy/MM}/{fileName}";

                using var s3Client = new AmazonS3Client();
                await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
                await s3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    InputStream = stream,
                    ContentType = "text/csv"
                });

                job.Status = ReportExportStatus.Completed;
                job.S3Bucket = bucketName;
                job.S3Key = key;
                job.FileName = fileName;
                job.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                logger.LogInformation("Completed report export job {JobId}.", job.ReportExportJobId);
            }
            catch (Exception ex)
            {
                job.Status = ReportExportStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                logger.LogError(ex, "Report export job {JobId} failed.", job.ReportExportJobId);
                throw;
            }
        }

        private static async Task<string> BuildPaymentCsvAsync(StripeWorkerDbContext db, ReportExportFilter filter)
        {
            var query = db.Payments.AsNoTracking()
                .Include(p => p.Tenant).ThenInclude(t => t.User)
                .Include(p => p.Property).ThenInclude(p => p.Landlord)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var search = filter.Search.Trim();
                query = query.Where(p =>
                    p.Tenant.User.Email.Contains(search) ||
                    p.Property.PropertyName.Contains(search) ||
                    (p.ReferenceNo != null && p.ReferenceNo.Contains(search)) ||
                    (p.StripeSessionId != null && p.StripeSessionId.Contains(search)) ||
                    (p.StripePaymentIntentId != null && p.StripePaymentIntentId.Contains(search)) ||
                    (p.Property.Landlord != null && p.Property.Landlord.Email.Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "All")
            {
                if (filter.Status == "Overdue")
                {
                    query = query.Where(p =>
                        p.DueDate < DateTime.UtcNow &&
                        !ClosedPaymentStatuses.Contains(p.Status));
                }
                else if (Enum.TryParse<PaymentStatus>(filter.Status, out var parsedStatus))
                {
                    query = query.Where(p => p.Status == parsedStatus);
                }
            }

            if (filter.FromDate.HasValue)
            {
                var from = DateTime.SpecifyKind(filter.FromDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(p => p.DueDate >= from);
            }

            if (filter.ToDate.HasValue)
            {
                var to = DateTime.SpecifyKind(filter.ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
                query = query.Where(p => p.DueDate < to);
            }

            var records = await query
                .OrderByDescending(p => p.DueDate)
                .Select(p => new
                {
                    p.PaymentId,
                    TenantEmail = p.Tenant.User.Email,
                    PropertyName = p.Property.PropertyName,
                    LandlordEmail = p.Property.Landlord != null ? p.Property.Landlord.Email : "",
                    p.Amount,
                    Status = p.Status.ToString(),
                    PaymentPeriod = p.PaymentMonth + " " + p.PaymentYear,
                    p.DueDate,
                    SubmittedDate = p.PaymentDate,
                    VerifiedDate = p.Status == PaymentStatus.Verified ? p.UpdatedAt : (DateTime?)null,
                    PaymentMethod = p.PaymentMethod != null ? p.PaymentMethod.ToString() : "",
                    ReferenceNo = p.ReferenceNo ?? "",
                    StripeSessionId = p.StripeSessionId ?? "",
                    StripePaymentIntentId = p.StripePaymentIntentId ?? "",
                    StripeReceiptUrl = p.StripeReceiptUrl ?? "",
                    StripeRefundId = p.StripeRefundId ?? "",
                    p.RefundAmount,
                    p.RefundDate,
                    RefundReason = p.RefundReason ?? ""
                })
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Payment ID,Tenant Email,Property Name,Landlord Email,Amount,Status,Payment Period,Due Date,Submitted Date,Verified Date,Payment Method,Reference No,Stripe Session ID,Stripe Payment Intent ID,Stripe Receipt URL,Stripe Refund ID,Refund Amount,Refund Date,Refund Reason");

            foreach (var r in records)
            {
                sb.AppendLine(string.Join(",",
                    r.PaymentId,
                    CsvEscape(r.TenantEmail),
                    CsvEscape(r.PropertyName),
                    CsvEscape(r.LandlordEmail),
                    r.Amount.ToString("F2"),
                    r.Status,
                    CsvEscape(r.PaymentPeriod),
                    r.DueDate.ToString("yyyy-MM-dd"),
                    r.SubmittedDate?.ToString("yyyy-MM-dd") ?? "",
                    r.VerifiedDate?.ToString("yyyy-MM-dd") ?? "",
                    r.PaymentMethod,
                    CsvEscape(r.ReferenceNo),
                    CsvEscape(r.StripeSessionId),
                    CsvEscape(r.StripePaymentIntentId),
                    CsvEscape(r.StripeReceiptUrl),
                    CsvEscape(r.StripeRefundId),
                    r.RefundAmount?.ToString("F2") ?? "",
                    r.RefundDate?.ToString("yyyy-MM-dd") ?? "",
                    CsvEscape(r.RefundReason)));
            }

            return sb.ToString();
        }

        private static string CsvEscape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            var escaped = value.Replace("\"", "\"\"");
            return escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r')
                ? $"\"{escaped}\""
                : escaped;
        }

        private static IConfiguration BuildConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
        }

        private sealed record ReportExportMessage(int JobId);

        private sealed record ReportExportFilter(
            string? Search = null,
            string? Status = null,
            DateTime? FromDate = null,
            DateTime? ToDate = null);
    }
}
