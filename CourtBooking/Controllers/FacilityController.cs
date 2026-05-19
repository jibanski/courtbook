using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CourtBooking.Controllers;

/// <summary>
/// Tenant-specific court listing at /sportshub/{slug}.
/// Each facility owner can share this URL with their customers.
/// A legacy /f/{slug} route is preserved via <see cref="LegacyFacilityRedirectController"/>
/// so links shared before the rename keep working.
/// </summary>
[Route("sportshub")]
public class FacilityController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;

    public FacilityController(ApplicationDbContext db, BookingService bookingService)
    {
        _db             = db;
        _bookingService = bookingService;
    }

    // GET /sportshub/{slug}
    [Route("{slug}")]
    public async Task<IActionResult> Index(string slug, string? sport)
    {
        var settings = await _db.FacilitySettings
            .FirstOrDefaultAsync(s => s.Slug == slug);

        if (settings is null) return NotFound();

        // Suspended by platform admin — show an "unavailable" page instead of the
        // courts list. Owners (logged in as this facility's admin) are still let
        // through so they can read the suspension banner and contact support.
        if (settings.IsSuspended && !IsCurrentUserOwnerOf(settings))
        {
            ViewBag.Slug = slug;
            return View("Suspended", settings);
        }

        // Remember this facility so we can redirect back here after login / registration.
        // 7-day lifetime so returning customers still land on the right page.
        SetFacilityCookie(slug);

        var query = _db.Courts.Where(c => c.OwnerId == settings.OwnerId && c.IsActive);

        if (!string.IsNullOrWhiteSpace(sport))
            query = query.Where(c => c.SportType == sport);

        var courts = await query.OrderBy(c => c.SportType).ThenBy(c => c.Name).ToListAsync();
        var sports  = await _db.Courts
            .Where(c => c.OwnerId == settings.OwnerId && c.IsActive)
            .Select(c => c.SportType).Distinct().ToListAsync();

        // Show "Browse all facilities" back-link for authenticated customers
        // who do not have a preferred facility pinned (they came from the directory).
        bool showBackToDirectory = false;
        if (User.Identity?.IsAuthenticated == true && !User.IsInRole("Admin"))
        {
            var uid  = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = uid != null ? await _db.Users.FindAsync(uid) : null;
            showBackToDirectory = string.IsNullOrEmpty((user as CourtBooking.Models.ApplicationUser)?.PreferredFacilitySlug);
        }

        ViewBag.FacilitySettings    = settings;
        ViewBag.Sports              = sports;
        ViewBag.SelectedSport       = sport;
        ViewBag.Slug                = slug;
        ViewBag.ShowBackToDirectory = showBackToDirectory;
        return View(courts);
    }

    // GET /sportshub/{slug}/book/{courtId}
    [Route("{slug}/book/{courtId:int}")]
    public async Task<IActionResult> BookCourt(string slug, int courtId, DateTime? date)
    {
        var settings = await _db.FacilitySettings.FirstOrDefaultAsync(s => s.Slug == slug);
        if (settings is null) return NotFound();

        // Don't accept new bookings on a suspended facility.
        if (settings.IsSuspended && !IsCurrentUserOwnerOf(settings))
        {
            ViewBag.Slug = slug;
            return View("Suspended", settings);
        }

        // Keep the facility cookie fresh even on deep-links to a specific court
        SetFacilityCookie(slug);

        var court = await _db.Courts
            .FirstOrDefaultAsync(c => c.Id == courtId && c.OwnerId == settings.OwnerId && c.IsActive);
        if (court is null) return NotFound();

        var selectedDate = date.HasValue
            ? DateOnly.FromDateTime(date.Value)
            : DateOnly.FromDateTime(DateTime.Today);

        var slots = await _db.CourtTimeSlots
            .Where(s => s.CourtId == courtId && s.IsActive && s.SlotDate == selectedDate)
            .OrderBy(s => s.StartHour)
            .ToListAsync();

        var vm = new CourtAvailabilityViewModel { Court = court, Date = selectedDate };

        if (slots.Any())
        {
            vm.TimeSlots          = slots;
            vm.UnavailableSlotIds = await _bookingService.GetUnavailableSlotIdsAsync(courtId, selectedDate, slots);
        }
        else
        {
            var bookedHours  = await _bookingService.GetBookedHoursAsync(courtId, selectedDate);
            var blockedHours = await _bookingService.GetBlockedHoursAsync(courtId, selectedDate);
            vm.BookedHours    = bookedHours;
            vm.BlockedHours   = blockedHours;
            vm.AvailableHours = Enumerable
                .Range(court.OpeningHour, court.ClosingHour - court.OpeningHour)
                .Where(h => !bookedHours.Contains(h) && !blockedHours.Contains(h))
                .ToList();
        }

        ViewBag.FacilitySettings = settings;
        ViewBag.Slug             = slug;
        return View(vm);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetFacilityCookie(string slug) =>
        Response.Cookies.Append("facilitySlug", slug, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            MaxAge   = TimeSpan.FromDays(7)
        });

    /// <summary>
    /// True when the current user is the admin owner of the supplied facility,
    /// so a suspended owner can still see their own /sportshub/{slug} page
    /// (read-only) rather than being locked out entirely.
    /// </summary>
    private bool IsCurrentUserOwnerOf(FacilitySettings settings)
    {
        if (!User.Identity?.IsAuthenticated ?? true) return false;
        if (!User.IsInRole("Admin")) return false;
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrEmpty(uid) && uid == settings.OwnerId;
    }
}
