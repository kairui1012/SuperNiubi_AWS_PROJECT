using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Stripe.Checkout;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using QRCoder;
using Amazon.S3;
using Amazon.S3.Model;
using MyMvcApp.Services;

namespace MyMvcApp.Controllers
{
    [Authorize] // Ensures only logged-in users can reach this page
    public class TenantController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly IAmazonS3 _s3Client;
        private readonly DocumentUploadService _documentUploadService;
        private const long MaxDocumentSizeBytes = 10 * 1024 * 1024;
        private const long MaxMaintenanceImageSizeBytes = 8 * 1024 * 1024;
        private static readonly string[] AllowedDocumentExtensions = [".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx"];
        private static readonly string[] AllowedMaintenanceImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

        public TenantController(
            AppDbContext context,
            IWebHostEnvironment environment,
            IConfiguration configuration,
            IAmazonS3 s3Client,
            DocumentUploadService documentUploadService)
        {
            _context = context;
            _environment = environment;
            _configuration = configuration;
            _s3Client = s3Client;
            _documentUploadService = documentUploadService;
        }

        private string? GetCurrentEmail()
        {
            return User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;
        }

        private static string BuildQrCodeDataUrl(string payload)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(data);
            var bytes = qrCode.GetGraphic(20);

            return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        }

        private static string ExtractPassCode(string? rawPassCode)
        {
            var input = (rawPassCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var payloadMatch = Regex.Match(input, @"(?:^|\|)\s*Code\s*:\s*([^|]+)", RegexOptions.IgnoreCase);
            if (payloadMatch.Success)
            {
                return payloadMatch.Groups[1].Value.Trim().ToUpperInvariant();
            }

            var tokenMatch = Regex.Match(input, @"VIS-[A-Z0-9]+", RegexOptions.IgnoreCase);
            if (tokenMatch.Success)
            {
                return tokenMatch.Value.ToUpperInvariant();
            }

            return input.ToUpperInvariant();
        }

        private static bool HasAllowedExtension(string fileName, string[] allowedExtensions)
        {
            var extension = Path.GetExtension(fileName);
            return !string.IsNullOrWhiteSpace(extension) && allowedExtensions.Contains(extension.ToLowerInvariant());
        }

        private async Task ExpireVisitorPassesAsync(int tenantId)
        {
            var today = DateTime.UtcNow.Date;
            var activePastPasses = await _context.VisitorPasses
                .Where(v => v.TenantId == tenantId && v.Status == VisitorPassStatus.Active && v.VisitDate.Date < today)
                .ToListAsync();

            if (activePastPasses.Count == 0)
            {
                return;
            }

            foreach (var pass in activePastPasses)
            {
                pass.Status = VisitorPassStatus.Expired;
                pass.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        private async Task<List<TenantNotificationItem>> BuildTenantNotificationsAsync(
            Tenant tenant,
            DateTime nextPaymentDue,
            List<MaintenanceRequest> maintenanceRequests,
            List<VisitorPass> visitorPasses)
        {
            var notifications = new List<TenantNotificationItem>();
            var now = DateTime.UtcNow;

            var daysUntilDue = (nextPaymentDue.Date - now.Date).Days;
            if (daysUntilDue <= 7)
            {
                notifications.Add(new TenantNotificationItem
                {
                    Category = "Payment",
                    Title = "Upcoming rent due",
                    Message = $"Your rent is due on {nextPaymentDue.ToLocalTime():dd MMM yyyy}.",
                    CreatedAt = now,
                    ActionText = "Pay now",
                    ActionUrl = Url.Action(nameof(Payments), "Tenant")
                });
            }

            var daysToLeaseEnd = (tenant.LeaseEndDate.Date - now.Date).Days;
            if (daysToLeaseEnd <= 45)
            {
                notifications.Add(new TenantNotificationItem
                {
                    Category = "Lease",
                    Title = "Lease expiry reminder",
                    Message = daysToLeaseEnd >= 0
                        ? $"Your lease ends in {daysToLeaseEnd} day(s) on {tenant.LeaseEndDate.ToLocalTime():dd MMM yyyy}."
                        : $"Your lease ended on {tenant.LeaseEndDate.ToLocalTime():dd MMM yyyy}.",
                    CreatedAt = now,
                    ActionText = "View property",
                    ActionUrl = Url.Action(nameof(MyProperty), "Tenant")
                });
            }

            foreach (var request in maintenanceRequests
                .Where(r => r.Status != MaintenanceStatus.Pending)
                .OrderByDescending(r => r.UpdatedAt)
                .Take(3))
            {
                notifications.Add(new TenantNotificationItem
                {
                    Category = "Maintenance",
                    Title = "Maintenance status update",
                    Message = $"{request.Title} is now {request.Status}.",
                    CreatedAt = request.UpdatedAt,
                    ActionText = "Open maintenance",
                    ActionUrl = Url.Action(nameof(MaintenanceRequest), "Tenant")
                });
            }

            var recentVisitorPass = visitorPasses.OrderByDescending(v => v.CreatedAt).FirstOrDefault();
            if (recentVisitorPass is not null)
            {
                notifications.Add(new TenantNotificationItem
                {
                    Category = "Visitor",
                    Title = "Visitor pass created",
                    Message = $"Pass {recentVisitorPass.PassCode} for {recentVisitorPass.VisitorName} ({recentVisitorPass.VisitDate.ToLocalTime():dd MMM yyyy}) is ready.",
                    CreatedAt = recentVisitorPass.CreatedAt,
                    ActionText = "Open visitors",
                    ActionUrl = Url.Action(nameof(Visitors), "Tenant", new { passId = recentVisitorPass.VisitorPassId })
                });
            }

            var announcementItems = await _context.SystemAnnouncements
                .AsNoTracking()
                .Where(a => a.VisibleTo == "All" || a.VisibleTo == "Tenant")
                .OrderByDescending(a => a.CreatedAt)
                .Take(3)
                .ToListAsync();

            notifications.AddRange(announcementItems.Select(a => new TenantNotificationItem
            {
                Category = "Announcement",
                Title = a.Title,
                Message = a.Body.Length > 120 ? a.Body[..120] + "..." : a.Body,
                CreatedAt = a.CreatedAt,
                ActionText = "Read announcement",
                ActionUrl = Url.Action(nameof(Announcements), "Tenant")
            }));

            return notifications
                .OrderByDescending(n => n.CreatedAt)
                .Take(8)
                .ToList();
        }

        private async Task<TenantVisitorsViewModel> BuildVisitorViewModelAsync(Tenant tenant, CreateVisitorViewModel? newVisitor = null, int? selectedVisitorPassId = null)
        {
            await ExpireVisitorPassesAsync(tenant.TenantId);

            var visitors = await _context.VisitorPasses
                .Where(v => v.TenantId == tenant.TenantId)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();

            var selectedVisitor = selectedVisitorPassId.HasValue
                ? visitors.FirstOrDefault(v => v.VisitorPassId == selectedVisitorPassId.Value)
                : visitors.FirstOrDefault();

            return new TenantVisitorsViewModel
            {
                PropertyName = tenant.Property?.PropertyName ?? "Assigned property",
                Visitors = visitors,
                NewVisitor = newVisitor ?? new CreateVisitorViewModel { VisitDate = DateTime.UtcNow.Date },
                LatestPass = selectedVisitor,
                GeneratedQrCodeDataUrl = selectedVisitor is null ? null : BuildQrCodeDataUrl(selectedVisitor.QrPayload)
            };
        }

        private static int ParseMonthNumber(string? monthValue)
        {
            if (string.IsNullOrWhiteSpace(monthValue))
            {
                return 0;
            }

            if (int.TryParse(monthValue, out var monthNumber) && monthNumber >= 1 && monthNumber <= 12)
            {
                return monthNumber;
            }

            if (DateTime.TryParseExact(monthValue, "MMMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                return parsedDate.Month;
            }

            if (DateTime.TryParse(monthValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallbackDate))
            {
                return fallbackDate.Month;
            }

            return 0;
        }

        private static DateTime GetNextDueDateUtc(int rentDueDay, IEnumerable<Payment> payments, DateTime referenceUtc)
        {
            var dueDay = Math.Clamp(rentDueDay, 1, 28);
            var thisMonthDueDate = new DateTime(referenceUtc.Year, referenceUtc.Month, dueDay, 0, 0, 0, DateTimeKind.Utc);
            var candidateDueDate = referenceUtc <= thisMonthDueDate ? thisMonthDueDate : thisMonthDueDate.AddMonths(1);

            for (var i = 0; i < 36; i++)
            {
                var hasPaidPeriod = payments.Any(p =>
                    p.Status == PaymentStatus.Verified &&
                    p.PaymentYear == candidateDueDate.Year &&
                    ParseMonthNumber(p.PaymentMonth) == candidateDueDate.Month);

                if (!hasPaidPeriod)
                {
                    return candidateDueDate;
                }

                candidateDueDate = candidateDueDate.AddMonths(1);
            }

            return candidateDueDate;
        }

        private TenantPaymentsViewModel BuildPaymentsViewModel(Tenant tenant, List<Payment> payments)
        {
            var now = DateTime.UtcNow;
            var nextDueDate = GetNextDueDateUtc(tenant.RentDueDay, payments, now);

            return new TenantPaymentsViewModel
            {
                Payments = payments,
                TotalVerifiedAmount = payments.Where(p => p.Status == PaymentStatus.Verified).Sum(p => p.Amount),
                SubmittedCount = payments.Count(p => p.Status == PaymentStatus.Submitted),
                PendingCount = payments.Count(p => p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Overdue),
                MonthlyRent = tenant.MonthlyRent,
                NextDueDate = nextDueDate,
                NewPayment = new CreatePaymentViewModel
                {
                    PaymentMonth = nextDueDate.Month,
                    PaymentYear = nextDueDate.Year,
                    Amount = tenant.MonthlyRent,
                    PaymentDate = now.Date,
                    PaymentMethod = PaymentMethod.OnlineTransfer
                }
            };
        }

        private static IOrderedQueryable<Payment> OrderPaymentHistory(IQueryable<Payment> payments)
        {
            return payments
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.PaymentId);
        }

        private async Task<string> GenerateMockPaymentReceiptPdfAsync(Tenant tenant, Payment payment)
        {
            var receiptsFolder = Path.Combine(_environment.WebRootPath, "uploads", "tenant", tenant.TenantId.ToString(), "payments");
            Directory.CreateDirectory(receiptsFolder);

            var safeMonth = payment.PaymentMonth.Replace(" ", "-", StringComparison.Ordinal);
            var fileName = $"receipt-{payment.PaymentYear}-{safeMonth}-{payment.PaymentId:D6}.pdf".ToLowerInvariant();
            var physicalPath = Path.Combine(receiptsFolder, fileName);

            var receiptDate = payment.PaymentDate?.ToLocalTime() ?? DateTime.UtcNow.ToLocalTime();
            var dueDate = payment.DueDate.ToLocalTime();
            var tenantLabel = tenant.User?.Email ?? $"Tenant #{tenant.TenantId}";
            var propertyLabel = tenant.Property?.PropertyName ?? $"Property #{tenant.PropertyId}";

            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(28);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header().Column(column =>
                    {
                        column.Item().Text("RENTAL PAYMENT RECEIPT").Bold().FontSize(18);
                        column.Item().Text($"Reference: {payment.ReferenceNo}").FontSize(10).FontColor(Colors.Grey.Darken2);
                        column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingTop(14).Column(column =>
                    {
                        column.Spacing(8);
                        column.Item().Text($"Tenant: {tenantLabel}");
                        column.Item().Text($"Property: {propertyLabel}");
                        column.Item().Text($"Billing Period: {payment.PaymentMonth} {payment.PaymentYear}");
                        column.Item().Text($"Amount Paid: {payment.Amount:C}");
                        column.Item().Text($"Payment Method: {payment.PaymentMethod?.ToString() ?? "OnlineTransfer"}");
                        column.Item().Text($"Paid On: {receiptDate:dd MMM yyyy}");
                        column.Item().Text($"Due Date: {dueDate:dd MMM yyyy}");
                        column.Item().Text($"Status: {payment.Status}");
                        column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                        column.Item().Text("This is a system-generated mock receipt for demonstration purposes.")
                            .FontSize(9)
                            .FontColor(Colors.Grey.Darken1);
                    });

                    page.Footer().AlignRight().Text($"Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });

            await System.IO.File.WriteAllBytesAsync(physicalPath, document.GeneratePdf());

            return Path.Combine("uploads", "tenant", tenant.TenantId.ToString(), "payments", fileName).Replace("\\", "/");
        }

        public Task<IActionResult> Dashboard()
        {
            return TenantDashboard();
        }

        public async Task<IActionResult> TenantDashboard()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenantData = await _context.Tenants
                .Include(t => t.User)
                .Include(t => t.Property)
                .Include(t => t.MaintenanceRequests)
                .Include(t => t.Documents)
                .Include(t => t.Payments)
                .Include(t => t.VisitorPasses)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenantData == null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var orderedMaintenanceRequests = tenantData.MaintenanceRequests
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
            var paymentRecords = tenantData.Payments.ToList();
            var visitorPasses = tenantData.VisitorPasses.ToList();
            var nextPaymentDue = GetNextDueDateUtc(tenantData.RentDueDay, paymentRecords, DateTime.UtcNow);
            var openMaintenanceCount = orderedMaintenanceRequests.Count(r =>
                r.Status == MaintenanceStatus.Pending ||
                r.Status == MaintenanceStatus.Approved ||
                r.Status == MaintenanceStatus.InProgress);
            var maintenanceStatusSummary = openMaintenanceCount > 0
                ? $"{openMaintenanceCount} open"
                : orderedMaintenanceRequests.FirstOrDefault()?.Status.ToString() ?? "No requests";

            var chartMonths = Enumerable.Range(0, 6)
                .Select(offset => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-5 + offset))
                .ToList();

            var verifiedPaymentsByMonth = paymentRecords
                .Where(p => p.Status == PaymentStatus.Verified)
                .GroupBy(p => new { p.PaymentYear, Month = ParseMonthNumber(p.PaymentMonth) })
                .Where(g => g.Key.Month >= 1 && g.Key.Month <= 12)
                .ToDictionary(g => (g.Key.PaymentYear, g.Key.Month), g => g.Sum(p => p.Amount));

            var notifications = await BuildTenantNotificationsAsync(tenantData, nextPaymentDue, orderedMaintenanceRequests, visitorPasses);

            var viewModel = new TenantDashboardViewModel
            {
                TenantEmail = tenantData.User.Email,
                PropertyName = tenantData.Property?.PropertyName ?? "No property assigned",
                PropertyAddress = tenantData.Property is null
                    ? "Not assigned"
                    : string.Join(", ", new[]
                    {
                        tenantData.Property.AddressLine1,
                        tenantData.Property.City,
                        tenantData.Property.State,
                        tenantData.Property.PostalCode
                    }.Where(s => !string.IsNullOrWhiteSpace(s))),
                LeaseStartDate = tenantData.LeaseStartDate,
                LeaseEndDate = tenantData.LeaseEndDate,
                LeaseStatus = tenantData.LeaseStatus.ToString(),
                MaintenanceRequest = orderedMaintenanceRequests,
                PaymentRecord = paymentRecords.Count,
                DocumentQuantity = tenantData.Documents.Count(d => !d.IsDeleted),
                VisitorPassCount = tenantData.VisitorPasses.Count,
                MonthlyRent = tenantData.MonthlyRent,
                NextPaymentDue = nextPaymentDue,
                OpenMaintenanceCount = openMaintenanceCount,
                MaintenanceStatusSummary = maintenanceStatusSummary,
                Notifications = notifications,
                PaymentChartLabels = chartMonths.Select(month => month.ToString("MMM yy")).ToList(),
                PaymentChartAmounts = chartMonths
                    .Select(month => verifiedPaymentsByMonth.TryGetValue((month.Year, month.Month), out var amount) ? amount : 0m)
                    .ToList(),
                MaintenanceStatusCounts =
                [
                    orderedMaintenanceRequests.Count(r => r.Status == MaintenanceStatus.Pending),
                    orderedMaintenanceRequests.Count(r => r.Status == MaintenanceStatus.Approved),
                    orderedMaintenanceRequests.Count(r => r.Status == MaintenanceStatus.InProgress),
                    orderedMaintenanceRequests.Count(r => r.Status == MaintenanceStatus.Completed),
                    orderedMaintenanceRequests.Count(r => r.Status == MaintenanceStatus.Rejected)
                ]
            };

            return View("TenantDashboard", viewModel);
        }

        public async Task<IActionResult> PendingAssignment()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;

            if (!string.IsNullOrWhiteSpace(email))
            {
                var hasAssignment = await _context.Tenants.AnyAsync(t => t.User.Email == email);
                if (hasAssignment)
                {
                    return RedirectToAction(nameof(Dashboard));
                }
            }

            return View();
        }

        public async Task<IActionResult> MyProperty()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenantData = await _context.Tenants
                .Include(t => t.User)
                .Include(t => t.Property)
                    .ThenInclude(p => p.Amenities)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenantData == null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var model = new TenantPropertyViewModel
            {
                TenantEmail = tenantData.User.Email,
                LeaseStartDate = tenantData.LeaseStartDate,
                LeaseEndDate = tenantData.LeaseEndDate,
                LeaseStatus = tenantData.LeaseStatus,
                MonthlyRent = tenantData.MonthlyRent,
                RentDueDay = tenantData.RentDueDay,
                DepositPaid = tenantData.DepositPaid,
                DepositStatus = tenantData.DepositStatus,
                Property = tenantData.Property,
                Amenities = tenantData.Property.Amenities
                    .OrderBy(a => a.AmenityName)
                    .ToList()
            };

            return View(model);
        }

        public async Task<IActionResult> MaintenanceRequest()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant == null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var requests = await _context.MaintenanceRequests
                .Include(r => r.Property)
                .Include(r => r.Timeline)
                .Where(r => r.TenantId == tenant.TenantId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var viewModel = new MaintenanceRequestViewModel
            {
                Requests = requests,
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMaintenance(MaintenanceRequestViewModel viewModel)
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant == null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            if (!ModelState.IsValid)
            {
                viewModel.Requests = await _context.MaintenanceRequests
                    .Where(r => r.TenantId == tenant.TenantId)
                    .OrderByDescending(r => r.CreatedAt)
                    .ToListAsync();

                return View("MaintenanceRequest", viewModel);
            }

            var newRequest = new MaintenanceRequest
            {
                TenantId = tenant.TenantId,
                PropertyId = tenant.PropertyId,
                Title = viewModel.NewRequest.Title,
                Category = viewModel.NewRequest.Category,
                Priority = viewModel.NewRequest.Priority,
                Description = viewModel.NewRequest.Description,
                PreferredDate = viewModel.NewRequest.PreferredDate.HasValue
                    ? DateTime.SpecifyKind(viewModel.NewRequest.PreferredDate.Value, DateTimeKind.Utc)
                    : null,
                Status = MaintenanceStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var issueImage = viewModel.NewRequest.IssueImage;
            if (issueImage is not null && issueImage.Length > 0)
            {
                if (issueImage.Length > MaxMaintenanceImageSizeBytes)
                {
                    ModelState.AddModelError("NewRequest.IssueImage", "Image must be 8MB or smaller.");
                }
                else if (!HasAllowedExtension(issueImage.FileName, AllowedMaintenanceImageExtensions))
                {
                    ModelState.AddModelError("NewRequest.IssueImage", "Only JPG, JPEG, PNG, and WEBP images are allowed.");
                }

                if (!ModelState.IsValid)
                {
                    viewModel.Requests = await _context.MaintenanceRequests
                        .Where(r => r.TenantId == tenant.TenantId)
                        .OrderByDescending(r => r.CreatedAt)
                        .ToListAsync();

                    return View("MaintenanceRequest", viewModel);
                }

                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "tenant", tenant.TenantId.ToString(), "maintenance");
                Directory.CreateDirectory(uploadsFolder);

                var safeExtension = Path.GetExtension(issueImage.FileName).ToLowerInvariant();
                var savedFileName = $"{Guid.NewGuid():N}{safeExtension}";
                var physicalPath = Path.Combine(uploadsFolder, savedFileName);
                await using var stream = System.IO.File.Create(physicalPath);
                await issueImage.CopyToAsync(stream);

                newRequest.IssueImageKey = Path.Combine("uploads", "tenant", tenant.TenantId.ToString(), "maintenance", savedFileName)
                    .Replace("\\", "/");
            }

            _context.MaintenanceRequests.Add(newRequest);
            AddMaintenanceTimeline(newRequest, "Request submitted", "Tenant submitted the maintenance request.", email);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Maintenance request submitted.";

            return RedirectToAction(nameof(MaintenanceRequest));
        }

        public async Task<IActionResult> Documents()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant == null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var model = new TenantDocumentsViewModel
            {
                Documents = await _context.Documents
                    .Where(d => d.TenantId == tenant.TenantId && !d.IsDeleted)
                    .OrderByDescending(d => d.CreatedAt)
                    .ToListAsync()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDocumentUpload([FromBody] CreateDirectDocumentUploadRequest? request)
        {
            var email = GetCurrentEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                return Unauthorized(new { message = "Please log in again." });
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant == null)
            {
                return BadRequest(new { message = "Tenant assignment was not found." });
            }

            var uploadResult = await _documentUploadService.CreateTenantDirectUploadAsync(
                request,
                tenant.User.Id,
                tenant.TenantId,
                tenant.PropertyId,
                AllowedDocumentExtensions,
                MaxDocumentSizeBytes);

            if (!uploadResult.Succeeded)
            {
                return BadRequest(new { message = uploadResult.ErrorMessage });
            }

            return Json(uploadResult.Response);
        }

        [HttpGet]
        public async Task<IActionResult> GetDocumentUploadStatus(int id)
        {
            var email = GetCurrentEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                return Unauthorized(new { message = "Please log in again." });
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant is null)
            {
                return BadRequest(new { message = "Tenant assignment was not found." });
            }

            var status = await _documentUploadService.GetTenantUploadStatusAsync(id, tenant.TenantId);

            if (status is null)
            {
                return NotFound(new { message = "Document not found." });
            }

            return Json(status);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDocument(TenantDocumentsViewModel model)
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            var appUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (tenant == null || appUser == null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            if (!ModelState.IsValid)
            {
                model.Documents = await _context.Documents
                    .Where(d => d.TenantId == tenant.TenantId && !d.IsDeleted)
                    .OrderByDescending(d => d.CreatedAt)
                    .ToListAsync();
                return View(nameof(Documents), model);
            }

            var file = model.NewDocument.File;
            if (file is null || file.Length <= 0)
            {
                ModelState.AddModelError("NewDocument.File", "Please choose a valid file.");
                model.Documents = await _context.Documents
                    .Where(d => d.TenantId == tenant.TenantId && !d.IsDeleted)
                    .OrderByDescending(d => d.CreatedAt)
                    .ToListAsync();
                return View(nameof(Documents), model);
            }

            if (file.Length > MaxDocumentSizeBytes)
            {
                ModelState.AddModelError("NewDocument.File", "File size must not exceed 10MB.");
                model.Documents = await _context.Documents
                    .Where(d => d.TenantId == tenant.TenantId && !d.IsDeleted)
                    .OrderByDescending(d => d.CreatedAt)
                    .ToListAsync();
                return View(nameof(Documents), model);
            }

            if (!HasAllowedExtension(file.FileName, AllowedDocumentExtensions))
            {
                ModelState.AddModelError("NewDocument.File", "Allowed file types: PDF, JPG, JPEG, PNG, DOC, DOCX.");
                model.Documents = await _context.Documents
                    .Where(d => d.TenantId == tenant.TenantId && !d.IsDeleted)
                    .OrderByDescending(d => d.CreatedAt)
                    .ToListAsync();
                return View(nameof(Documents), model);
            }

            var safeExtension = Path.GetExtension(file.FileName);
            var savedFileName = $"{Guid.NewGuid():N}{safeExtension}";
            var key = $"tenant/{tenant.TenantId}/documents/{savedFileName}";

            var bucketName = _configuration["AWS:BucketName"];
            var region = _configuration["AWS:Region"] ?? "us-east-1";
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                ModelState.AddModelError("NewDocument.File", "S3 bucket is not configured.");
                model.Documents = await _context.Documents
                    .Where(d => d.TenantId == tenant.TenantId && !d.IsDeleted)
                    .OrderByDescending(d => d.CreatedAt)
                    .ToListAsync();
                return View(nameof(Documents), model);
            }

            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            memory.Position = 0;

            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                InputStream = memory,
                ContentType = file.ContentType,
            };

            await _s3Client.PutObjectAsync(putRequest);

            var fileKey = key;
            var s3Url = $"https://{bucketName}.s3.{region}.amazonaws.com/{fileKey}";

            var document = new MyMvcApp.Models.Document
            {
                UploadedBy = appUser.Id,
                PropertyId = tenant.PropertyId,
                TenantId = tenant.TenantId,
                DocumentName = model.NewDocument.DocumentName,
                DocumentType = model.NewDocument.DocumentType ?? DocumentType.Others,
                FileKey = fileKey,
                FileSize = (int)Math.Min(file.Length, int.MaxValue),
                FileType = file.ContentType,
                S3Url = s3Url,
                S3BucketName = bucketName,
                UploadStatus = Models.DocumentUploadStatus.Confirmed,
                UploadId = Guid.NewGuid().ToString("N"),
                ConfirmedAt = DateTime.UtcNow,
                ValidationMessage = "Uploaded through MVC backend.",
                Notes = model.NewDocument.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Document uploaded successfully.";
            return RedirectToAction(nameof(Documents));
        }

        public async Task<IActionResult> DownloadDocument(int id)
        {
            var email = GetCurrentEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant is null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentId == id && d.TenantId == tenant.TenantId && !d.IsDeleted);

            if (document is null)
            {
                return NotFound();
            }

            if (document.UploadStatus != Models.DocumentUploadStatus.Confirmed)
            {
                TempData["ErrorMessage"] = "This document is still being validated and is not ready for download yet.";
                return RedirectToAction(nameof(Documents));
            }

            var bucketName = _configuration["AWS:BucketName"];
            // If we have an S3 object key, generate a presigned URL and redirect (works with ACL-disabled buckets)
            if (!string.IsNullOrWhiteSpace(document.FileKey) && !string.IsNullOrWhiteSpace(bucketName))
            {
                try
                {
                    var presignRequest = new GetPreSignedUrlRequest
                    {
                        BucketName = bucketName,
                        Key = document.FileKey,
                        Expires = DateTime.UtcNow.AddMinutes(15),
                        Verb = HttpVerb.GET
                    };

                    var url = _s3Client.GetPreSignedURL(presignRequest);
                    return Redirect(url);
                }
                catch (Exception)
                {
                    // fall through to local file fallback or NotFound
                }
            }

            if (!string.IsNullOrWhiteSpace(document.S3Url) && Uri.IsWellFormedUriString(document.S3Url, UriKind.Absolute))
            {
                return Redirect(document.S3Url);
            }

            var relativeFileKey = document.FileKey.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var physicalPath = Path.Combine(_environment.WebRootPath, relativeFileKey);
            if (!System.IO.File.Exists(physicalPath))
            {
                return NotFound();
            }

            var provider = new FileExtensionContentTypeProvider();
            var contentType = provider.TryGetContentType(document.FileKey, out var detected)
                ? detected
                : "application/octet-stream";

            return PhysicalFile(physicalPath, contentType, enableRangeProcessing: true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var email = GetCurrentEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant is null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentId == id && d.TenantId == tenant.TenantId && !d.IsDeleted);

            if (document is null)
            {
                TempData["ErrorMessage"] = "Document not found.";
                return RedirectToAction(nameof(Documents));
            }

            document.IsDeleted = true;
            document.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Document is deleted.";
            return RedirectToAction(nameof(Documents));
        }

        public async Task<IActionResult> Payments()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .Include(t => t.Property)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant == null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var payments = await OrderPaymentHistory(_context.Payments
                .Where(p => p.TenantId == tenant.TenantId))
                .ToListAsync();

            return View(BuildPaymentsViewModel(tenant, payments));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCheckoutSession()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .Include(t => t.Property)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant == null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var stripeSecretKey = _configuration["Stripe:SecretKey"];
            if (string.IsNullOrWhiteSpace(stripeSecretKey) || stripeSecretKey.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Stripe secret key is not configured.";
                return RedirectToAction(nameof(Payments));
            }

            Stripe.StripeConfiguration.ApiKey = stripeSecretKey;

            var existingPayments = await OrderPaymentHistory(_context.Payments
                .Where(p => p.TenantId == tenant.TenantId))
                .ToListAsync();

            var now = DateTime.UtcNow;
            var nextDueDate = GetNextDueDateUtc(tenant.RentDueDay, existingPayments, now);
            var month = nextDueDate.Month;
            var year = nextDueDate.Year;
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
            var propertyName = tenant.Property?.PropertyName ?? "Rental Property";
            var unitAmount = (long)Math.Round(tenant.MonthlyRent * 100m, MidpointRounding.AwayFromZero);
            var payment = existingPayments.FirstOrDefault(p =>
                p.Status == PaymentStatus.Pending &&
                p.PaymentYear == year &&
                ParseMonthNumber(p.PaymentMonth) == month);

            if (payment is null)
            {
                payment = new Payment
                {
                    TenantId = tenant.TenantId,
                    PropertyId = tenant.PropertyId,
                    PaymentMonth = monthName,
                    PaymentYear = year,
                    Amount = tenant.MonthlyRent,
                    DueDate = nextDueDate,
                    PaymentMethod = PaymentMethod.OnlineTransfer,
                    Status = PaymentStatus.Pending,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();
            }

            var successPath = Url.Action(nameof(PaymentSuccess), "Tenant");
            var cancelPath = Url.Action(nameof(PaymentCancel), "Tenant");
            var successUrl = $"{Request.Scheme}://{Request.Host}{successPath}?session_id={{CHECKOUT_SESSION_ID}}";
            var cancelUrl = $"{Request.Scheme}://{Request.Host}{cancelPath}?session_id={{CHECKOUT_SESSION_ID}}";
            var stripeMetadata = new Dictionary<string, string>
            {
                ["paymentId"] = payment.PaymentId.ToString(CultureInfo.InvariantCulture),
                ["tenantId"] = tenant.TenantId.ToString(CultureInfo.InvariantCulture),
                ["propertyId"] = tenant.PropertyId.ToString(CultureInfo.InvariantCulture),
                ["paymentMonth"] = monthName,
                ["paymentYear"] = year.ToString(CultureInfo.InvariantCulture)
            };

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                CustomerEmail = tenant.User.Email,
                ClientReferenceId = payment.PaymentId.ToString(CultureInfo.InvariantCulture),
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                PaymentMethodTypes = new List<string> { "card" },
                PaymentIntentData = new SessionPaymentIntentDataOptions
                {
                    Metadata = stripeMetadata,
                    ReceiptEmail = tenant.User.Email
                },
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "myr",
                            UnitAmount = unitAmount,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"Rent - {propertyName}",
                                Description = $"{monthName} {year} rent payment"
                            }
                        }
                    }
                },
                Metadata = stripeMetadata
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            payment.StripeSessionId = session.Id;
            payment.ReferenceNo = session.Id;
            payment.LandlordRemarks = "Awaiting Stripe confirmation from Amazon EventBridge.";
            payment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Redirect(session.Url);
        }

        [HttpGet]
        public IActionResult PaymentSuccess(string? session_id)
        {
            ViewBag.StripeSessionId = session_id;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> PaymentCancel(string? session_id)
        {
            if (!string.IsNullOrWhiteSpace(session_id))
            {
                var payment = await _context.Payments.FirstOrDefaultAsync(p =>
                    p.StripeSessionId == session_id &&
                    p.Status == PaymentStatus.Pending);

                if (payment is not null)
                {
                    payment.Status = PaymentStatus.Cancelled;
                    payment.LandlordRemarks = "Stripe Checkout was cancelled before payment was completed.";
                    payment.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            return View();
        }

        public async Task<IActionResult> Visitors(int? passId)
        {
            var email = GetCurrentEmail();

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .Include(t => t.Property)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant == null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            if (passId.HasValue)
            {
                var pass = await _context.VisitorPasses.FirstOrDefaultAsync(v => v.VisitorPassId == passId.Value && v.TenantId == tenant.TenantId);
                if (pass != null)
                {
                    return View(await BuildVisitorViewModelAsync(tenant, null, pass.VisitorPassId));
                }

            }
            return View(await BuildVisitorViewModelAsync(tenant));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterVisitor(TenantVisitorsViewModel model)
        {
            var email = GetCurrentEmail();

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .Include(t => t.Property)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant == null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            if (!ModelState.IsValid)
            {
                return View(nameof(Visitors), await BuildVisitorViewModelAsync(tenant, model.NewVisitor));
            }

            var visitDate = model.NewVisitor.VisitDate.HasValue
                ? DateTime.SpecifyKind(model.NewVisitor.VisitDate.Value, DateTimeKind.Utc)
                : DateTime.UtcNow.Date;

            var passCode = $"VIS-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
            var qrPayload = $"MyMvcApp Visitor Pass|Tenant:{tenant.TenantId}|Property:{tenant.PropertyId}|Code:{passCode}|Visitor:{model.NewVisitor.VisitorName}|Date:{visitDate:yyyy-MM-dd}|Purpose:{model.NewVisitor.Purpose}";

            var visitorPass = new VisitorPass
            {
                TenantId = tenant.TenantId,
                VisitorName = model.NewVisitor.VisitorName,
                VisitorPhone = model.NewVisitor.VisitorPhone,
                Purpose = model.NewVisitor.Purpose,
                VisitDate = visitDate,
                PassCode = passCode,
                QrPayload = qrPayload,
                Notes = model.NewVisitor.Notes,
                Status = VisitorPassStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.VisitorPasses.Add(visitorPass);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visitor pass created and QR code generated.";

            return RedirectToAction(nameof(Visitors), new { passId = visitorPass.VisitorPassId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelVisitorPass(int id)
        {
            var email = GetCurrentEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant is null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var pass = await _context.VisitorPasses
                .FirstOrDefaultAsync(v => v.VisitorPassId == id && v.TenantId == tenant.TenantId);

            if (pass is null)
            {
                TempData["ErrorMessage"] = "Visitor pass not found.";
                return RedirectToAction(nameof(Visitors));
            }

            if (pass.Status == VisitorPassStatus.Used || pass.Status == VisitorPassStatus.Cancelled)
            {
                TempData["ErrorMessage"] = "This visitor pass can no longer be cancelled.";
                return RedirectToAction(nameof(Visitors), new { passId = pass.VisitorPassId });
            }

            pass.Status = VisitorPassStatus.Cancelled;
            pass.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visitor pass cancelled.";
            return RedirectToAction(nameof(Visitors), new { passId = pass.VisitorPassId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkVisitorPassUsed(int id)
        {
            var email = GetCurrentEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant is null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var pass = await _context.VisitorPasses
                .FirstOrDefaultAsync(v => v.VisitorPassId == id && v.TenantId == tenant.TenantId);

            if (pass is null)
            {
                TempData["ErrorMessage"] = "Visitor pass not found.";
                return RedirectToAction(nameof(Visitors));
            }

            if (pass.Status != VisitorPassStatus.Active)
            {
                TempData["ErrorMessage"] = "Only active passes can be marked as used.";
                return RedirectToAction(nameof(Visitors), new { passId = pass.VisitorPassId });
            }

            pass.Status = VisitorPassStatus.Used;
            pass.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visitor pass marked as used.";
            return RedirectToAction(nameof(Visitors), new { passId = pass.VisitorPassId });
        }

        [Authorize(Roles = "Security")]
        [Route("/ValidateQrPass")]
        //[Route("/Tenant/ValidateVisitorPass")]
        public async Task<IActionResult> ValidateVisitorPass(string? passCode, bool checkedIn = false)
        {
            var code = ExtractPassCode(passCode);
            var model = new VisitorPassValidationViewModel
            {
                PassCode = code,
                CheckedIn = checkedIn,
                StatusMessage = string.IsNullOrWhiteSpace(code)
                    ? "Enter a visitor pass code to validate access."
                    : "Pass code not found."
            };

            if (string.IsNullOrWhiteSpace(code))
            {
                return View(model);
            }

            var pass = await _context.VisitorPasses
                .Include(v => v.Tenant)
                .ThenInclude(t => t.Property)
                .FirstOrDefaultAsync(v => v.PassCode == code);

            if (pass is null)
            {
                return View(model);
            }

            if (pass.Status == VisitorPassStatus.Active && pass.VisitDate.Date < DateTime.UtcNow.Date)
            {
                pass.Status = VisitorPassStatus.Expired;
                pass.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            var isValid = pass.Status == VisitorPassStatus.Active && pass.VisitDate.Date >= DateTime.UtcNow.Date;
            model.Pass = pass;
            model.Found = true;
            model.IsValid = isValid;
            model.StatusMessage = isValid
                ? "Valid pass. Entry may proceed."
                : $"Pass is {pass.Status}. Entry denied.";

            return View(model);
        }

        [Authorize(Roles = "Security")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidateVisitorPassAndCheckIn(string passCode)
        {
            var code = ExtractPassCode(passCode);
            if (string.IsNullOrWhiteSpace(code))
            {
                return RedirectToAction(nameof(ValidateVisitorPass));
            }

            var pass = await _context.VisitorPasses
                .FirstOrDefaultAsync(v => v.PassCode == code);

            if (pass is null)
            {
                return RedirectToAction(nameof(ValidateVisitorPass), new { passCode = code });
            }

            if (pass.Status != VisitorPassStatus.Active || pass.VisitDate.Date < DateTime.UtcNow.Date)
            {
                return RedirectToAction(nameof(ValidateVisitorPass), new { passCode = code });
            }

            pass.Status = VisitorPassStatus.Used;
            pass.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ValidateVisitorPass), new { passCode = code, checkedIn = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmMaintenanceCompletion(int requestId, int? rating, string? feedbackComment)
        {
            var email = GetCurrentEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Login", "Account");
            }

            var tenant = await _context.Tenants
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.User.Email == email && !t.User.IsDisabled);

            if (tenant is null)
            {
                return RedirectToAction(nameof(PendingAssignment));
            }

            var request = await _context.MaintenanceRequests
                .FirstOrDefaultAsync(r => r.RequestId == requestId && r.TenantId == tenant.TenantId);

            if (request is null)
            {
                TempData["ErrorMessage"] = "Maintenance request not found.";
                return RedirectToAction(nameof(MaintenanceRequest));
            }

            if (request.Status != MaintenanceStatus.Completed)
            {
                TempData["ErrorMessage"] = "Only completed requests can be confirmed.";
                return RedirectToAction(nameof(MaintenanceRequest));
            }

            if (rating.HasValue && (rating < 1 || rating > 5))
            {
                TempData["ErrorMessage"] = "Rating must be between 1 and 5.";
                return RedirectToAction(nameof(MaintenanceRequest));
            }

            request.TenantConfirmedAt = DateTime.UtcNow;
            request.TenantFeedbackRating = rating;
            request.TenantFeedbackComment = string.IsNullOrWhiteSpace(feedbackComment)
                ? null
                : feedbackComment.Trim()[..Math.Min(feedbackComment.Trim().Length, 1000)];
            request.UpdatedAt = DateTime.UtcNow;
            AddMaintenanceTimeline(
                request,
                "Completion confirmed",
                rating.HasValue
                    ? $"Tenant confirmed completion with rating {rating}/5."
                    : "Tenant confirmed completion.",
                email);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Completion confirmed. Thank you for your feedback.";
            return RedirectToAction(nameof(MaintenanceRequest));
        }

        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> Announcements()
        {
            var announcements = await _context.SystemAnnouncements
                .AsNoTracking()
                .Where(a => a.VisibleTo == "All" || a.VisibleTo == "Tenant")
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return View(announcements);
        }

        private void AddMaintenanceTimeline(MaintenanceRequest request, string action, string? details, string actorEmail)
        {
            _context.MaintenanceTimelines.Add(new MaintenanceTimeline
            {
                MaintenanceRequest = request,
                RequestId = request.RequestId,
                Action = action,
                Details = details,
                ActorEmail = actorEmail,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
