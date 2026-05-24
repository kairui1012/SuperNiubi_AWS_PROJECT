using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyMvcApp.Data;
using MyMvcApp.Models;

namespace MyMvcApp.Controllers;

/// <summary>
/// Serves public home, community update details, privacy, and error pages.
/// </summary>
public class HomeController : Controller
{
    /// <summary>
    /// Writes diagnostics for public home page requests.
    /// </summary>
    private readonly ILogger<HomeController> _logger;

    /// <summary>
    /// Provides access to public community update data.
    /// </summary>
    private readonly AppDbContext _context;

    /// <summary>
    /// Creates a controller instance with logging and application data services.
    /// </summary>
    public HomeController(ILogger<HomeController> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    /// <summary>
    /// Shows the public landing page with active community updates.
    /// </summary>
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        // Fetch updates where the EndDate is in the future, sorted newest first
        var activeUpdates = await _context.CommunityUpdates
            .Where(u => u.EndDate >= DateTime.UtcNow)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        // Pass the live data to the landing page
        return View(activeUpdates);
    }

    /// <summary>
    /// Shows the details for one public community update.
    /// </summary>
    [AllowAnonymous]
    public async Task<IActionResult> UpdateDetails(int id)
    {
        // Load the selected public community update.
        var update = await _context.CommunityUpdates.FindAsync(id);
        
        if (update == null) 
        {
            return NotFound();
        }
        
        return View(update);
    }

    /// <summary>
    /// Shows the privacy page.
    /// </summary>
    [AllowAnonymous]
    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>
    /// Shows the error page with the current request identifier.
    /// </summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
