using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using MyMvcApp.Models.Admin;
using System.Security.Claims;
using System.Text;

namespace MyMvcApp.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminPaymentController : Controller
    {
        private static readonly PaymentStatus[] ClosedPaymentStatuses =
        {
            PaymentStatus.Verified,
            PaymentStatus.Rejected,
            PaymentStatus.Failed,
            PaymentStatus.Cancelled,
            PaymentStatus.Refunded
        };

        private readonly AppDbContext _db;

        public AdminPaymentController(AppDbContext db)
        {
            _db = db;
        }

        // GET /AdminPayment/Index
        public async Task<IActionResult> Index(PaymentFilterViewModel filter)
        {
            filter.Page = Math.Max(1, filter.Page);

            var today = DateTime.UtcNow.Date;
            var query = _db.Payments.AsNoTracking()
                .Include(p => p.Tenant).ThenInclude(t => t.User)
                .Include(p => p.Property).ThenInclude(prop => prop.Landlord)
                .AsQueryable();

            // Search across tenant email, property name, reference number
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim();
                query = query.Where(p =>
                    p.Tenant.User.Email.Contains(s) ||
                    p.Property.PropertyName.Contains(s) ||
                    (p.ReferenceNo != null && p.ReferenceNo.Contains(s)) ||
                    (p.StripeSessionId != null && p.StripeSessionId.Contains(s)) ||
                    (p.StripePaymentIntentId != null && p.StripePaymentIntentId.Contains(s)) ||
                    (p.Property.Landlord != null && p.Property.Landlord.Email.Contains(s)));
            }

            // Status filter — "Overdue" means computed overdue
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

            // Date range on DueDate
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

            // Sorting
            query = (filter.SortBy, filter.SortDir?.ToLower()) switch
            {
                ("amount", "asc") => query.OrderBy(p => p.Amount),
                ("amount", _) => query.OrderByDescending(p => p.Amount),
                ("submitted", "asc") => query.OrderBy(p => p.PaymentDate),
                ("submitted", _) => query.OrderByDescending(p => p.PaymentDate),
                ("updated", "asc") => query.OrderBy(p => p.UpdatedAt),
                ("updated", _) => query.OrderByDescending(p => p.UpdatedAt),
                ("due", "asc") => query.OrderBy(p => p.DueDate),
                _ => query.OrderByDescending(p => p.DueDate)
            };

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)PaymentFilterViewModel.PageSize);
            filter.Page = Math.Min(filter.Page, Math.Max(1, totalPages));

            var payments = await query
                .Skip((filter.Page - 1) * PaymentFilterViewModel.PageSize)
                .Take(PaymentFilterViewModel.PageSize)
                .Select(p => new PaymentListItemViewModel
                {
                    PaymentId = p.PaymentId,
                    TenantEmail = p.Tenant.User.Email,
                    PropertyName = p.Property.PropertyName,
                    UnitNumber = p.Property.UnitNumber,
                    LandlordEmail = p.Property.Landlord != null ? p.Property.Landlord.Email : null,
                    Amount = p.Amount,
                    Status = p.Status,
                    IsComputedOverdue = p.DueDate < DateTime.UtcNow
                        && !ClosedPaymentStatuses.Contains(p.Status),
                    DueDate = p.DueDate,
                    SubmittedDate = p.PaymentDate,
                    VerifiedDate = p.Status == PaymentStatus.Verified ? p.UpdatedAt : (DateTime?)null,
                    PaymentMethod = p.PaymentMethod,
                    ReferenceNo = p.ReferenceNo,
                    StripeSessionId = p.StripeSessionId,
                    StripePaymentIntentId = p.StripePaymentIntentId,
                    StripeReceiptUrl = p.StripeReceiptUrl,
                    StripeRefundId = p.StripeRefundId,
                    PaymentPeriod = p.PaymentMonth + " " + p.PaymentYear
                })
                .ToListAsync();

            // Stats (across entire dataset, no filter)
            var allPayments = _db.Payments.AsNoTracking();
            var utcNow = DateTime.UtcNow;
            var currentMonthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var nextMonthStart = currentMonthStart.AddMonths(1);
            var previousMonthStart = currentMonthStart.AddMonths(-1);

            var vm = new AdminPaymentListViewModel
            {
                Filter = filter,
                Payments = payments,
                TotalCount = totalCount,
                TotalPages = Math.Max(1, totalPages),
                CurrentPage = filter.Page,
                PendingCount = await allPayments.CountAsync(p => p.Status == PaymentStatus.Pending),
                SubmittedCount = await allPayments.CountAsync(p => p.Status == PaymentStatus.Submitted),
                VerifiedCount = await allPayments.CountAsync(p => p.Status == PaymentStatus.Verified),
                RejectedCount = await allPayments.CountAsync(p => p.Status == PaymentStatus.Rejected),
                FailedCount = await allPayments.CountAsync(p => p.Status == PaymentStatus.Failed),
                CancelledCount = await allPayments.CountAsync(p => p.Status == PaymentStatus.Cancelled),
                RefundedCount = await allPayments.CountAsync(p => p.Status == PaymentStatus.Refunded),
                ComputedOverdueCount = await allPayments.CountAsync(p =>
                    p.DueDate < utcNow &&
                    !ClosedPaymentStatuses.Contains(p.Status)),
                TotalVerifiedAmount = await allPayments
                    .Where(p => p.Status == PaymentStatus.Verified)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0m,
                CurrentMonthRevenue = await allPayments
                    .Where(p => p.Status == PaymentStatus.Verified
                        && p.PaymentDate >= currentMonthStart
                        && p.PaymentDate < nextMonthStart)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0m,
                PreviousMonthRevenue = await allPayments
                    .Where(p => p.Status == PaymentStatus.Verified
                        && p.PaymentDate >= previousMonthStart
                        && p.PaymentDate < currentMonthStart)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0m,
                MonthlyRevenueReport = await BuildMonthlyRevenueReportAsync(utcNow),
                OverdueTenantReport = await BuildOverdueTenantReportAsync(utcNow),
                TenantReliabilityReport = await BuildTenantReliabilityReportAsync(utcNow)
            };

            return View(vm);
        }

        // GET /AdminPayment/Detail/{id}
        public async Task<IActionResult> Detail(int id, string? returnUrl)
        {
            var payment = await _db.Payments.AsNoTracking()
                .Include(p => p.Tenant).ThenInclude(t => t.User)
                .Include(p => p.Property).ThenInclude(prop => prop.Landlord)
                .FirstOrDefaultAsync(p => p.PaymentId == id);

            if (payment == null)
                return NotFound();

            var utcNow = DateTime.UtcNow;
            var vm = new PaymentDetailViewModel
            {
                PaymentId = payment.PaymentId,
                TenantEmail = payment.Tenant.User.Email,
                PropertyName = payment.Property.PropertyName,
                UnitNumber = payment.Property.UnitNumber,
                LandlordEmail = payment.Property.Landlord?.Email,
                Amount = payment.Amount,
                Status = payment.Status,
                IsComputedOverdue = payment.DueDate < utcNow
                    && !ClosedPaymentStatuses.Contains(payment.Status),
                DueDate = payment.DueDate,
                SubmittedDate = payment.PaymentDate,
                VerifiedDate = payment.Status == PaymentStatus.Verified ? payment.UpdatedAt : null,
                PaymentMethod = payment.PaymentMethod,
                ReferenceNo = payment.ReferenceNo,
                ReceiptFileUrl = !string.IsNullOrWhiteSpace(payment.StripeReceiptUrl)
                    ? payment.StripeReceiptUrl
                    : !string.IsNullOrWhiteSpace(payment.ReceiptFileKey)
                    ? "/" + payment.ReceiptFileKey.TrimStart('/')
                    : null,
                StripeSessionId = payment.StripeSessionId,
                StripePaymentIntentId = payment.StripePaymentIntentId,
                StripeReceiptUrl = payment.StripeReceiptUrl,
                StripeEventId = payment.StripeEventId,
                StripeRefundId = payment.StripeRefundId,
                RefundAmount = payment.RefundAmount,
                RefundDate = payment.RefundDate,
                RefundReason = payment.RefundReason,
                LandlordRemarks = payment.LandlordRemarks,
                PaymentPeriod = payment.PaymentMonth + " " + payment.PaymentYear,
                ReturnUrl = returnUrl
            };

            return View(vm);
        }

        // POST /AdminPayment/Verify/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Verify(int id, string? returnUrl)
        {
            var payment = await _db.Payments.FindAsync(id);
            if (payment == null)
                return NotFound();

            if (payment.Status == PaymentStatus.Verified)
            {
                TempData["ErrorMessage"] = "This payment is already verified.";
                return RedirectToAction(nameof(Detail), new { id, returnUrl });
            }

            if (!string.IsNullOrWhiteSpace(payment.StripeSessionId))
            {
                TempData["ErrorMessage"] = "Stripe payments are verified by Amazon EventBridge. Wait for the Stripe event instead of verifying manually.";
                return RedirectToAction(nameof(Detail), new { id, returnUrl });
            }

            payment.Status = PaymentStatus.Verified;
            if (!payment.PaymentDate.HasValue)
                payment.PaymentDate = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;

            AddAuditLog(
                "VerifyPayment",
                "Payment",
                payment.PaymentId,
                null,
                $"Verified payment #{payment.PaymentId} ({payment.PaymentMonth} {payment.PaymentYear}, {payment.Amount:C}).");
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Payment #{id} has been verified.";
            return RedirectToDetail(id, returnUrl);
        }

        // POST /AdminPayment/Reject/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string? remarks, string? returnUrl)
        {
            var payment = await _db.Payments.FindAsync(id);
            if (payment == null)
                return NotFound();

            if (payment.Status == PaymentStatus.Verified)
            {
                TempData["ErrorMessage"] = "A verified payment cannot be rejected.";
                return RedirectToAction(nameof(Detail), new { id, returnUrl });
            }

            payment.Status = PaymentStatus.Rejected;
            payment.LandlordRemarks = remarks?.Trim();
            payment.UpdatedAt = DateTime.UtcNow;

            AddAuditLog(
                "RejectPayment",
                "Payment",
                payment.PaymentId,
                null,
                $"Rejected payment #{payment.PaymentId} ({payment.PaymentMonth} {payment.PaymentYear}). Remarks: {remarks}");
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Payment #{id} has been rejected.";
            return RedirectToDetail(id, returnUrl);
        }

        // GET /AdminPayment/ExportCsv
        public async Task<IActionResult> ExportCsv(PaymentFilterViewModel filter)
        {
            var today = DateTime.UtcNow.Date;
            var query = _db.Payments.AsNoTracking()
                .Include(p => p.Tenant).ThenInclude(t => t.User)
                .Include(p => p.Property).ThenInclude(prop => prop.Landlord)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim();
                query = query.Where(p =>
                    p.Tenant.User.Email.Contains(s) ||
                    p.Property.PropertyName.Contains(s) ||
                    (p.ReferenceNo != null && p.ReferenceNo.Contains(s)) ||
                    (p.StripeSessionId != null && p.StripeSessionId.Contains(s)) ||
                    (p.StripePaymentIntentId != null && p.StripePaymentIntentId.Contains(s)) ||
                    (p.Property.Landlord != null && p.Property.Landlord.Email.Contains(s)));
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

            query = query.OrderByDescending(p => p.DueDate);

            var records = await query
                .Select(p => new
                {
                    p.PaymentId,
                    TenantEmail = p.Tenant.User.Email,
                    PropertyName = p.Property.PropertyName,
                    LandlordEmail = p.Property.Landlord != null ? p.Property.Landlord.Email : "",
                    p.Amount,
                    Status = p.Status.ToString(),
                    PaymentPeriod = p.PaymentMonth + " " + p.PaymentYear,
                    DueDate = p.DueDate,
                    SubmittedDate = p.PaymentDate,
                    VerifiedDate = p.Status == PaymentStatus.Verified ? p.UpdatedAt : (DateTime?)null,
                    PaymentMethod = p.PaymentMethod != null ? p.PaymentMethod.ToString() : "",
                    ReferenceNo = p.ReferenceNo ?? "",
                    StripeSessionId = p.StripeSessionId ?? "",
                    StripePaymentIntentId = p.StripePaymentIntentId ?? "",
                    StripeReceiptUrl = p.StripeReceiptUrl ?? "",
                    StripeRefundId = p.StripeRefundId ?? "",
                    RefundAmount = p.RefundAmount,
                    RefundDate = p.RefundDate,
                    RefundReason = p.RefundReason ?? ""
                })
                .ToListAsync();

            AddAuditLog(
                "ExportPaymentReport",
                "Payment",
                null,
                null,
                $"Exported {records.Count} payment records to CSV. Filter: status={filter.Status}, search={filter.Search}, from={filter.FromDate:yyyy-MM-dd}, to={filter.ToDate:yyyy-MM-dd}.");
            await _db.SaveChangesAsync();

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

            var fileName = $"payment-report-{DateTime.UtcNow:yyyy-MM-dd}.csv";
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", fileName);
        }

        private IActionResult RedirectToDetail(int id, string? returnUrl)
        {
            return RedirectToAction(nameof(Detail), new { id, returnUrl });
        }

        private async Task<List<MonthlyRevenueReportItem>> BuildMonthlyRevenueReportAsync(DateTime utcNow)
        {
            var startMonth = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-5);
            var endMonth = startMonth.AddMonths(6);

            var payments = await _db.Payments.AsNoTracking()
                .Where(p => p.PaymentDate.HasValue
                    && p.PaymentDate.Value >= startMonth
                    && p.PaymentDate.Value < endMonth
                    && (p.Status == PaymentStatus.Verified || p.Status == PaymentStatus.Refunded))
                .Select(p => new
                {
                    PaymentDate = p.PaymentDate!.Value,
                    p.Status,
                    p.Amount,
                    p.RefundAmount
                })
                .ToListAsync();

            return Enumerable.Range(0, 6)
                .Select(offset => startMonth.AddMonths(offset))
                .Select(month =>
                {
                    var rows = payments.Where(p => p.PaymentDate.Year == month.Year && p.PaymentDate.Month == month.Month).ToList();
                    return new MonthlyRevenueReportItem
                    {
                        MonthLabel = month.ToString("MMM yyyy"),
                        VerifiedAmount = rows.Where(p => p.Status == PaymentStatus.Verified).Sum(p => p.Amount),
                        VerifiedCount = rows.Count(p => p.Status == PaymentStatus.Verified),
                        RefundedAmount = rows.Where(p => p.Status == PaymentStatus.Refunded).Sum(p => p.RefundAmount ?? p.Amount)
                    };
                })
                .ToList();
        }

        private async Task<List<OverdueTenantReportItem>> BuildOverdueTenantReportAsync(DateTime utcNow)
        {
            var overduePayments = await _db.Payments.AsNoTracking()
                .Include(p => p.Tenant).ThenInclude(t => t.User)
                .Include(p => p.Property)
                .Where(p => p.DueDate < utcNow && !ClosedPaymentStatuses.Contains(p.Status))
                .Select(p => new
                {
                    p.TenantId,
                    TenantEmail = p.Tenant.User.Email,
                    PropertyName = p.Property.PropertyName,
                    p.Property.UnitNumber,
                    p.Amount,
                    p.DueDate
                })
                .ToListAsync();

            return overduePayments
                .GroupBy(p => new { p.TenantId, p.TenantEmail, p.PropertyName, p.UnitNumber })
                .Select(g =>
                {
                    var oldestDueDate = g.Min(p => p.DueDate);
                    return new OverdueTenantReportItem
                    {
                        TenantId = g.Key.TenantId,
                        TenantEmail = g.Key.TenantEmail,
                        PropertyName = g.Key.PropertyName,
                        UnitNumber = g.Key.UnitNumber,
                        OverdueCount = g.Count(),
                        OverdueAmount = g.Sum(p => p.Amount),
                        OldestDueDate = oldestDueDate,
                        DaysOverdue = Math.Max(0, (int)(utcNow.Date - oldestDueDate.Date).TotalDays)
                    };
                })
                .OrderByDescending(r => r.OverdueAmount)
                .ThenByDescending(r => r.DaysOverdue)
                .Take(10)
                .ToList();
        }

        private async Task<List<TenantPaymentReliabilityItem>> BuildTenantReliabilityReportAsync(DateTime utcNow)
        {
            var payments = await _db.Payments.AsNoTracking()
                .Include(p => p.Tenant).ThenInclude(t => t.User)
                .Include(p => p.Property)
                .Select(p => new
                {
                    p.TenantId,
                    TenantEmail = p.Tenant.User.Email,
                    PropertyName = p.Property.PropertyName,
                    p.Status,
                    p.PaymentDate,
                    p.DueDate
                })
                .ToListAsync();

            return payments
                .GroupBy(p => new { p.TenantId, p.TenantEmail, p.PropertyName })
                .Select(g =>
                {
                    var total = g.Count();
                    var verified = g.Count(p => p.Status == PaymentStatus.Verified || p.Status == PaymentStatus.Refunded);
                    var lateOrProblem = g.Count(p =>
                        p.Status == PaymentStatus.Failed ||
                        p.Status == PaymentStatus.Cancelled ||
                        p.Status == PaymentStatus.Rejected ||
                        (p.PaymentDate.HasValue && p.PaymentDate.Value.Date > p.DueDate.Date) ||
                        (!p.PaymentDate.HasValue && p.DueDate.Date < utcNow.Date && !ClosedPaymentStatuses.Contains(p.Status)));
                    var onTime = g.Count(p => p.Status == PaymentStatus.Verified
                        && p.PaymentDate.HasValue
                        && p.PaymentDate.Value.Date <= p.DueDate.Date);

                    return new TenantPaymentReliabilityItem
                    {
                        TenantId = g.Key.TenantId,
                        TenantEmail = g.Key.TenantEmail,
                        PropertyName = g.Key.PropertyName,
                        TotalPayments = total,
                        VerifiedPayments = verified,
                        LateOrProblemPayments = lateOrProblem,
                        OnTimeRate = total == 0 ? 0 : Math.Round(onTime * 100d / total, 1),
                        ReliabilityScore = total == 0 ? 0 : Math.Round(Math.Max(0, (total - lateOrProblem) * 100d / total), 1)
                    };
                })
                .OrderBy(r => r.ReliabilityScore)
                .ThenByDescending(r => r.LateOrProblemPayments)
                .Take(10)
                .ToList();
        }

        private void AddAuditLog(string action, string targetType, int? targetId, string? targetEmail, string? details)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Action = action,
                ActorEmail = GetCurrentUserEmail(),
                TargetType = targetType,
                TargetId = targetId,
                TargetEmail = targetEmail,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });
        }

        private string GetCurrentUserEmail()
        {
            return User.FindFirstValue(ClaimTypes.Email)
                ?? User.Identity?.Name
                ?? "Unknown admin";
        }

        private static string CsvEscape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}
