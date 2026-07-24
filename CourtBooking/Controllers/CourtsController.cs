using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CourtBooking.Controllers;

public class CourtsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CourtsController(ApplicationDbContext db, BookingService bookingService,
                            UserManager<ApplicationUser> userManager)
    {
        _db          = db;
        _bookingService = bookingService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(string? sport, string? facility)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var uid  = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = uid != null ? await _userManager.FindByIdAsync(uid) : null;

            // Admins → redirect to their own facility's public page
            if (User.IsInRole("Admin") && user != null)
            {
                var settings = await _db.FacilitySettings
                    .FirstOrDefaultAsync(s => s.OwnerId == uid);
                if (!string.IsNullOrEmpty(settings?.Slug))
                    return RedirectToAction("Index", "Facility", new { slug = settings.Slug });
            }

            // Customers → redirect to their preferred facility
            if (!User.IsInRole("Admin") && !string.IsNullOrEmpty(user?.PreferredFacilitySlug))
                return RedirectToAction("Index", "Facility", new { slug = user.PreferredFacilitySlug });
        }

        // Unauthenticated visitor who arrived via a shared facility link —
        // authenticated customers without a preferred facility can always reach
        // the directory, so skip the cookie redirect for them.
        if (User.Identity?.IsAuthenticated != true)
        {
            var cookieSlug = Request.Cookies["facilitySlug"];
            if (!string.IsNullOrEmpty(cookieSlug))
                return RedirectToAction("Index", "Facility", new { slug = cookieSlug });
        }

        var query = _db.Courts.Where(c => c.IsActive && c.OwnerId != null);

        if (!string.IsNullOrWhiteSpace(sport))
            query = query.Where(c => c.SportType == sport);

        // Load facility settings for all visible owners — keyed by OwnerId.
        // Excludes admin-suspended and owner-deactivated facilities.
        var facilityMap = await _db.FacilitySettings
            .Where(s => s.OwnerId != null && !s.IsSuspended && !s.IsDeactivated)
            .ToDictionaryAsync(s => s.OwnerId!);

        // Drop courts whose facility is hidden (or whose owner has no settings).
        var allowedOwnerIds = facilityMap.Keys.ToHashSet();
        query = query.Where(c => allowedOwnerIds.Contains(c.OwnerId!));

        // Optionally filter by facility
        if (!string.IsNullOrWhiteSpace(facility) && facilityMap.Values.Any(s => s.DisplayName == facility))
        {
            var ownerIds = facilityMap.Values
                .Where(s => s.DisplayName == facility)
                .Select(s => s.OwnerId)
                .ToList();
            query = query.Where(c => ownerIds.Contains(c.OwnerId));
        }

        var courts = await query.ToListAsync();

        var allSports = await _db.Courts
            .Where(c => c.IsActive && c.OwnerId != null && allowedOwnerIds.Contains(c.OwnerId!))
            .Select(c => new { c.OwnerId, c.SportType })
            .ToListAsync();

        // Group sports by facility owner
        var facilitySports = allSports
            .GroupBy(x => x.OwnerId!)
            .ToDictionary(g => g.Key, g => g.Select(x => x.SportType).Distinct().OrderBy(s => s).ToList());

        // Court count per facility
        var facilityCourtsCount = courts
            .GroupBy(c => c.OwnerId!)
            .ToDictionary(g => g.Key, g => g.Count());

        // All sports across all facilities (for the sport filter)
        var sports = allSports.Select(x => x.SportType).Distinct().OrderBy(s => s).ToList();

        // Facilities with at least one active court, sport-filtered if needed
        var activeFacilities = facilityMap.Values
            .Where(s => s.OwnerId != null
                     && facilityCourtsCount.ContainsKey(s.OwnerId!)
                     && (string.IsNullOrEmpty(sport) || (facilitySports.TryGetValue(s.OwnerId!, out var sp) && sp.Contains(sport))))
            .OrderByDescending(s => s.IsSubscribed)
            .ThenBy(s => s.DisplayName)
            .ToList();

        ViewBag.Sports             = sports;
        ViewBag.SelectedSport      = sport;
        ViewBag.FacilityMap        = facilityMap;
        ViewBag.ActiveFacilities   = activeFacilities;
        ViewBag.FacilitySports     = facilitySports;
        ViewBag.FacilityCourtsCount = facilityCourtsCount;
        return View(courts);
    }

    public async Task<IActionResult> Details(int id, DateTime? date)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == id && c.IsActive && c.OwnerId != null);
        if (court is null) return NotFound();

        // Load the facility's settings for branding on the details page
        var facilitySettings = court.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == court.OwnerId)
            : null;
        ViewBag.FacilitySettings = facilitySettings;

        var selectedDate = date.HasValue ? DateOnly.FromDateTime(date.Value) : DateOnly.FromDateTime(DateTime.Today);

        var slots = await _db.CourtTimeSlots
            .Where(s => s.CourtId == id && s.IsActive && s.SlotDate == selectedDate)
            .OrderBy(s => s.StartHour)
            .ToListAsync();

        var vm = new CourtAvailabilityViewModel { Court = court, Date = selectedDate };

        if (slots.Any())
        {
            vm.TimeSlots = slots;
            vm.UnavailableSlotIds = await _bookingService.GetUnavailableSlotIdsAsync(id, selectedDate, slots);
        }
        else
        {
            var bookedHours = await _bookingService.GetBookedHoursAsync(id, selectedDate);
            vm.BookedHours = bookedHours;
            vm.AvailableHours = Enumerable.Range(court.OpeningHour, court.ClosingHour - court.OpeningHour)
                .Where(h => !bookedHours.Contains(h))
                .ToList();
        }

        return View(vm);
    }
}
