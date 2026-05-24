using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;

namespace MyMvcApp.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AppDbContext _context;

    // We inject BOTH the Logger (from your original code) and the AppDbContext
    public HomeController(ILogger<HomeController> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        List<CommunityUpdate> activeUpdates;

        try
        {
            activeUpdates = await _context.CommunityUpdates
                .Where(u => u.EndDate >= DateTime.UtcNow)
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load community updates because the database is unavailable.");
            ViewBag.DatabaseUnavailable = true;
            activeUpdates = new List<CommunityUpdate>();
        }

        return View(activeUpdates);
    }

    [AllowAnonymous]
    public async Task<IActionResult> UpdateDetails(int id)
    {
        CommunityUpdate? update;

        try
        {
            update = await _context.CommunityUpdates.FindAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load community update {UpdateId} because the database is unavailable.", id);
            TempData["ErrorMessage"] = "Community updates are temporarily unavailable. Please try again shortly.";
            return RedirectToAction(nameof(Index));
        }
        
        if (update == null) 
        {
            return NotFound();
        }
        
        return View(update);
    }

    [AllowAnonymous]
    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
