using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;
using System.Security.Claims;
using MyMvcApp.Data;
using MyMvcApp.Models;

namespace MyMvcApp.Controllers
{
    public class FacilityBookingController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public FacilityBookingController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // GET: /FacilityBooking
        // Displays available facilities. Visitors only see public ones.
        public async Task<IActionResult> Index()
        {
            var facilities = await _context.Facilities
                .Where(f => f.IsPubliclyAvailable || User.Identity.IsAuthenticated)
                .ToListAsync();
            return View(facilities);
        }

        // GET: /FacilityBooking/Book/5
        public async Task<IActionResult> Book(int id)
        {
            var facility = await _context.Facilities.FindAsync(id);
            if (facility == null) return NotFound();

            return View(facility); 
        }

        // POST: /FacilityBooking/CreateCheckoutSession
        [HttpPost]
        public async Task<IActionResult> CreateCheckoutSession(int facilityId, DateTime bookingDate, TimeSpan startTime, TimeSpan endTime, string? promoCode, string? guestName, string? guestEmail, string? guestPhone)
        {
            // --- FIX 1: PostgreSQL requires DateTime to explicitly be UTC ---
            var utcBookingDate = DateTime.SpecifyKind(bookingDate.Date, DateTimeKind.Utc);

            var facility = await _context.Facilities.FindAsync(facilityId);
            if (facility == null) return BadRequest("Facility not found.");

            // --- FIX 2: Enforce 30-minute granularity (Backend Validation) ---
            if (startTime.Minutes % 30 != 0 || endTime.Minutes % 30 != 0)
            {
                TempData["ErrorMessage"] = "Bookings must be in 30-minute intervals (e.g., 14:00, 14:30).";
                return RedirectToAction(nameof(Book), new { id = facilityId });
            }

            if (endTime <= startTime)
            {
                TempData["ErrorMessage"] = "End time must be after start time.";
                return RedirectToAction(nameof(Book), new { id = facilityId });
            }

            // 1. Availability Check
            bool hasOverlap = await _context.FacilityBookings.AnyAsync(b =>
                b.FacilityId == facilityId &&
                b.BookingDate == utcBookingDate && // <-- Use the UTC variable here
                b.Status != BookingStatus.Cancelled &&
                (b.StartTime < endTime && b.EndTime > startTime) 
            );

            if (hasOverlap)
            {
                TempData["ErrorMessage"] = "The selected time slot is already booked.";
                return RedirectToAction(nameof(Book), new { id = facilityId });
            }

            // 2. Calculate Final Price
            double durationHours = (endTime - startTime).TotalHours;
            if (durationHours <= 0) return BadRequest("Invalid time range.");

            decimal totalAmount = facility.HourlyRate * (decimal)durationHours;
            decimal discountAmount = 0;
            int? appliedPromoId = null;

            if (!string.IsNullOrWhiteSpace(promoCode))
            {
                var promo = await _context.PromoCodes
                    .FirstOrDefaultAsync(p => p.Code.ToUpper() == promoCode.ToUpper() && p.IsActive && p.ValidFrom <= DateTime.UtcNow && p.ValidUntil >= DateTime.UtcNow);

                if (promo != null)
                {
                    appliedPromoId = promo.Id;
                    discountAmount = promo.DiscountPercentage.HasValue 
                        ? totalAmount * (promo.DiscountPercentage.Value / 100m) // Add 'm' for decimal
                        : (promo.FlatDiscount ?? 0);
                }
            }

            decimal finalAmount = Math.Max(0, totalAmount - discountAmount);

            // 3. Identify User Role vs Visitor
            int? currentUserId = null;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(userIdClaim, out int uid)) currentUserId = uid;
            }

            // 4. Record Pending Transaction
            var booking = new FacilityBooking
            {
                FacilityId = facilityId,
                AppUserId = currentUserId,
                GuestName = currentUserId == null ? guestName : null,
                GuestEmail = currentUserId == null ? guestEmail : null,
                GuestPhone = currentUserId == null ? guestPhone : null,
                BookingDate = utcBookingDate, // <-- Use the UTC variable here
                StartTime = startTime,
                EndTime = endTime,
                PromoCodeId = appliedPromoId,
                TotalAmount = totalAmount,
                DiscountAmount = discountAmount,
                FinalAmount = finalAmount,
                Status = BookingStatus.Pending,
                PaymentStatus = BookingPaymentStatus.Pending
            };

            _context.FacilityBookings.Add(booking);
            await _context.SaveChangesAsync();

            // 5. Generate Stripe Checkout
            var domain = _configuration["Domain"] ?? $"https://{Request.Host}"; 
            
            var options = new Stripe.Checkout.SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card", "fpx" }, 
                LineItems = new List<Stripe.Checkout.SessionLineItemOptions>
                {
                    new Stripe.Checkout.SessionLineItemOptions
                    {
                        PriceData = new Stripe.Checkout.SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(finalAmount * 100), 
                            Currency = "myr",
                            ProductData = new Stripe.Checkout.SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"Facility Booking: {facility.Name}",
                                Description = $"{utcBookingDate:dd MMM yyyy} ({startTime:hh\\:mm} - {endTime:hh\\:mm})"
                            },
                        },
                        Quantity = 1,
                    },
                },
                Mode = "payment",
                SuccessUrl = $"{domain}/FacilityBooking/Success?bookingId={booking.Id}",
                CancelUrl = $"{domain}/FacilityBooking/Cancel?bookingId={booking.Id}",
                Metadata = new Dictionary<string, string>
                {
                    { "TransactionType", "FacilityBooking" },
                    { "BookingId", booking.Id.ToString() }
                }
            };

            var service = new Stripe.Checkout.SessionService();
            Stripe.Checkout.Session session = await service.CreateAsync(options);

            booking.StripeSessionId = session.Id;
            await _context.SaveChangesAsync();

            return Redirect(session.Url);
        }

        public IActionResult Success(int bookingId)
        {
            return View(); // We will build this UI next
        }

        public IActionResult Cancel(int bookingId)
        {
            return View(); 
        }
    }
}