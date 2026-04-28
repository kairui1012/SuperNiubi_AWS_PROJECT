using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;
using MyMvcApp.Data;
using MyMvcApp.Models;

namespace MyMvcApp.Controllers
{
    public class PropertyBookingController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public PropertyBookingController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<IActionResult> Index()
        {
            // Put the strict rules back!
            var properties = await _context.Properties
                .Where(p => p.AllowShortTerm && p.DailyRate.HasValue && p.AvailabilityStatus == PropertyAvailabilityStatus.Available)
                .ToListAsync();
                
            return View(properties);
        }

        public async Task<IActionResult> Book(int id)
        {
            var property = await _context.Properties.FindAsync(id);
            if (property == null || !property.AllowShortTerm || property.AvailabilityStatus != PropertyAvailabilityStatus.Available)
                return NotFound();

            return View(property); 
        }

        [HttpPost]
        public async Task<IActionResult> CreateCheckoutSession(int propertyId, DateTime checkInDate, DateTime checkOutDate, string? promoCode, string guestName, string guestEmail, string guestPhone)
        {
            var utcCheckIn = DateTime.SpecifyKind(checkInDate.Date, DateTimeKind.Utc);
            var utcCheckOut = DateTime.SpecifyKind(checkOutDate.Date, DateTimeKind.Utc);

            var property = await _context.Properties.FindAsync(propertyId);
            if (property == null || property.DailyRate == null) return BadRequest();

            if (utcCheckOut <= utcCheckIn)
            {
                TempData["ErrorMessage"] = "Check-out date must be after Check-in date.";
                return RedirectToAction(nameof(Book), new { id = propertyId });
            }

            // Check for date overlaps. 
            // Note: Guest A checking out on the 5th and Guest B checking in on the 5th is ALLOWED.
            bool hasOverlap = await _context.PropertyBookings.AnyAsync(b =>
                b.PropertyId == propertyId &&
                b.Status != BookingStatus.Cancelled &&
                (b.CheckInDate < utcCheckOut && b.CheckOutDate > utcCheckIn) 
            );

            if (hasOverlap)
            {
                TempData["ErrorMessage"] = "Those dates are already booked.";
                return RedirectToAction(nameof(Book), new { id = propertyId });
            }

            // Calculate Nights and Price
            int nights = (utcCheckOut - utcCheckIn).Days;
            decimal totalAmount = property.DailyRate.Value * nights;
            decimal discountAmount = 0;
            int? appliedPromoId = null;

            if (!string.IsNullOrWhiteSpace(promoCode))
            {
                var promo = await _context.PromoCodes.FirstOrDefaultAsync(p => p.Code.ToUpper() == promoCode.ToUpper() && p.IsActive);
                if (promo != null)
                {
                    appliedPromoId = promo.Id;
                    discountAmount = promo.DiscountPercentage.HasValue 
                        ? totalAmount * (promo.DiscountPercentage.Value / 100m) : (promo.FlatDiscount ?? 0);
                }
            }

            decimal finalAmount = Math.Max(0, totalAmount - discountAmount);

            var booking = new PropertyBooking
            {
                PropertyId = propertyId,
                GuestName = guestName,
                GuestEmail = guestEmail,
                GuestPhone = guestPhone,
                CheckInDate = utcCheckIn,
                CheckOutDate = utcCheckOut,
                PromoCodeId = appliedPromoId,
                TotalAmount = totalAmount,
                DiscountAmount = discountAmount,
                FinalAmount = finalAmount
            };

            _context.PropertyBookings.Add(booking);
            await _context.SaveChangesAsync();

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
                            UnitAmount = (long)(finalAmount * 100), 
                            Currency = "myr",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"Stay at {property.PropertyName}",
                                Description = $"{nights} Nights ({utcCheckIn:dd MMM} to {utcCheckOut:dd MMM})"
                            },
                        },
                        Quantity = 1,
                    },
                },
                Mode = "payment",
                SuccessUrl = $"{domain}/PropertyBooking/Success", // We will build views later
                CancelUrl = $"{domain}/PropertyBooking/Cancel",
                Metadata = new Dictionary<string, string> { { "TransactionType", "PropertyBooking" }, { "BookingId", booking.Id.ToString() } }
            };

            var service = new SessionService();
            Session session = await service.CreateAsync(options);
            booking.StripeSessionId = session.Id;
            await _context.SaveChangesAsync();

            return Redirect(session.Url);
        }
    }
}