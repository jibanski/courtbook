using CourtBooking.Data;
using CourtBooking.Filters;
using CourtBooking.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Controllers;

[Authorize(Roles = "Admin")]
[TypeFilter(typeof(TrialCheckFilter))]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var totalBookings    = await _db.Bookings.CountAsync(b => b.Status != BookingStatus.Cancelled);
        var todayBookings    = await _db.Bookings.CountAsync(b => b.BookingDate == DateOnly.FromDateTime(DateTime.Today) && b.Status != BookingStatus.Cancelled);
        var totalRevenue     = await _db.Bookings.Where(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed).SumAsync(b => b.TotalPrice);
        var activeCourts     = await _db.Courts.CountAsync(c => c.IsActive);
        var awaitingPayment  = await _db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending && b.PaymentReference != null);

        ViewBag.TotalBookings   = totalBookings;
        ViewBag.TodayBookings   = todayBookings;
        ViewBag.TotalRevenue    = totalRevenue;
        ViewBag.ActiveCourts    = activeCourts;
        ViewBag.AwaitingPayment = awaitingPayment;

        var recentBookings = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .OrderByDescending(b => b.CreatedAt)
            .Take(10)
            .ToListAsync();

        return View(recentBookings);
    }

    public async Task<IActionResult> Bookings(string? status, DateOnly? date, bool? awaitingConfirmation)
    {
        var query = _db.Bookings.Include(b => b.Court).Include(b => b.User).AsQueryable();

        if (awaitingConfirmation == true)
            query = query.Where(b => b.Status == BookingStatus.Pending && b.PaymentReference != null);
        else if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BookingStatus>(status, out var s))
            query = query.Where(b => b.Status == s);

        if (date.HasValue)
            query = query.Where(b => b.BookingDate == date.Value);

        var bookings = await query.OrderByDescending(b => b.PaymentProofSubmittedAt ?? b.CreatedAt).ToListAsync();
        ViewBag.SelectedStatus            = status;
        ViewBag.SelectedDate              = date;
        ViewBag.AwaitingConfirmation      = awaitingConfirmation;
        ViewBag.PendingPaymentCount       = await _db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending && b.PaymentReference != null);
        return View(bookings);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(int id)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();

        booking.Status        = BookingStatus.Confirmed;
        booking.PaymentStatus = PaymentStatus.Paid;
        booking.PaidAt        = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Booking #{id} confirmed.";
        return RedirectToAction(nameof(Bookings), new { awaitingConfirmation = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectPayment(int id)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();

        booking.Status           = BookingStatus.Cancelled;
        booking.PaymentReference = null;
        booking.PaymentProofPath = null;
        await _db.SaveChangesAsync();
        TempData["Error"] = $"Booking #{id} rejected and cancelled.";
        return RedirectToAction(nameof(Bookings), new { awaitingConfirmation = true });
    }

    public async Task<IActionResult> Courts()
    {
        var courts = await _db.Courts.ToListAsync();
        return View(courts);
    }

    public async Task<IActionResult> CreateCourt()
    {
        await PopulateSportsAsync();
        return View(new Court());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCourt(Court court, IFormFile? photo)
    {
        if (!ModelState.IsValid) { await PopulateSportsAsync(); return View(court); }
        _db.Courts.Add(court);
        await _db.SaveChangesAsync();
        court.ImageUrl = await SaveCourtPhotoAsync(photo, court.Id, null);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Court created successfully.";
        return RedirectToAction(nameof(Courts));
    }

    public async Task<IActionResult> EditCourt(int id)
    {
        var court = await _db.Courts.FindAsync(id);
        if (court is null) return NotFound();
        await PopulateSportsAsync();
        return View(court);
    }

    public async Task<IActionResult> ManageSlots(int id, DateOnly? date)
    {
        var court = await _db.Courts.FindAsync(id);
        if (court is null) return NotFound();

        var selectedDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var slots = await _db.CourtTimeSlots
            .Where(s => s.CourtId == id && s.SlotDate == selectedDate)
            .OrderBy(s => s.StartHour)
            .ToListAsync();

        ViewBag.Court = court;
        ViewBag.Date  = selectedDate;
        return View(slots);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCourt(Court court, IFormFile? photo)
    {
        if (!ModelState.IsValid) { await PopulateSportsAsync(); return View(court); }

        var existing = await _db.Courts.FindAsync(court.Id);
        if (existing is null) return NotFound();

        existing.Name         = court.Name;
        existing.SportType    = court.SportType;
        existing.Description  = court.Description;
        existing.PricePerHour = court.PricePerHour;
        existing.OpeningHour  = court.OpeningHour;
        existing.ClosingHour  = court.ClosingHour;
        existing.IsIndoor     = court.IsIndoor;
        existing.IsActive     = court.IsActive;
        existing.ImageUrl     = await SaveCourtPhotoAsync(photo, court.Id, existing.ImageUrl);

        await _db.SaveChangesAsync();
        TempData["Success"] = "Court updated successfully.";
        return RedirectToAction(nameof(Courts));
    }

    // ── Sports ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Sports()
    {
        var sports = await _db.Sports.OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).ToListAsync();
        return View(sports);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSport(Sport sport)
    {
        if (string.IsNullOrWhiteSpace(sport.Name))
        {
            TempData["Error"] = "Sport name is required.";
            return RedirectToAction(nameof(Sports));
        }
        if (await _db.Sports.AnyAsync(s => s.Name == sport.Name))
        {
            TempData["Error"] = $"Sport '{sport.Name}' already exists.";
            return RedirectToAction(nameof(Sports));
        }
        _db.Sports.Add(sport);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Sport '{sport.Name}' added.";
        return RedirectToAction(nameof(Sports));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditSport(int id, string name, string? description, int displayOrder)
    {
        var sport = await _db.Sports.FindAsync(id);
        if (sport is null) return NotFound();

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Sport name is required.";
            return RedirectToAction(nameof(Sports));
        }
        sport.Name = name.Trim();
        sport.Description = description?.Trim();
        sport.DisplayOrder = displayOrder;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Sport updated.";
        return RedirectToAction(nameof(Sports));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSport(int id)
    {
        var sport = await _db.Sports.FindAsync(id);
        if (sport is null) return NotFound();
        sport.IsActive = !sport.IsActive;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Sports));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSport(int id)
    {
        var sport = await _db.Sports.FindAsync(id);
        if (sport is null) return NotFound();
        bool inUse = await _db.Courts.AnyAsync(c => c.SportType == sport.Name);
        if (inUse)
        {
            TempData["Error"] = $"Cannot delete '{sport.Name}' — it is used by one or more courts.";
            return RedirectToAction(nameof(Sports));
        }
        _db.Sports.Remove(sport);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Sport '{sport.Name}' deleted.";
        return RedirectToAction(nameof(Sports));
    }

    // ── Court Time Slots ──────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCourtSlot(int courtId, DateOnly slotDate, int startHour, int endHour)
    {
        var court = await _db.Courts.FindAsync(courtId);
        if (court is null) return NotFound();

        if (endHour <= startHour || startHour < 0 || endHour > 24)
        {
            TempData["Error"] = "Invalid slot: end hour must be after start hour.";
            return RedirectToAction(nameof(ManageSlots), new { id = courtId, date = slotDate });
        }

        bool duplicate = await _db.CourtTimeSlots.AnyAsync(s =>
            s.CourtId == courtId && s.SlotDate == slotDate &&
            s.StartHour == startHour && s.EndHour == endHour);
        if (duplicate)
        {
            TempData["Error"] = $"Slot {startHour:D2}:00–{endHour:D2}:00 already exists for this date.";
            return RedirectToAction(nameof(ManageSlots), new { id = courtId, date = slotDate });
        }

        _db.CourtTimeSlots.Add(new CourtTimeSlot
        {
            CourtId = courtId,
            SlotDate = slotDate,
            StartHour = startHour,
            EndHour = endHour
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Slot {startHour:D2}:00–{endHour:D2}:00 added for {slotDate:MMM d, yyyy}.";
        return RedirectToAction(nameof(ManageSlots), new { id = courtId, date = slotDate });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCourtSlot(int id, int courtId, DateOnly slotDate)
    {
        var slot = await _db.CourtTimeSlots.FindAsync(id);
        if (slot is null) return NotFound();
        _db.CourtTimeSlots.Remove(slot);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Slot removed.";
        return RedirectToAction(nameof(ManageSlots), new { id = courtId, date = slotDate });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCourtSlot(int id, int courtId, DateOnly slotDate)
    {
        var slot = await _db.CourtTimeSlots.FindAsync(id);
        if (slot is null) return NotFound();
        slot.IsActive = !slot.IsActive;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ManageSlots), new { id = courtId, date = slotDate });
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    public async Task<IActionResult> Settings()
    {
        var settings = await _db.FacilitySettings.FirstOrDefaultAsync() ?? new FacilitySettings();
        return View(settings);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(FacilitySettings model, IFormFile? logo)
    {
        if (!ModelState.IsValid) return View(model);

        var settings = await _db.FacilitySettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            model.Id = 1;
            _db.FacilitySettings.Add(model);
        }
        else
        {
            settings.FacilityName        = model.FacilityName;
            settings.GCashNumber         = model.GCashNumber;
            settings.GCashName           = model.GCashName;
            settings.MayaNumber          = model.MayaNumber;
            settings.MayaName            = model.MayaName;
            settings.PaymentInstructions = model.PaymentInstructions;

            // Custom branding — only applied for subscribed users
            if (settings.IsSubscribed)
            {
                settings.BrandName    = string.IsNullOrWhiteSpace(model.BrandName)    ? null : model.BrandName.Trim();
                settings.BrandTagline = string.IsNullOrWhiteSpace(model.BrandTagline) ? null : model.BrandTagline.Trim();

                if (logo is { Length: > 0 })
                    settings.BrandLogoUrl = await SaveBrandLogoAsync(logo, settings.BrandLogoUrl);
            }
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    private async Task<string?> SaveBrandLogoAsync(IFormFile file, string? existing)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".svg")) return existing;

        var dir = Path.Combine(UploadsRoot, "uploads", "brand");
        Directory.CreateDirectory(dir);
        var fileName = $"logo_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream);
        return $"/uploads/brand/{fileName}";
    }

    private async Task<string?> SaveCourtPhotoAsync(IFormFile? photo, int courtId, string? existing)
    {
        if (photo is not { Length: > 0 }) return existing;

        var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) return existing;

        var dir = Path.Combine(UploadsRoot, "uploads", "courts");
        Directory.CreateDirectory(dir);
        var fileName = $"court_{courtId}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        using var stream = System.IO.File.Create(fullPath);
        await photo.CopyToAsync(stream);
        return $"/uploads/courts/{fileName}";
    }

    /// <summary>
    /// Returns the root folder for file uploads.
    /// On Railway: UPLOADS_ROOT env var points to the persistent volume (e.g. /data).
    /// Locally: falls back to wwwroot so existing behaviour is unchanged.
    /// </summary>
    private static string UploadsRoot =>
        Environment.GetEnvironmentVariable("UPLOADS_ROOT")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

    private async Task PopulateSportsAsync()
    {
        ViewBag.SportOptions = await _db.Sports
            .Where(s => s.IsActive)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => s.Name)
            .ToListAsync();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCourt(int id)
    {
        var court = await _db.Courts.FindAsync(id);
        if (court is null) return NotFound();
        court.IsActive = !court.IsActive;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Courts));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBookingStatus(int id, BookingStatus status)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();
        booking.Status = status;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Booking status updated.";
        return RedirectToAction(nameof(Bookings));
    }
}
