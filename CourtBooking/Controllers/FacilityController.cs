using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Controllers;

/// <summary>
/// Tenant-specific court listing at /f/{slug}
/// Each facility owner can share this URL with their customers.
/// </summary>
[Route("f")]
public class FacilityController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;

    public FacilityController(ApplicationDbContext db, BookingService bookingService)
    {
        _db             = db;
        _bookingService = bookingService;
    }

    // GET /f/{slug}
    [Route("{slug}")]
    public async Task<IActionResult> Index(string slug, string? sport)
    {
        var settings = await _db.FacilitySettings
            .FirstOrDefaultAsync(s => s.Slug == slug);

        if (settings is null) return NotFound();

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

        ViewBag.FacilitySettings = settings;
        ViewBag.Sports           = sports;
        ViewBag.SelectedSport    = sport;
        ViewBag.Slug             = slug;
        return View(courts);
    }

    // GET /f/{slug}/book/{courtId}
    [Route("{slug}/book/{courtId:int}")]
    public async Task<IActionResult> BookCourt(string slug, int courtId, DateTime? date)
    {
        var settings = await _db.FacilitySettings.FirstOrDefaultAsync(s => s.Slug == slug);
        if (settings is null) return NotFound();

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
            var bookedHours = await _bookingService.GetBookedHoursAsync(courtId, selectedDate);
            vm.BookedHours    = bookedHours;
            vm.AvailableHours = Enumerable
                .Range(court.OpeningHour, court.ClosingHour - court.OpeningHour)
                .Where(h => !bookedHours.Contains(h))
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
}
