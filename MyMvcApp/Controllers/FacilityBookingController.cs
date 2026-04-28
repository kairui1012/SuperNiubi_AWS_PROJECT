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
            var facility = await _context.Facilities.FindAsync(facilityId);
            if (facility == null) return BadRequest("Facility not found.");

            // 1. Availability Check (First-Come, First-Serve logic)
            // Ensures the new requested timeslot does not overlap with any Pending or Confirmed bookings
            bool hasOverlap = await _context.FacilityBookings.AnyAsync(b =>
                b.FacilityId == facilityId &&
                b.BookingDate.Date == bookingDate.Date &&
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
                        ? totalAmount * (promo.DiscountPercentage.Value / 100) 
                        : (promo.FlatDiscount ?? 0);
                }
            }

            decimal finalAmount = Math.Max(0, totalAmount - discountAmount);

            // 3. Identify User Role vs Visitor
            int? currentUserId = null;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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
                BookingDate = bookingDate.Date,
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
            // For propease.dev deployment, ensure DOMAIN is set in appsettings.json
            var domain = _configuration["Domain"] ?? $"https://{Request.Host}"; 
            
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card", "fpx" }, 
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(finalAmount * 100), // Stripe processes in cents
                            Currency = "myr",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"Facility Booking: {facility.Name}",
                                Description = $"{bookingDate:dd MMM yyyy} ({startTime:hh\\:mm} - {endTime:hh\\:mm})"
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

            var service = new SessionService();
            Session session = await service.CreateAsync(options);

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