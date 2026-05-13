using System.Diagnostics;
using System.Security.Claims;
using CourtBooking.Data;
using CourtBooking.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db          = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        // Unauthenticated visitors see the marketing landing page, including
        // any approved + featured testimonials from real facility owners.
        if (User.Identity?.IsAuthenticated != true)
        {
            var featured = await _db.Reviews
                .Where(r => r.IsApproved && r.IsFeatured)
                .OrderBy(r => r.DisplayOrder)
                .ThenByDescending(r => r.SubmittedAt)
                .Take(6)
                .ToListAsync();

            return View("Landing", featured);
        }

        // Admins go directly to their dashboard
        if (User.IsInRole("Admin"))
            return RedirectToAction("Index", "Admin");

        // Customers: always send them to their preferred facility if one is set
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId != null)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (!string.IsNullOrEmpty(user?.PreferredFacilitySlug))
                return RedirectToAction("Index", "Facility", new { slug = user.PreferredFacilitySlug });
        }

        // No preferred facility — show the generic courts browser
        return RedirectToAction("Index", "Courts");
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
