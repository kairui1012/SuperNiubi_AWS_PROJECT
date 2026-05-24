using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;

namespace MyMvcApp.Controllers
{
    /// <summary>
    /// Allows guards to verify short-term booking access passes.
    /// </summary>
    public class PropertyGuardController : Controller
    {
        /// <summary>
        /// Provides access to property booking pass records.
        /// </summary>
        private readonly AppDbContext _context;

        /// <summary>
        /// Creates a controller instance with the application database context.
        /// </summary>
        public PropertyGuardController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Validates a guest access pass code and reports whether access is currently allowed.
        /// </summary>
        public async Task<IActionResult> Verify(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return View(); 

            code = code.Trim().ToUpper();
            var booking = await _context.PropertyBookings
                .Include(b => b.Property)
                .FirstOrDefaultAsync(b => b.PassCode == code);

            if (booking == null)
            {
                ViewBag.Status = "Invalid";
                ViewBag.Message = "No booking found.";
                return View();
            }

            if (booking.Status != BookingStatus.Confirmed)
            {
                ViewBag.Status = "Invalid";
                ViewBag.Message = "Payment not completed.";
                return View(booking);
            }

            var now = DateTime.Now; 
            // Guests can enter only after 3:00 PM on the arrival date.
            var checkInTime = booking.CheckInDate.Date.AddHours(15); 
            // Guest access expires at 11:00 AM on the departure date.
            var checkOutTime = booking.CheckOutDate.Date.AddHours(11); 

            if (now < checkInTime)
            {
                ViewBag.Status = "Early";
                ViewBag.Message = $"Check-in begins at 3:00 PM on {booking.CheckInDate:dd MMM yyyy}.";
                return View(booking);
            }
            
            if (now > checkOutTime)
            {
                ViewBag.Status = "Expired";
                ViewBag.Message = $"This stay expired at 11:00 AM on {booking.CheckOutDate:dd MMM yyyy}.";
                return View(booking);
            }

            // The pass is valid for the current stay window.
            ViewBag.Status = "Valid";
            ViewBag.Message = "Active Guest. Full Access Granted.";
            return View(booking);
        }
    }
}
