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
        // Fetch updates where the EndDate is in the future, sorted newest first
        var activeUpdates = await _context.CommunityUpdates
            .Where(u => u.EndDate >= DateTime.UtcNow)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        // Pass the live data to the landing page
        return View(activeUpdates);
    }

    [AllowAnonymous]
    public async Task<IActionResult> UpdateDetails(int id)
    {
        // Find the specific event the user clicked on
        var update = await _context.CommunityUpdates.FindAsync(id);
        
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