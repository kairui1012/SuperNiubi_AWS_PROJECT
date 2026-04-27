using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using MyMvcApp.Services;

namespace MyMvcApp.Controllers
{
    [Authorize(Roles = "Admin")] // Ensures only Admins can access this
    public class CommunityAdminController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IS3ImageService _s3Service;

        public CommunityAdminController(AppDbContext context, IS3ImageService s3Service)
        {
            _context = context;
            _s3Service = s3Service;
        }

        // GET: /CommunityAdmin/
        public async Task<IActionResult> Index()
        {
            // Fetch all updates, ordered by the newest first
            var updates = await _context.CommunityUpdates
                                        .OrderByDescending(u => u.CreatedAt)
                                        .ToListAsync();
            return View(updates);
        }

        // GET: /CommunityAdmin/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /CommunityAdmin/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CommunityUpdate model, IFormFile? ImageFile)
        {
            if (ModelState.IsValid)
            {
                // 1. Handle S3 Image Upload if a file was selected
                if (ImageFile != null && ImageFile.Length > 0)
                {
                    try
                    {
                        model.ImageUrl = await _s3Service.UploadImageAsync(ImageFile);
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError("", $"Image upload failed: {ex.Message}");
                        return View(model);
                    }
                }

                // 2. Save to Database
                model.CreatedAt = DateTime.UtcNow;
                model.EndDate = DateTime.SpecifyKind(model.EndDate, DateTimeKind.Utc);
                _context.CommunityUpdates.Add(model);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Community update published successfully!";
                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        // POST: /CommunityAdmin/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var update = await _context.CommunityUpdates.FindAsync(id);
            if (update != null)
            {
                _context.CommunityUpdates.Remove(update);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Update deleted successfully.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}