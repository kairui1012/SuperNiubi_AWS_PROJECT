using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Stripe.Checkout;
using System.Globalization;
using System.Security.Claims;
using QRCoder;

namespace MyMvcApp.Controllers

{
    [Authorize] // Ensures only logged-in users can reach this page
    public class TenantController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;

        public TenantController(AppDbContext context, IWebHostEnvironment environment, IConfiguration configuration)
        {
            _context = context;
            _environment = environment;
            _configuration = configuration;
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

        private async Task<TenantVisitorsViewModel> BuildVisitorViewModelAsync(Tenant tenant, CreateVisitorViewModel? newVisitor = null, int? selectedVisitorPassId = null)
        {
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
            var nextPaymentDue = GetNextDueDateUtc(tenantData.RentDueDay, paymentRecords, DateTime.UtcNow);
            var openMaintenanceCount = orderedMaintenanceRequests.Count(r =>
                r.Status == MaintenanceStatus.Pending ||
                r.Status == MaintenanceStatus.Approved ||
                r.Status == MaintenanceStatus.InProgress);
            var maintenanceStatusSummary = openMaintenanceCount > 0
                ? $"{openMaintenanceCount} open"
                : orderedMaintenanceRequests.FirstOrDefault()?.Status.ToString() ?? "No requests";

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
                MaintenanceStatusSummary = maintenanceStatusSummary
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

            _context.MaintenanceRequests.Add(newRequest);
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

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "tenant", tenant.TenantId.ToString(), "documents");
            Directory.CreateDirectory(uploadsFolder);

            var safeExtension = Path.GetExtension(file.FileName);
            var savedFileName = $"{Guid.NewGuid():N}{safeExtension}";
            var physicalPath = Path.Combine(uploadsFolder, savedFileName);
            await using (var stream = System.IO.File.Create(physicalPath))
            {
                await file.CopyToAsync(stream);
            }

            var fileKey = Path.Combine("uploads", "tenant", tenant.TenantId.ToString(), "documents", savedFileName)
                .Replace("\\", "/");

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
                S3Url = "/" + fileKey,
                Notes = model.NewDocument.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Document uploaded successfully.";
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

            var payments = await _context.Payments
                .Where(p => p.TenantId == tenant.TenantId)
                .OrderByDescending(p => p.PaymentYear)
                .ThenByDescending(p => p.PaymentDate)
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

            var existingPayments = await _context.Payments
                .Where(p => p.TenantId == tenant.TenantId)
                .OrderByDescending(p => p.PaymentYear)
                .ThenByDescending(p => p.PaymentDate)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var nextDueDate = GetNextDueDateUtc(tenant.RentDueDay, existingPayments, now);
            var month = nextDueDate.Month;
            var year = nextDueDate.Year;
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
            var propertyName = tenant.Property?.PropertyName ?? "Rental Property";
            var unitAmount = (long)Math.Round(tenant.MonthlyRent * 100m, MidpointRounding.AwayFromZero);
            var successUrl = Url.Action(nameof(PaymentSuccess), "Tenant", null, Request.Scheme);
            var cancelUrl = Url.Action(nameof(PaymentCancel), "Tenant", null, Request.Scheme);

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                CustomerEmail = tenant.User.Email,
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                PaymentMethodTypes = new List<string> { "card" },
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
                Metadata = new Dictionary<string, string>
                {
                    ["tenantId"] = tenant.TenantId.ToString(CultureInfo.InvariantCulture),
                    ["propertyId"] = tenant.PropertyId.ToString(CultureInfo.InvariantCulture),
                    ["paymentMonth"] = monthName,
                    ["paymentYear"] = year.ToString(CultureInfo.InvariantCulture)
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return Redirect(session.Url);
        }

        [HttpGet]
        public IActionResult PaymentSuccess()
        {
            return View();
        }

        [HttpGet]
        public IActionResult PaymentCancel()
        {
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
    }
}
