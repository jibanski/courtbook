using CourtBooking.Data;
using CourtBooking.Filters;
using CourtBooking.Models;
using CourtBooking.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace CourtBooking.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;

    public AdminController(ApplicationDbContext db, BookingService bookingService)
    {
        _db             = db;
        _bookingService = bookingService;
    }

    // ── Current-owner helpers ─────────────────────────────────────────────────
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private IQueryable<Court> MyCourts => _db.Courts.Where(c => c.OwnerId == CurrentUserId);
    private async Task<FacilitySettings?> GetMySettingsAsync() =>
        await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == CurrentUserId);
    private async Task<List<int>> GetMyCourtIdsAsync() =>
        await MyCourts.Select(c => c.Id).ToListAsync();

    public async Task<IActionResult> Index()
    {
        var courtIds = await GetMyCourtIdsAsync();

        var totalBookings   = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.Status != BookingStatus.Cancelled);
        var todayBookings   = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.BookingDate == DateOnly.FromDateTime(DateTime.Today) && b.Status != BookingStatus.Cancelled);
        var totalRevenue    = await _db.Bookings.Where(b => courtIds.Contains(b.CourtId) && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed)).SumAsync(b => b.TotalPrice);
        var activeCourts    = await MyCourts.CountAsync(c => c.IsActive);
        var awaitingPayment = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.Status == BookingStatus.Pending && b.PaymentReference != null);

        ViewBag.TotalBookings   = totalBookings;
        ViewBag.TodayBookings   = todayBookings;
        ViewBag.TotalRevenue    = totalRevenue;
        ViewBag.ActiveCourts    = activeCourts;
        ViewBag.AwaitingPayment = awaitingPayment;
        var settings = await GetMySettingsAsync();
        ViewBag.FacilitySettings = settings;

        // ── Setup Checklist (shown on dashboard until all required items are done) ──
        var hasCourt   = activeCourts > 0;
        var hasPayment = !string.IsNullOrWhiteSpace(settings?.GCashNumber)
                         || !string.IsNullOrWhiteSpace(settings?.MayaNumber)
                         || !string.IsNullOrWhiteSpace(settings?.PayMongoSecretKey);
        var hasAddress = !string.IsNullOrWhiteSpace(settings?.Address);
        var hasLogo    = !string.IsNullOrWhiteSpace(settings?.BrandLogoUrl);
        var hasTagline = !string.IsNullOrWhiteSpace(settings?.BrandTagline);
        var hasSlug    = !string.IsNullOrWhiteSpace(settings?.Slug);

        var steps = new[]
        {
            new { Title = "Add your first court",        Done = hasCourt,   Required = true,  Url = Url.Action("CreateCourt", "Admin")!,        Cta = "Add court",       Icon = "bi-buildings",     Hint = "Customers need a court they can book." },
            new { Title = "Add a payment option",        Done = hasPayment, Required = true,  Url = Url.Action("Settings",    "Admin") + "#payments", Cta = "Add payment",   Icon = "bi-credit-card",   Hint = "GCash, Maya, or PayMongo \u2014 at least one." },
            new { Title = "Set your facility address",   Done = hasAddress, Required = true,  Url = Url.Action("Settings",    "Admin") + "#facility", Cta = "Add address",   Icon = "bi-geo-alt",       Hint = "Shown on your public booking page." },
            new { Title = "Upload your brand logo",      Done = hasLogo,    Required = false, Url = Url.Action("Settings",    "Admin") + "#branding", Cta = "Upload logo",   Icon = "bi-image",         Hint = "Recommended \u2014 replaces the CourtBook logo for your customers." },
            new { Title = "Add a tagline",               Done = hasTagline, Required = false, Url = Url.Action("Settings",    "Admin") + "#branding", Cta = "Add tagline",   Icon = "bi-chat-quote",    Hint = "A short line shown on your public page." },
            new { Title = "Share your public booking link", Done = hasSlug && hasCourt && hasPayment && hasAddress, Required = false, Url = Url.Action("Settings", "Admin") + "#share", Cta = "Copy link", Icon = "bi-link-45deg", Hint = "Send this to your customers so they can start booking." },
        };
        ViewBag.SetupSteps        = steps;
        ViewBag.SetupRequiredDone = steps.Where(s => s.Required).All(s => s.Done);
        ViewBag.SetupDoneCount    = steps.Count(s => s.Done);
        ViewBag.SetupTotalCount   = steps.Length;

        var recentBookings = await _db.Bookings
            .Where(b => courtIds.Contains(b.CourtId))
            .Include(b => b.Court)
            .Include(b => b.User)
            .OrderByDescending(b => b.CreatedAt)
            .Take(10)
            .ToListAsync();

        return View(recentBookings);
    }

    // ── Real-time analytics ───────────────────────────────────────────────────

    /// <summary>Analytics dashboard. Charts are populated by AnalyticsData() via polling.</summary>
    public async Task<IActionResult> Analytics()
    {
        ViewBag.FacilitySettings = await GetMySettingsAsync();
        return View();
    }

    /// <summary>
    /// JSON endpoint backing /admin/analytics. Auto-refreshed every 10s by the page.
    /// Returns counters, last-30-day revenue series, payment-method breakdown, conversion.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> AnalyticsData()
    {
        var courtIds = await GetMyCourtIdsAsync();
        var today    = DateOnly.FromDateTime(DateTime.Today);
        var since30  = today.AddDays(-29);
        var since30Dt = DateTime.UtcNow.AddDays(-30);
        var todayDt  = DateTime.UtcNow.Date;

        var liveBookings = _db.Bookings.Where(b => courtIds.Contains(b.CourtId));

        var totalBookings   = await liveBookings.CountAsync(b => b.Status != BookingStatus.Cancelled);
        var todayBookings   = await liveBookings.CountAsync(b => b.BookingDate == today && b.Status != BookingStatus.Cancelled);
        var todayRevenue    = await liveBookings
            .Where(b => b.PaidAt != null && b.PaidAt >= todayDt)
            .SumAsync(b => (decimal?)b.TotalPrice) ?? 0m;
        var totalRevenue    = await liveBookings
            .Where(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed)
            .SumAsync(b => (decimal?)b.TotalPrice) ?? 0m;
        var awaitingPayment = await liveBookings.CountAsync(b => b.Status == BookingStatus.Pending && b.PaymentReference != null);
        var pendingNoProof  = await liveBookings.CountAsync(b => b.Status == BookingStatus.Pending && b.PaymentReference == null);

        var revenueRows = await liveBookings
            .Where(b => b.BookingDate >= since30
                        && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed))
            .GroupBy(b => b.BookingDate)
            .Select(g => new { Day = g.Key, Revenue = g.Sum(b => b.TotalPrice), Count = g.Count() })
            .ToListAsync();

        var revenueByDay = new List<object>();
        for (var d = since30; d <= today; d = d.AddDays(1))
        {
            var row = revenueRows.FirstOrDefault(r => r.Day == d);
            revenueByDay.Add(new
            {
                date    = d.ToString("yyyy-MM-dd"),
                revenue = row?.Revenue ?? 0m,
                count   = row?.Count   ?? 0
            });
        }

        // Payment mix — include legacy paid bookings that have no PaidAt by
        // falling back to BookingDate, matching the 'paid30' counter below.
        var methodRows = await liveBookings
            .Where(b => b.PaymentStatus == PaymentStatus.Paid
                        && ((b.PaidAt != null && b.PaidAt >= since30Dt)
                            || (b.PaidAt == null && b.BookingDate >= since30)))
            .GroupBy(b => b.PaymentMethod ?? "Unknown")
            .Select(g => new { Method = g.Key, Count = g.Count(), Revenue = g.Sum(b => b.TotalPrice) })
            .ToListAsync();

        var bookings30 = await liveBookings
            .CountAsync(b => b.BookingDate >= since30 && b.Status != BookingStatus.Cancelled);
        var paid30 = await liveBookings
            .CountAsync(b => b.BookingDate >= since30 && b.PaymentStatus == PaymentStatus.Paid);
        var conversion = bookings30 > 0 ? Math.Round(paid30 * 100.0 / bookings30, 1) : 0.0;

        return Json(new
        {
            generatedAt = DateTime.UtcNow,
            counters = new
            {
                totalBookings,
                todayBookings,
                todayRevenue,
                totalRevenue,
                awaitingPayment,
                pendingNoProof,
                conversionPct  = conversion,
                paidLast30     = paid30,
                bookingsLast30 = bookings30
            },
            revenueByDay,
            methodBreakdown = methodRows.Select(r => new
            {
                method  = r.Method,
                count   = r.Count,
                revenue = r.Revenue
            })
        });
    }

    public async Task<IActionResult> Bookings(string? status, DateOnly? date, bool? awaitingConfirmation)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var query = _db.Bookings
            .Where(b => courtIds.Contains(b.CourtId))
            .Include(b => b.Court).Include(b => b.User).AsQueryable();

        if (awaitingConfirmation == true)
            query = query.Where(b => b.Status == BookingStatus.Pending && b.PaymentReference != null);
        else if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BookingStatus>(status, out var s))
            query = query.Where(b => b.Status == s);

        if (date.HasValue)
            query = query.Where(b => b.BookingDate == date.Value);

        var bookings = await query.OrderByDescending(b => b.PaymentProofSubmittedAt ?? b.CreatedAt).ToListAsync();
        ViewBag.SelectedStatus       = status;
        ViewBag.SelectedDate         = date;
        ViewBag.AwaitingConfirmation = awaitingConfirmation;
        ViewBag.PendingPaymentCount  = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.Status == BookingStatus.Pending && b.PaymentReference != null);
        return View(bookings);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(int id)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var booking  = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id && courtIds.Contains(b.CourtId));
        if (booking is null) return NotFound();

        booking.Status        = BookingStatus.Confirmed;
        booking.PaymentStatus = PaymentStatus.Paid;
        booking.PaidAt        = DateTime.UtcNow;

        // Accrue platform commission for commission-model facilities
        var settings = await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == CurrentUserId);
        if (settings?.IsCommissionModel == true && booking.TotalPrice > 0)
        {
            var commission = Math.Round(booking.TotalPrice * settings.CommissionRate / 100m, 2);
            booking.CommissionAmount          = commission;
            settings.CommissionBalanceOwed   += commission;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Booking #{id} confirmed.";
        return RedirectToAction(nameof(Bookings), new { awaitingConfirmation = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectPayment(int id)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var booking  = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id && courtIds.Contains(b.CourtId));
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
        var courts = await MyCourts.ToListAsync();
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
        court.OwnerId = CurrentUserId;
        court.FacilityName = (await GetMySettingsAsync())?.FacilityName;
        _db.Courts.Add(court);
        await _db.SaveChangesAsync();
        court.ImageUrl = await SaveCourtPhotoAsync(photo, court.Id, null);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Court created successfully.";
        return RedirectToAction(nameof(Courts));
    }

    public async Task<IActionResult> EditCourt(int id)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == id);
        if (court is null) return NotFound();
        await PopulateSportsAsync();
        return View(court);
    }

    public async Task<IActionResult> ManageSlots(int id, DateOnly? date)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == id);
        if (court is null) return NotFound();

        var selectedDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var slots = await _db.CourtTimeSlots
            .Where(s => s.CourtId == id && s.SlotDate == selectedDate)
            .OrderBy(s => s.StartHour)
            .ToListAsync();

        var bookedHours  = await _bookingService.GetBookedHoursAsync(id, selectedDate);
        var blockedHours = slots
            .Where(s => !s.IsActive)
            .SelectMany(s => Enumerable.Range(s.StartHour, s.EndHour - s.StartHour))
            .ToHashSet();

        // Date/time range blocks that cover the selected date (for banner display)
        var activeRangeBlocks = await _db.CourtBlocks
            .Where(b => b.CourtId == id && b.StartDate <= selectedDate && b.EndDate >= selectedDate)
            .ToListAsync();

        ViewBag.Court             = court;
        ViewBag.Date              = selectedDate;
        ViewBag.BookedHours       = bookedHours.ToHashSet();
        ViewBag.BlockedHours      = blockedHours;
        ViewBag.ActiveRangeBlocks = activeRangeBlocks;
        return View(slots);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BlockHour(int courtId, DateOnly slotDate, int hour)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        // Upsert: if a 1-hour slot already exists for this hour, mark inactive; otherwise create one
        var existing = await _db.CourtTimeSlots.FirstOrDefaultAsync(s =>
            s.CourtId == courtId && s.SlotDate == slotDate &&
            s.StartHour == hour && s.EndHour == hour + 1);

        if (existing is not null)
            existing.IsActive = false;
        else
            _db.CourtTimeSlots.Add(new CourtTimeSlot
            {
                CourtId   = courtId,
                SlotDate  = slotDate,
                StartHour = hour,
                EndHour   = hour + 1,
                IsActive  = false
            });

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ManageSlots), new { id = courtId, date = slotDate });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnblockHour(int courtId, DateOnly slotDate, int hour)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        // Remove any inactive 1-hour marker for this hour
        var slot = await _db.CourtTimeSlots.FirstOrDefaultAsync(s =>
            myCourtIds.Contains(s.CourtId) && s.CourtId == courtId &&
            s.SlotDate == slotDate && s.StartHour == hour && s.EndHour == hour + 1 && !s.IsActive);

        if (slot is not null)
        {
            _db.CourtTimeSlots.Remove(slot);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(ManageSlots), new { id = courtId, date = slotDate });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCourt(Court court, IFormFile? photo)
    {
        if (!ModelState.IsValid) { await PopulateSportsAsync(); return View(court); }

        var existing = await MyCourts.FirstOrDefaultAsync(c => c.Id == court.Id);
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
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
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
        var myCourtIds = await GetMyCourtIdsAsync();
        var slot = await _db.CourtTimeSlots.FirstOrDefaultAsync(s => s.Id == id && myCourtIds.Contains(s.CourtId));
        if (slot is null) return NotFound();
        _db.CourtTimeSlots.Remove(slot);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Slot removed.";
        return RedirectToAction(nameof(ManageSlots), new { id = courtId, date = slotDate });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCourtSlot(int id, int courtId, DateOnly slotDate)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var slot = await _db.CourtTimeSlots.FirstOrDefaultAsync(s => s.Id == id && myCourtIds.Contains(s.CourtId));
        if (slot is null) return NotFound();
        slot.IsActive = !slot.IsActive;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ManageSlots), new { id = courtId, date = slotDate });
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    public async Task<IActionResult> Settings()
    {
        var settings = await GetMySettingsAsync() ?? new FacilitySettings();
        return View(settings);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(FacilitySettings model, IFormFile? logo,
        IFormFile? gcashQr, IFormFile? mayaQr, string[]? paymentMethods)
    {
        // These properties are not part of the settings form — remove any binding
        // errors caused by nullable-reference-type implicit [Required] checks.
        foreach (var key in new[] {
            nameof(FacilitySettings.BillingModel),
            nameof(FacilitySettings.OwnerId),
            nameof(FacilitySettings.CommissionRate),
            nameof(FacilitySettings.CommissionBalanceOwed),
            nameof(FacilitySettings.CommissionTotalPaid),
            nameof(FacilitySettings.BrandLogoUrl),
        })
            ModelState.Remove(key);

        if (!ModelState.IsValid) return View(model);

        var settings = await GetMySettingsAsync();
        if (settings is null)
        {
            model.OwnerId = CurrentUserId;
            _db.FacilitySettings.Add(model);
        }
        else
        {
            settings.FacilityName        = model.FacilityName;
            settings.Address             = model.Address;
            settings.GCashNumber         = model.GCashNumber;
            settings.GCashName           = model.GCashName;
            settings.MayaNumber          = model.MayaNumber;
            settings.MayaName            = model.MayaName;

            if (gcashQr is { Length: > 0 })
                settings.GCashQrCodePath = await SaveQrCodeAsync(gcashQr, "gcash", settings.GCashQrCodePath);
            if (mayaQr is { Length: > 0 })
                settings.MayaQrCodePath  = await SaveQrCodeAsync(mayaQr,  "maya",  settings.MayaQrCodePath);
            settings.PaymentInstructions = model.PaymentInstructions;
            settings.PayMongoSecretKey   = string.IsNullOrWhiteSpace(model.PayMongoSecretKey)
                                           ? null : model.PayMongoSecretKey.Trim();

            // Payment methods: keep only the supported ones. Fall back to QRPh
            // when the user unticks everything so checkout never breaks.
            var picked = (paymentMethods ?? Array.Empty<string>())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim().ToLowerInvariant())
                .Where(Services.PayMongoService.AllPhilippinesMethods.Contains)
                .Distinct()
                .ToArray();
            settings.PayMongoMethods = picked.Length == 0 ? "qrph" : string.Join(",", picked);
            settings.FacebookUrl         = string.IsNullOrWhiteSpace(model.FacebookUrl)  ? null : model.FacebookUrl.Trim();
            settings.InstagramUrl        = string.IsNullOrWhiteSpace(model.InstagramUrl) ? null : model.InstagramUrl.Trim();

            // Slug update — sanitize and ensure uniqueness
            if (!string.IsNullOrWhiteSpace(model.Slug))
            {
                var newSlug = SanitizeSlug(model.Slug);
                var taken   = await _db.FacilitySettings
                    .AnyAsync(s => s.Slug == newSlug && s.OwnerId != CurrentUserId);
                if (taken)
                    ModelState.AddModelError(nameof(model.Slug), "That URL is already taken. Please choose another.");
                else
                    settings.Slug = newSlug;
            }

            // Custom branding — available to all users (CourtBook is free)
            settings.BrandName    = string.IsNullOrWhiteSpace(model.BrandName)    ? null : model.BrandName.Trim();
            settings.BrandTagline = string.IsNullOrWhiteSpace(model.BrandTagline) ? null : model.BrandTagline.Trim();

            if (logo is { Length: > 0 })
                settings.BrandLogoUrl = await SaveBrandLogoAsync(logo, settings.BrandLogoUrl);
        }

        if (!ModelState.IsValid) return View(await GetMySettingsAsync() ?? model);

        await _db.SaveChangesAsync();
        TempData["Success"] = "Settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    private async Task<string?> SaveQrCodeAsync(IFormFile file, string prefix, string? existing)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) return existing;

        var dir = Path.Combine(UploadsRoot, "uploads", "qr");
        Directory.CreateDirectory(dir);
        var fileName = $"{prefix}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream);
        return $"/uploads/qr/{fileName}";
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

    private static string SanitizeSlug(string input) =>
        Regex.Replace(
            Regex.Replace(input.ToLowerInvariant().Replace(" ", "-"), @"[^a-z0-9\-]", ""),
            @"-+", "-").Trim('-');

    private async Task PopulateSportsAsync()
    {
        ViewBag.SportOptions = await _db.Sports
            .Where(s => s.IsActive)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => s.Name)
            .ToListAsync();
    }

    // ── Court Date/Time Range Blocks ─────────────────────────────────────────

    public async Task<IActionResult> BlockCourt(int id)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == id);
        if (court is null) return NotFound();

        var blocks = await _db.CourtBlocks
            .Where(b => b.CourtId == id)
            .OrderByDescending(b => b.StartDate).ThenByDescending(b => b.StartHour)
            .ToListAsync();

        ViewBag.Court = court;
        return View(blocks);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCourtBlock(int courtId,
        DateOnly startDate, int startHour,
        DateOnly endDate,   int endHour,
        string?  reason)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        // Basic validation
        var startDt = startDate.ToDateTime(new TimeOnly(startHour, 0));
        var endDt   = endDate.ToDateTime(new TimeOnly(endHour,   0));
        if (endDt <= startDt)
        {
            TempData["Error"] = "End must be after start.";
            return RedirectToAction(nameof(BlockCourt), new { id = courtId });
        }

        _db.CourtBlocks.Add(new CourtBlock
        {
            CourtId   = courtId,
            StartDate = startDate,
            StartHour = startHour,
            EndDate   = endDate,
            EndHour   = endHour,
            Reason    = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Court blocked from {startDate:MMM d} {startHour:D2}:00 to {endDate:MMM d} {endHour:D2}:00.";
        return RedirectToAction(nameof(BlockCourt), new { id = courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCourtBlock(int id, int courtId)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var blk = await _db.CourtBlocks.FirstOrDefaultAsync(b =>
            b.Id == id && myCourtIds.Contains(b.CourtId));
        if (blk is not null)
        {
            _db.CourtBlocks.Remove(blk);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Block removed.";
        }
        return RedirectToAction(nameof(BlockCourt), new { id = courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCourt(int id)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == id);
        if (court is null) return NotFound();
        court.IsActive = !court.IsActive;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Courts));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBookingStatus(int id, BookingStatus status)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var booking  = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id && courtIds.Contains(b.CourtId));
        if (booking is null) return NotFound();
        booking.Status = status;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Booking status updated.";
        return RedirectToAction(nameof(Bookings));
    }
}
