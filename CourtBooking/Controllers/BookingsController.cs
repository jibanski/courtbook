using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Controllers;

[Authorize]
public class BookingsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public BookingsController(ApplicationDbContext db, BookingService bookingService, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _bookingService = bookingService;
        _userManager = userManager;
    }

    public async Task<IActionResult> My()
    {
        var userId = _userManager.GetUserId(User)!;
        var bookings = await _db.Bookings
            .Include(b => b.Court)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync();

        // Build a facility name map keyed by OwnerId for display in the list
        var ownerIds = bookings
            .Where(b => b.Court?.OwnerId != null)
            .Select(b => b.Court!.OwnerId!)
            .Distinct()
            .ToList();
        var facilityMap = await _db.FacilitySettings
            .Where(s => ownerIds.Contains(s.OwnerId!))
            .ToDictionaryAsync(s => s.OwnerId!, s => s.FacilityName);
        ViewBag.FacilityMap = facilityMap;

        return View(bookings);
    }

    public async Task<IActionResult> Create(int courtId, DateOnly? date, int? startHour, int? endHour)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == courtId && c.IsActive);
        if (court is null) return NotFound();

        // Load the facility name for this court's owner
        var facilityName = court.OwnerId != null
            ? (await _db.FacilitySettings
                .Where(s => s.OwnerId == court.OwnerId)
                .Select(s => s.FacilityName)
                .FirstOrDefaultAsync())
            : null;
        ViewBag.FacilityName = facilityName;

        var vm = new BookingViewModel
        {
            CourtId = courtId,
            Court = court,
            BookingDate = date ?? DateOnly.FromDateTime(DateTime.Today),
            StartHour = startHour ?? court.OpeningHour,
            DurationHours = (endHour.HasValue && startHour.HasValue) ? endHour.Value - startHour.Value : 1,
            FixedEndHour = endHour
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BookingViewModel vm)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == vm.CourtId && c.IsActive);
        if (court is null) return NotFound();
        vm.Court = court;

        if (vm.BookingDate < DateOnly.FromDateTime(DateTime.Today))
            ModelState.AddModelError("BookingDate", "Cannot book a date in the past.");

        if (vm.StartHour < court.OpeningHour || vm.StartHour >= court.ClosingHour)
            ModelState.AddModelError("StartHour", $"Start hour must be between {court.OpeningHour}:00 and {court.ClosingHour - 1}:00.");

        if (vm.StartHour + vm.DurationHours > court.ClosingHour)
            ModelState.AddModelError("DurationHours", "Booking extends beyond closing time.");

        if (!ModelState.IsValid) return View(vm);

        var available = await _bookingService.IsSlotAvailableAsync(vm.CourtId, vm.BookingDate, vm.StartTime, vm.EndTime);
        if (!available)
        {
            ModelState.AddModelError("", "This time slot is no longer available. Please choose another time.");
            return View(vm);
        }

        var userId = _userManager.GetUserId(User)!;
        var booking = new Booking
        {
            CourtId = vm.CourtId,
            UserId = userId,
            BookingDate = vm.BookingDate,
            StartTime = vm.StartTime,
            EndTime = vm.EndTime,
            TotalPrice = vm.TotalPrice,
            Notes = vm.Notes,
            Status = BookingStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid
        };

        await _bookingService.CreateBookingAsync(booking);
        return RedirectToAction(nameof(Pay), new { id = booking.Id });
    }

    // Shows GCash/Maya numbers + reference number submission form
    public async Task<IActionResult> Pay(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (booking is null) return NotFound();

        // Load the correct facility's settings (the court owner's), not just the first row
        var settings = (booking.Court?.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == booking.Court.OwnerId)
            : await _db.FacilitySettings.FirstOrDefaultAsync())
            ?? new FacilitySettings();
        ViewBag.Settings = settings;
        return View(booking);
    }

    // User submits their GCash/Maya reference number + optional screenshot
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitProof(int bookingId, string method, string reference, IFormFile? screenshot)
    {
        var userId = _userManager.GetUserId(User)!;
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId && b.PaymentStatus == PaymentStatus.Unpaid);

        if (booking is null) return NotFound();

        if (string.IsNullOrWhiteSpace(reference))
        {
            TempData["Error"] = "Please enter your transaction/reference number.";
            return RedirectToAction(nameof(Pay), new { id = bookingId });
        }

        string? screenshotPath = null;
        if (screenshot is { Length: > 0 })
        {
            var ext = Path.GetExtension(screenshot.FileName).ToLower();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            {
                TempData["Error"] = "Screenshot must be JPG, PNG, or WebP.";
                return RedirectToAction(nameof(Pay), new { id = bookingId });
            }

            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "proofs");
            Directory.CreateDirectory(uploadsDir);
            var fileName = $"{bookingId}_{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(uploadsDir, fileName);
            using var stream = System.IO.File.Create(fullPath);
            await screenshot.CopyToAsync(stream);
            screenshotPath = $"/uploads/proofs/{fileName}";
        }

        booking.PaymentMethod = method;
        booking.PaymentReference = reference.Trim();
        booking.PaymentProofPath = screenshotPath;
        booking.PaymentProofSubmittedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Payment details submitted! Your booking will be confirmed once the admin verifies your payment.";
        return RedirectToAction(nameof(My));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (booking is null) return NotFound();

        if (booking.BookingDate <= DateOnly.FromDateTime(DateTime.Today))
        {
            TempData["Error"] = "Cannot cancel a past or same-day booking.";
            return RedirectToAction(nameof(My));
        }

        booking.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Booking cancelled successfully.";
        return RedirectToAction(nameof(My));
    }
}
