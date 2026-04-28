using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;

namespace MyMvcApp.Controllers
{
    public class FacilityGuardController : Controller
    {
        private readonly AppDbContext _context;

        public FacilityGuardController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /FacilityGuard/Verify?code=ABC12345
        public async Task<IActionResult> Verify(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return View(); // Show the empty search form
            }

            code = code.Trim().ToUpper();
            
            var booking = await _context.FacilityBookings
                .Include(b => b.Facility)
                .Include(b => b.AppUser)
                .FirstOrDefaultAsync(b => b.PassCode == code);

            if (booking == null)
            {
                ViewBag.Status = "Invalid";
                ViewBag.Message = "No booking found with this pass code.";
                return View();
            }

            if (booking.Status != BookingStatus.Confirmed)
            {
                ViewBag.Status = "Invalid";
                ViewBag.Message = $"This booking is marked as {booking.Status}. Payment may not be complete.";
                return View(booking);
            }

            // --- Time Validation Logic ---
            // Note: Adjust DateTime.Now to your preferred timezone if necessary
            var currentDateTime = DateTime.Now; 
            var bookingDate = booking.BookingDate.Date;
            var currentTime = currentDateTime.TimeOfDay;

            if (currentDateTime.Date < bookingDate)
            {
                ViewBag.Status = "Early";
                ViewBag.Message = $"This pass is for a future date: {bookingDate:dd MMM yyyy}.";
                return View(booking);
            }
            
            if (currentDateTime.Date > bookingDate)
            {
                ViewBag.Status = "Expired";
                ViewBag.Message = $"This pass expired on {bookingDate:dd MMM yyyy}.";
                return View(booking);
            }

            // It is today. Allow 15 minutes early entry grace period.
            var earlyEntryTime = booking.StartTime.Subtract(TimeSpan.FromMinutes(15));
            
            if (currentTime < earlyEntryTime)
            {
                ViewBag.Status = "Early";
                ViewBag.Message = $"Too early. Facility access begins at {booking.StartTime:hh\\:mm}.";
                return View(booking);
            }

            if (currentTime > booking.EndTime)
            {
                ViewBag.Status = "Expired";
                ViewBag.Message = $"This pass expired today at {booking.EndTime:hh\\:mm}.";
                return View(booking);
            }

            // If it passes all checks, grant access
            ViewBag.Status = "Valid";
            ViewBag.Message = "Access Granted.";
            return View(booking);
        }
    }
}