using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;
using MyMvcApp.Services;

namespace MyMvcApp.Controllers
{
    /// <summary>
    /// Allows admins to manage community updates and event announcements.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class CommunityAdminController : Controller
    {
        /// <summary>
        /// Provides access to community update records.
        /// </summary>
        private readonly AppDbContext _context;

        /// <summary>
        /// Uploads community update images to S3.
        /// </summary>
        private readonly IS3ImageService _s3Service;

        /// <summary>
        /// Creates a controller instance with database and image upload services.
        /// </summary>
        public CommunityAdminController(AppDbContext context, IS3ImageService s3Service)
        {
            _context = context;
            _s3Service = s3Service;
        }

        /// <summary>
        /// Lists all community updates for admin management.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            var updates = await _context.CommunityUpdates
                                        .OrderByDescending(u => u.CreatedAt)
                                        .ToListAsync();
            return View(updates);
        }

        /// <summary>
        /// Shows the form for creating a new community update.
        /// </summary>
        public IActionResult Create()
        {
            return View();
        }

        /// <summary>
        /// Creates a community update and optionally uploads its image to S3.
        /// </summary>
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
                
                // Normalize event dates to UTC before saving.
                if (model.EventStartDate.HasValue) model.EventStartDate = DateTime.SpecifyKind(model.EventStartDate.Value, DateTimeKind.Utc);
                if (model.EventEndDate.HasValue) model.EventEndDate = DateTime.SpecifyKind(model.EventEndDate.Value, DateTimeKind.Utc);

                _context.CommunityUpdates.Add(model);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Community update published successfully!";
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        /// <summary>
        /// Shows the edit form for an existing community update.
        /// </summary>
        public async Task<IActionResult> Edit(int id)
        {
            var update = await _context.CommunityUpdates.FindAsync(id);
            if (update == null) return NotFound();
            return View(update);
        }

        /// <summary>
        /// Updates an existing community update and replaces its image when a new file is provided.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CommunityUpdate model, IFormFile? ImageFile)
        {
            if (id != model.Id) return NotFound();

            if (ModelState.IsValid)
            {
                var existingUpdate = await _context.CommunityUpdates.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
                if (existingUpdate == null) return NotFound();

                // Replace the existing image only when a new image file is provided.
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
                    model.ImageUrl = existingUpdate.ImageUrl;
                }

                // Preserve original creation metadata while editing.
                model.CreatedAt = existingUpdate.CreatedAt;

                // Normalize edited event dates to UTC before saving.
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

        /// <summary>
        /// Deletes a community update.
        /// </summary>
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
