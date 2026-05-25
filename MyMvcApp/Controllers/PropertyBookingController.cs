using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;
using MyMvcApp.Data;
using MyMvcApp.Models;
using MyMvcApp.Services;

namespace MyMvcApp.Controllers
{
    /// <summary>
    /// Handles short-term property booking, Stripe checkout, and guest access email testing.
    /// </summary>
    public class PropertyBookingController : Controller
    {
        /// <summary>
        /// Provides access to property and booking records.
        /// </summary>
        private readonly AppDbContext _context;

        /// <summary>
        /// Reads Stripe redirect domain and booking configuration values.
        /// </summary>
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Sends property access pass emails for booking tests.
        /// </summary>
        private readonly MyMvcApp.Services.EmailService _emailService;

        /// <summary>
        /// Creates a controller instance with booking data, configuration, and email services.
        /// </summary>
        public PropertyBookingController(AppDbContext context, IConfiguration configuration, MyMvcApp.Services.EmailService emailService)
        {
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
        }

        /// <summary>
        /// Lists available properties that can be booked for short-term stays.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            var properties = await GetBookableStayProperties(_context.Properties.AsNoTracking())
                .ToListAsync();
                
            return View(properties);
        }

        /// <summary>
        /// Shows the booking page for an available short-term property.
        /// </summary>
        public async Task<IActionResult> Book(int id)
        {
            var property = await GetBookableStayProperties(_context.Properties.AsNoTracking())
                .FirstOrDefaultAsync(p => p.PropertyId == id);

            if (property == null)
                return NotFound();

            return View(property); 
        }

        /// <summary>
        /// Creates a pending booking and redirects the guest to Stripe Checkout for payment.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateCheckoutSession(int propertyId, DateTime checkInDate, DateTime checkOutDate, string? promoCode, string guestName, string guestEmail, string guestPhone)
        {
            var utcCheckIn = DateTime.SpecifyKind(checkInDate.Date, DateTimeKind.Utc);
            var utcCheckOut = DateTime.SpecifyKind(checkOutDate.Date, DateTimeKind.Utc);

            var property = await GetBookableStayProperties(_context.Properties)
                .FirstOrDefaultAsync(p => p.PropertyId == propertyId);

            if (property == null)
            {
                return BadRequest();
            }

            if (utcCheckOut <= utcCheckIn)
            {
                TempData["ErrorMessage"] = "Check-out date must be after Check-in date.";
                return RedirectToAction(nameof(Book), new { id = propertyId });
            }

            // Block confirmed bookings and recent pending checkout locks.
            var fifteenMinsAgo = DateTime.UtcNow.AddMinutes(-15);
            
            bool hasOverlap = await _context.PropertyBookings.AnyAsync(b =>
                b.PropertyId == propertyId &&
                (b.Status == BookingStatus.Confirmed || (b.Status == BookingStatus.Pending && b.CreatedAt > fifteenMinsAgo)) &&
                (b.CheckInDate < utcCheckOut && b.CheckOutDate > utcCheckIn) 
            );

            if (hasOverlap)
            {
                TempData["ErrorMessage"] = "Those dates are already booked or are currently locked by another user checking out. Please try again in 15 minutes.";
                return RedirectToAction(nameof(Book), new { id = propertyId });
            }

            // Calculate stay duration, discount, and final checkout amount.
            int nights = (utcCheckOut - utcCheckIn).Days;
            var dailyRate = property.DailyRate.GetValueOrDefault();
            decimal totalAmount = dailyRate * nights;
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
                FinalAmount = finalAmount,
                Status = BookingStatus.Pending,
                PaymentStatus = BookingPaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _context.PropertyBookings.Add(booking);
            await _context.SaveChangesAsync();

            // Build Stripe redirect URLs from the configured domain or current request host.
            var domain = _configuration["Domain"] ?? $"{Request.Scheme}://{Request.Host}"; 
            
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
                SuccessUrl = $"{domain}/PropertyBooking/Success", 
                CancelUrl = $"{domain}/PropertyBooking/Cancel",
                Metadata = new Dictionary<string, string> { { "TransactionType", "PropertyBooking" }, { "BookingId", booking.Id.ToString() } }
            };

            var service = new SessionService();
            Session session = await service.CreateAsync(options);
            booking.StripeSessionId = session.Id;
            await _context.SaveChangesAsync();

            return Redirect(session.Url);
        }

        /// <summary>
        /// Applies the public short-term booking eligibility rules.
        /// </summary>
        private static IQueryable<Property> GetBookableStayProperties(IQueryable<Property> properties)
        {
            var utcNow = DateTime.UtcNow;
            var currentMonthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var nextMonthStart = currentMonthStart.AddMonths(1);

            return properties.Where(p => !p.IsDeleted
                && p.AllowShortTerm
                && p.DailyRate.HasValue
                && p.DailyRate.Value > 0
                && p.AvailabilityStatus == PropertyAvailabilityStatus.Available
                && !p.Tenants.Any(t => t.LeaseStatus == LeaseStatus.Active
                    && t.Payments.Any(payment =>
                        payment.Status == PaymentStatus.Verified
                        && payment.PaymentDate.HasValue
                        && payment.PaymentDate.Value >= currentMonthStart
                        && payment.PaymentDate.Value < nextMonthStart)));
        }

        /// <summary>
        /// Shows the booking success page after Stripe redirects back.
        /// </summary>
        public IActionResult Success()
        {
            return View();
        }

        /// <summary>
        /// Shows the booking cancellation page after Stripe checkout is cancelled.
        /// </summary>
        public IActionResult Cancel()
        {
            return View();
        }

        /// <summary>
        /// Sends a test property access pass email for a booking.
        /// </summary>
        public async Task<IActionResult> ForceTestEmail(int id)
        {
            // Load the booking and property for the access pass email.
            var booking = await _context.PropertyBookings
                .Include(b => b.Property)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return Content("Booking not found. Check the ID.");

            // Use a fallback test pass code when the booking does not have one yet.
            booking.PassCode ??= "TEST1234";

            try
            {
                // Send the property access pass using the same email service as production bookings.
                await _emailService.SendPropertyAccessPassAsync(booking.GuestEmail, booking, booking.PassCode);
                return Content("SUCCESS! Email triggered. Check your terminal for [QR DEBUG] and check your inbox.");
            }
            catch (Exception ex)
            {
                return Content($"FAILED: {ex.Message}");
            }
        }
    }
}
