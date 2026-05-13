using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CourtBooking.Controllers;

/// <summary>
/// Facility owners submit testimonials about CourtBook here. Submissions go
/// to the platform admin's moderation queue at /Dev/Reviews. Approved +
/// featured ones appear on the public landing page.
/// </summary>
[Authorize(Roles = "Admin")]
public class ReviewsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReviewsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db          = db;
        _userManager = userManager;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET /Reviews
    public async Task<IActionResult> Index()
    {
        var mine = await _db.Reviews
            .Where(r => r.OwnerId == CurrentUserId)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync();

        return View(mine);
    }

    // GET /Reviews/Create
    public IActionResult Create() => View(new ReviewSubmitViewModel());

    // POST /Reviews/Create
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReviewSubmitViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByIdAsync(CurrentUserId);
        if (user is null) return Forbid();

        var settings = await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == CurrentUserId);
        var facilityName = settings?.FacilityName ?? "(facility name not set)";

        var ownerName = $"{user.FirstName} {user.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(ownerName)) ownerName = user.Email ?? "Anonymous";

        var review = new Review
        {
            OwnerId       = CurrentUserId,
            OwnerName     = ownerName,
            FacilityName  = facilityName,
            Rating        = vm.Rating,
            Title         = string.IsNullOrWhiteSpace(vm.Title) ? null : vm.Title.Trim(),
            Body          = vm.Body.Trim(),
            SubmittedAt   = DateTime.UtcNow,
            IsApproved    = false,
            IsFeatured    = false,
        };

        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Thanks for the review! Our team will check it and may feature it on the homepage.";
        return RedirectToAction(nameof(Index));
    }
}
