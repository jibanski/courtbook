using System.Diagnostics;
using CourtBooking.Data;
using CourtBooking.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;

    public HomeController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        // Unauthenticated visitors see the marketing landing page
        if (User.Identity?.IsAuthenticated != true)
            return View("Landing");

        // Admins go directly to their dashboard
        if (User.IsInRole("Admin"))
            return RedirectToAction("Index", "Admin");

        // Customers see the courts listing
        var courts = await _db.Courts.Where(c => c.IsActive).ToListAsync();
        return View(courts);
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
