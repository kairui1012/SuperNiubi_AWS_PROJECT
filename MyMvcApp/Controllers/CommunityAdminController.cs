using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using MyMvcApp.Services;

namespace MyMvcApp.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CommunityAdminController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IS3ImageService _s3Service;

        public CommunityAdminController(AppDbContext context, IS3ImageService s3Service)
        {
            _context = context;
            _s3Service = s3Service;
        }

        public async Task<IActionResult> Index()
        {
            var updates = await _context.CommunityUpdates
                                        .OrderByDescending(u => u.CreatedAt)
                                        .ToListAsync();
            return View(updates);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CommunityUpdate model, IFormFile? ImageFile)
        {
            if (ModelState.IsValid)
            {
                if (ImageFile != null && ImageFile.Length > 0)
                {
                    try { model.ImageUrl = await _s3Service.UploadImageAsync(ImageFile); }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError("", $"Image upload failed: {ex.Message}");
                        return View(model);
                    }
                }

                model.CreatedAt = DateTime.UtcNow;
                model.EndDate = DateTime.SpecifyKind(model.EndDate, DateTimeKind.Utc);
                
                // Convert new dates to UTC if they were provided
                if (model.EventStartDate.HasValue) model.EventStartDate = DateTime.SpecifyKind(model.EventStartDate.Value, DateTimeKind.Utc);
                if (model.EventEndDate.HasValue) model.EventEndDate = DateTime.SpecifyKind(model.EventEndDate.Value, DateTimeKind.Utc);

                _context.CommunityUpdates.Add(model);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Community update published successfully!";
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        // GET: /CommunityAdmin/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var update = await _context.CommunityUpdates.FindAsync(id);
            if (update == null) return NotFound();
            return View(update);
        }

        // POST: /CommunityAdmin/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CommunityUpdate model, IFormFile? ImageFile)
        {
            if (id != model.Id) return NotFound();

            if (ModelState.IsValid)
            {
                var existingUpdate = await _context.CommunityUpdates.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
                if (existingUpdate == null) return NotFound();

                // Handle Image Update
                if (ImageFile != null && ImageFile.Length > 0)
                {
                    try { model.ImageUrl = await _s3Service.UploadImageAsync(ImageFile); }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError("", $"Image upload failed: {ex.Message}");
                        return View(model);
                    }
                }
                else
                {
                    model.ImageUrl = existingUpdate.ImageUrl; // Keep old image
                }

                // Preserve creation date
                model.CreatedAt = existingUpdate.CreatedAt;

                // Convert dates to UTC
                model.EndDate = DateTime.SpecifyKind(model.EndDate, DateTimeKind.Utc);
                if (model.EventStartDate.HasValue) model.EventStartDate = DateTime.SpecifyKind(model.EventStartDate.Value, DateTimeKind.Utc);
                if (model.EventEndDate.HasValue) model.EventEndDate = DateTime.SpecifyKind(model.EventEndDate.Value, DateTimeKind.Utc);

                _context.Update(model);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Update modified successfully!";
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

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