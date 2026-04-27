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
                    (p.Property.Landlord != null && p.Property.Landlord.Email.Contains(s)));
            }

            // Status filter — "Overdue" means computed overdue
            if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "All")
            {
                if (filter.Status == "Overdue")
                {
                    query = query.Where(p =>
                        p.DueDate < DateTime.UtcNow &&
                        p.Status != PaymentStatus.Verified &&
                        p.Status != PaymentStatus.Rejected);
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
                        && p.Status != PaymentStatus.Verified
                        && p.Status != PaymentStatus.Rejected,
                    DueDate = p.DueDate,
                    SubmittedDate = p.PaymentDate,
                    VerifiedDate = p.Status == PaymentStatus.Verified ? p.UpdatedAt : (DateTime?)null,
                    PaymentMethod = p.PaymentMethod,
                    ReferenceNo = p.ReferenceNo,
                    PaymentPeriod = p.PaymentMonth + " " + p.PaymentYear
                })
                .ToListAsync();

            // Stats (across entire dataset, no filter)
            var allPayments = _db.Payments.AsNoTracking();
            var utcNow = DateTime.UtcNow;

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
                ComputedOverdueCount = await allPayments.CountAsync(p =>
                    p.DueDate < utcNow &&
                    p.Status != PaymentStatus.Verified &&
                    p.Status != PaymentStatus.Rejected),
                TotalVerifiedAmount = await allPayments
                    .Where(p => p.Status == PaymentStatus.Verified)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0m
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
                    && payment.Status != PaymentStatus.Verified
                    && payment.Status != PaymentStatus.Rejected,
                DueDate = payment.DueDate,
                SubmittedDate = payment.PaymentDate,
                VerifiedDate = payment.Status == PaymentStatus.Verified ? payment.UpdatedAt : null,
                PaymentMethod = payment.PaymentMethod,
                ReferenceNo = payment.ReferenceNo,
                ReceiptFileUrl = !string.IsNullOrWhiteSpace(payment.ReceiptFileKey)
                    ? "/" + payment.ReceiptFileKey.TrimStart('/')
                    : null,
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
                    (p.Property.Landlord != null && p.Property.Landlord.Email.Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "All")
            {
                if (filter.Status == "Overdue")
                {
                    query = query.Where(p =>
                        p.DueDate < DateTime.UtcNow &&
                        p.Status != PaymentStatus.Verified &&
                        p.Status != PaymentStatus.Rejected);
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
                    ReferenceNo = p.ReferenceNo ?? ""
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
            sb.AppendLine("Payment ID,Tenant Email,Property Name,Landlord Email,Amount,Status,Payment Period,Due Date,Submitted Date,Verified Date,Payment Method,Reference No");

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
                    CsvEscape(r.ReferenceNo)));
            }

            var fileName = $"payment-report-{DateTime.UtcNow:yyyy-MM-dd}.csv";
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", fileName);
        }

        private IActionResult RedirectToDetail(int id, string? returnUrl)
        {
            return RedirectToAction(nameof(Detail), new { id, returnUrl });
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
