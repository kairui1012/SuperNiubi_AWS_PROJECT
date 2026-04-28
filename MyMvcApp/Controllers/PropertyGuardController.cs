using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;

namespace MyMvcApp.Controllers
{
    public class PropertyGuardController : Controller
    {
        private readonly AppDbContext _context;

        public PropertyGuardController(AppDbContext context)
        {
            _context = context;
        }

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
            // Strict check-in at 3:00 PM on arrival date
            var checkInTime = booking.CheckInDate.Date.AddHours(15); 
            // Strict check-out at 11:00 AM on departure date
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

            // Valid for the entire duration!
            ViewBag.Status = "Valid";
            ViewBag.Message = "Active Guest. Full Access Granted.";
            return View(booking);
        }
    }
}