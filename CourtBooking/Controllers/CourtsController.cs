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
        // Customers with a preferred facility always go to their facility's branded page
        if (User.Identity?.IsAuthenticated == true && !User.IsInRole("Admin"))
        {
            var uid  = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = uid != null ? await _userManager.FindByIdAsync(uid) : null;
            if (!string.IsNullOrEmpty(user?.PreferredFacilitySlug))
                return RedirectToAction("Index", "Facility", new { slug = user.PreferredFacilitySlug });
        }

        // Unauthenticated visitor who arrived via a shared facility link
        var cookieSlug = Request.Cookies["facilitySlug"];
        if (!string.IsNullOrEmpty(cookieSlug))
            return RedirectToAction("Index", "Facility", new { slug = cookieSlug });

        var query = _db.Courts.Where(c => c.IsActive && c.OwnerId != null);

        if (!string.IsNullOrWhiteSpace(sport))
            query = query.Where(c => c.SportType == sport);

        // Load facility settings for all owners — keyed by OwnerId
        var facilityMap = await _db.FacilitySettings
            .Where(s => s.OwnerId != null)
            .ToDictionaryAsync(s => s.OwnerId!);

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
        var sports  = await _db.Courts
            .Where(c => c.IsActive && c.OwnerId != null)
            .Select(c => c.SportType).Distinct().ToListAsync();

        // Facilities with at least one active court (subscribed shown first)
        var activeFacilities = facilityMap.Values
            .Where(s => courts.Any(c => c.OwnerId == s.OwnerId))
            .OrderByDescending(s => s.IsSubscribed)
            .ThenBy(s => s.DisplayName)
            .ToList();

        ViewBag.Sports           = sports;
        ViewBag.SelectedSport    = sport;
        ViewBag.SelectedFacility = facility;
        ViewBag.FacilityMap      = facilityMap;
        ViewBag.ActiveFacilities = activeFacilities;
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
