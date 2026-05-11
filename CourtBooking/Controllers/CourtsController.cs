using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Controllers;

public class CourtsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;

    public CourtsController(ApplicationDbContext db, BookingService bookingService)
    {
        _db = db;
        _bookingService = bookingService;
    }

    public async Task<IActionResult> Index(string? sport)
    {
        var query = _db.Courts.Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(sport))
            query = query.Where(c => c.SportType == sport);

        var courts = await query.ToListAsync();
        var sports = await _db.Courts.Where(c => c.IsActive).Select(c => c.SportType).Distinct().ToListAsync();

        ViewBag.Sports = sports;
        ViewBag.SelectedSport = sport;
        return View(courts);
    }

    public async Task<IActionResult> Details(int id, DateTime? date)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == id && c.IsActive);
        if (court is null) return NotFound();

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
