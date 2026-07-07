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
    private readonly IConfiguration _config;

    public HomeController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IConfiguration config)
    {
        _db          = db;
        _userManager = userManager;
        _config      = config;
    }

    public async Task<IActionResult> Index()
    {
        // Unauthenticated visitors see the marketing landing page, including
        // any approved + featured testimonials from real facility owners.
        if (User.Identity?.IsAuthenticated != true)
        {
            // Clear any lingering facility cookie so users who navigate to the
            // main CourtBook site (rather than a facility's shared link) are not
            // silently pinned to the last facility they visited.
            if (Request.Cookies.ContainsKey("facilitySlug"))
                Response.Cookies.Delete("facilitySlug");

            var featured = await _db.Reviews
                .Where(r => r.IsApproved && r.IsFeatured)
                .OrderBy(r => r.DisplayOrder)
                .ThenByDescending(r => r.SubmittedAt)
                .Take(6)
                .ToListAsync();

            // Onboarded clients: publicly-listed facilities (have a slug, not
            // suspended, and have set their own name) shown as a logo wall.
            ViewBag.Clients = await _db.FacilitySettings
                .Where(f => f.Slug != null
                            && !f.IsSuspended
                            && f.FacilityName != "CourtBook")
                .OrderBy(f => f.FacilityName)
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
    public IActionResult About()   => View();
    public IActionResult Terms()   => View();
    public async Task<IActionResult> Donate()
    {
        var sub = _config.GetSection("Subscription");
        ViewBag.GCashNumber  = sub["GCashNumber"];
        ViewBag.GCashName    = sub["GCashName"];
        ViewBag.MayaNumber   = sub["MayaNumber"];
        ViewBag.MayaName     = sub["MayaName"];
        ViewBag.ContactEmail = sub["ContactEmail"];

        ViewBag.MetrobankAccountNumber = sub["MetrobankAccountNumber"];
        ViewBag.MetrobankAccountName   = sub["MetrobankAccountName"];
        ViewBag.BpiAccountNumber       = sub["BpiAccountNumber"];
        ViewBag.BpiAccountName         = sub["BpiAccountName"];

        var cfg = await _db.PlatformConfig.FindAsync(1);
        ViewBag.GCashQrSrc     = cfg?.GCashQrData     is { Length: > 0 } ? $"data:{cfg.GCashQrContentType};base64,{Convert.ToBase64String(cfg.GCashQrData)}"         : null;
        ViewBag.MayaQrSrc      = cfg?.MayaQrData      is { Length: > 0 } ? $"data:{cfg.MayaQrContentType};base64,{Convert.ToBase64String(cfg.MayaQrData)}"           : null;
        ViewBag.MetrobankQrSrc = cfg?.MetrobankQrData  is { Length: > 0 } ? $"data:{cfg.MetrobankQrContentType};base64,{Convert.ToBase64String(cfg.MetrobankQrData)}" : null;
        ViewBag.BpiQrSrc       = cfg?.BpiQrData        is { Length: > 0 } ? $"data:{cfg.BpiQrContentType};base64,{Convert.ToBase64String(cfg.BpiQrData)}"             : null;

        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
