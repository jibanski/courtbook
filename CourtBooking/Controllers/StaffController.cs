using CourtBooking.Data;
using CourtBooking.Helpers;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Controllers;

/// <summary>
/// Front-desk area for limited-access Staff accounts: view the employer's court schedule,
/// create a walk-in booking for a customer paying cash on the spot, and review their own
/// cash log. Deliberately separate from <see cref="AdminController"/> — a Staff principal
/// never touches that controller, so there is no risk of an owner-only action leaking through.
/// </summary>
[Authorize(Roles = "Staff")]
public class StaffController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;
    private readonly GuestCheckoutService _guestCheckout;
    private readonly EmailService _email;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<StaffController> _logger;
    private readonly ImageCompressionService _imageCompression;

    public StaffController(
        ApplicationDbContext db,
        BookingService bookingService,
        GuestCheckoutService guestCheckout,
        EmailService email,
        UserManager<ApplicationUser> userManager,
        ILogger<StaffController> logger,
        ImageCompressionService imageCompression)
    {
        _db             = db;
        _bookingService = bookingService;
        _guestCheckout  = guestCheckout;
        _email          = email;
        _logger         = logger;
        _userManager    = userManager;
        _imageCompression = imageCompression;
    }

    // ── Employer scoping ─────────────────────────────────────────────────────

    private string CurrentStaffId => _userManager.GetUserId(User)!;

    private async Task<string?> GetEmployerOwnerIdAsync() =>
        (await _userManager.GetUserAsync(User))?.EmployerOwnerId;

    private async Task<IQueryable<Court>> MyCourtsAsync()
    {
        var employerOwnerId = await GetEmployerOwnerIdAsync();
        return _db.Courts.Where(c => c.OwnerId == employerOwnerId);
    }

    private async Task<List<int>> GetMyCourtIdsAsync() =>
        await (await MyCourtsAsync()).Select(c => c.Id).ToListAsync();

    // ── Dashboard ────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        var courtIds = await GetMyCourtIdsAsync();
        var today = PhtClock.Today;

        ViewBag.Courts = await (await MyCourtsAsync()).Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        var todaysRows = await GetBookingRowsAsync(courtIds, exactDate: today, excludeCancelled: true);
        ViewBag.TodaysBookings = todaysRows.OrderBy(r => r.StartTime).ToList();

        return View();
    }

    // ── Facility info (read-only) — house rules, weekly schedule, and pricing ──
    // for the employer's courts, so front-desk staff can answer customer questions
    // without needing Admin access. Mirrors AdminController.Schedule's data but
    // strips every edit/add/delete action — staff can look, not touch.

    public async Task<IActionResult> FacilityInfo()
    {
        var employerOwnerId = await GetEmployerOwnerIdAsync();
        var settings = employerOwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == employerOwnerId)
            : null;
        ViewBag.Settings = settings;

        var courts = await (await MyCourtsAsync()).Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        var courtInfo = new List<StaffCourtInfo>();
        foreach (var court in courts)
        {
            var tiers = (await _bookingService.GetRateTiersAsync(court.Id))
                .OrderBy(t => t.StartHour).ThenBy(t => t.EndHour).ThenBy(t => t.DaysOfWeek).ToList();
            var blocks = (await _bookingService.GetScheduleBlocksAsync(court.Id))
                .OrderBy(b => b.StartHour).ThenBy(b => b.EndHour).ThenBy(b => b.DaysOfWeek).ToList();
            courtInfo.Add(new StaffCourtInfo(court, tiers, blocks));
        }
        ViewBag.CourtInfo = courtInfo;

        return View();
    }

    public record StaffCourtInfo(Court Court, List<CourtRateTier> RateTiers, List<CourtScheduleBlock> ScheduleBlocks);

    // ── All bookings (read-only) — lets front-desk staff verify a booking went ──
    // through, or look up a customer's booking, without needing Admin access.

    public async Task<IActionResult> Bookings(string? status, DateOnly? date, string? search)
    {
        var courtIds = await GetMyCourtIdsAsync();
        BookingStatus? exactStatus = !string.IsNullOrWhiteSpace(status) && Enum.TryParse<BookingStatus>(status, out var s) ? s : null;

        var rows = await GetBookingRowsAsync(courtIds, exactDate: date, exactStatus: exactStatus);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(r => r.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                 || (r.CustomerPhone != null && r.CustomerPhone.Contains(term, StringComparison.OrdinalIgnoreCase)))
                       .ToList();
        }

        ViewBag.Rows = rows.OrderByDescending(r => r.CreatedAt).ToList();
        ViewBag.SelectedStatus = status;
        ViewBag.SelectedDate   = date;
        ViewBag.Search         = search;
        return View();
    }

    /// <summary>Merges regular court <see cref="Booking"/>s and <see cref="OpenPlaySignup"/>s for these
    /// courts into one unified row list — shared by the dashboard's "Today's Bookings" and the "All
    /// Bookings" page so Open Play sign-ups never silently disappear from either.</summary>
    private async Task<List<AdminBookingRow>> GetBookingRowsAsync(
        List<int> courtIds, DateOnly? exactDate = null, BookingStatus? exactStatus = null, bool excludeCancelled = false)
    {
        var query = _db.Bookings
            .Where(b => courtIds.Contains(b.CourtId))
            .Include(b => b.Court).Include(b => b.User).Include(b => b.AddOns).ThenInclude(a => a.AddOnItem)
            .AsQueryable();
        var signupQuery = _db.OpenPlaySignups
            .Where(sg => courtIds.Contains(sg.CourtId))
            .Include(sg => sg.Court).Include(sg => sg.User)
            .AsQueryable();

        if (exactDate.HasValue)
        {
            query = query.Where(b => b.BookingDate == exactDate.Value);
            signupQuery = signupQuery.Where(sg => sg.BookingDate == exactDate.Value);
        }
        if (exactStatus.HasValue)
        {
            query = query.Where(b => b.Status == exactStatus.Value);
            signupQuery = signupQuery.Where(sg => sg.Status == exactStatus.Value);
        }
        if (excludeCancelled)
        {
            query = query.Where(b => b.Status != BookingStatus.Cancelled);
            signupQuery = signupQuery.Where(sg => sg.Status != BookingStatus.Cancelled);
        }

        var bookings = await query.ToListAsync();
        var signups  = await signupQuery.ToListAsync();

        var staffIds = bookings.Where(b => b.LoggedByStaffId != null).Select(b => b.LoggedByStaffId!)
            .Concat(signups.Where(sg => sg.LoggedByStaffId != null).Select(sg => sg.LoggedByStaffId!))
            .Distinct().ToList();
        var staffNames = await _db.Users.Where(u => staffIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        var rows = bookings.Select(b => new AdminBookingRow
        {
            Id = b.Id,
            IsOpenPlay = false,
            CustomerName = b.CustomerNameSnapshot ?? b.User.FullName,
            CustomerPhone = b.User.PhoneNumber,
            IsGuest = b.User.IsGuest,
            CourtName = b.Court.Name,
            BookingDate = b.BookingDate,
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            CreatedAt = b.CreatedAt,
            TotalPrice = b.TotalPrice,
            Status = b.Status,
            PaymentStatus = b.PaymentStatus,
            HasPaymentProof = b.HasPaymentProof,
            PaymentMethod = b.PaymentMethod,
            BookedByStaffName = b.LoggedByStaffId != null && staffNames.TryGetValue(b.LoggedByStaffId, out var sn) ? sn : null,
            AddOnsTotal = b.AddOns.Sum(a => a.Quantity * a.UnitPrice),
            AddOnsSummary = b.AddOns.Any() ? string.Join(", ", b.AddOns.Select(a => $"{a.Quantity}x {a.AddOnItem.Name}")) : null,
            PaymentProofPath = b.PaymentProofPath
        }).ToList();

        rows.AddRange(signups.Select(sg => new AdminBookingRow
        {
            Id = sg.Id,
            IsOpenPlay = true,
            CustomerName = sg.CustomerNameSnapshot ?? sg.User.FullName,
            CustomerPhone = sg.User.PhoneNumber,
            IsGuest = sg.User.IsGuest,
            CourtName = sg.Court.Name,
            SpotCount = sg.SpotCount,
            PlayerNames = sg.PlayerNames,
            BookingDate = sg.BookingDate,
            StartTime = new TimeOnly(sg.StartHour % 24, 0),
            EndTime = new TimeOnly(sg.EndHour % 24, 0),
            CreatedAt = sg.CreatedAt,
            TotalPrice = sg.TotalPrice,
            Status = sg.Status,
            PaymentStatus = sg.PaymentStatus,
            HasPaymentProof = sg.HasPaymentProof,
            PaymentMethod = sg.PaymentMethod,
            BookedByStaffName = sg.LoggedByStaffId != null && staffNames.TryGetValue(sg.LoggedByStaffId, out var sgn) ? sgn : null
        }));

        return rows;
    }

    // ── Walk-in booking: pick a court/date, then a slot ──────────────────────

    public async Task<IActionResult> NewWalkIn(int? courtId, DateTime? date)
    {
        var myCourts = await (await MyCourtsAsync()).Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        ViewBag.Courts = myCourts;

        // Same house-rules / schedule-and-pricing reference customers see on the public
        // facility page, so staff have full context on this page instead of a bare court
        // dropdown — front desk shouldn't need a second tab open to answer a pricing question.
        var employerOwnerId = await GetEmployerOwnerIdAsync();
        ViewBag.Settings = employerOwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == employerOwnerId)
            : null;
        ViewBag.RateRanges = await _bookingService.GetRateRangesAsync(myCourts);
        var myCourtIds = myCourts.Select(c => c.Id).ToList();
        ViewBag.CourtRateTiers = await _db.CourtRateTiers
            .Where(t => myCourtIds.Contains(t.CourtId))
            .OrderBy(t => t.CourtId).ThenBy(t => t.StartHour)
            .ToListAsync();

        // "Today" must resolve to Philippine time regardless of the server's own OS timezone
        // (e.g. a UTC-hosted server) — otherwise, during PH midnight-8am, defaulting to the
        // server's local "today" silently books the wrong calendar date.
        var todayPht = PhtClock.Today;

        if (courtId is null)
        {
            ViewBag.SelectedDate = date.HasValue ? DateOnly.FromDateTime(date.Value) : todayPht;
            return View();
        }

        var court = myCourts.FirstOrDefault(c => c.Id == courtId);
        if (court is null) return NotFound();

        var selectedDate = date.HasValue ? DateOnly.FromDateTime(date.Value) : todayPht;

        var slots = await _db.CourtTimeSlots
            .Where(s => s.CourtId == courtId && s.IsActive && s.SlotDate == selectedDate)
            .OrderBy(s => s.StartHour)
            .ToListAsync();

        var vm = new CourtAvailabilityViewModel { Court = court, Date = selectedDate };
        (vm.RateRangeMin, vm.RateRangeMax) = await _bookingService.GetRateRangeAsync(court);

        if (slots.Any())
        {
            vm.TimeSlots = slots;
            vm.UnavailableSlotIds = await _bookingService.GetUnavailableSlotIdsAsync(courtId.Value, selectedDate, slots);
            foreach (var s in slots)
            {
                vm.SlotPrices[s.Id] = await _bookingService.GetTotalPriceAsync(
                    court, selectedDate, new TimeOnly(s.StartHour % 24, 0), new TimeOnly(s.EndHour % 24, 0));
            }
        }
        else
        {
            var bookedHours  = await _bookingService.GetBookedHoursAsync(courtId.Value, selectedDate);
            var pendingHours = await _bookingService.GetPendingHoursAsync(courtId.Value, selectedDate);
            var pendingBundleWindows = await _bookingService.GetPendingBundleWindowsAsync(courtId.Value, selectedDate);
            var blockedHours = await _bookingService.GetBlockedHoursAsync(courtId.Value, selectedDate);
            var blockReasons = await _bookingService.GetBlockReasonsAsync(courtId.Value, selectedDate);
            var schedule     = await _bookingService.GetHourlyScheduleAsync(court, selectedDate);

            var bundleOnlyHours = new Dictionary<int, (CourtBundle Bundle, CourtBundleRateBlock Block)>();
            var openPlaySignupInfo = new Dictionary<int, (CourtScheduleBlock Block, int SpotsRemaining)>();
            for (int h = court.OpeningHour; h < court.ClosingHour; h++)
            {
                var match = await _bookingService.ResolveBundleForHourAsync(court, selectedDate, h);
                if (match is not null) { bundleOnlyHours[h] = match.Value; continue; }

                if (schedule.TryGetValue(h, out var sh) && sh.Type == BookingType.AdminHostedOpenPlay)
                {
                    var block = await _bookingService.ResolveScheduleBlockForHourAsync(court, selectedDate, h);
                    if (block is not null)
                    {
                        // Staff can always register a walk-in into an Open Play block, regardless of
                        // whether online self-signup is enabled for customers — a null result (no
                        // MaxPlayers cap configured) is represented here as "unlimited" (int.MaxValue)
                        // so the view can merge the whole block into one always-clickable tile.
                        var spotsRemaining = await _bookingService.GetOpenPlaySpotsRemainingForStaffAsync(block, courtId.Value, selectedDate);
                        openPlaySignupInfo[h] = (block, spotsRemaining ?? int.MaxValue);
                    }
                }
            }

            vm.BookedHours     = bookedHours;
            vm.PendingHours    = pendingHours;
            vm.PendingBundleWindows = pendingBundleWindows;
            vm.BlockedHours    = blockedHours;
            vm.BlockReasons    = blockReasons;
            vm.BundleOnlyHours = bundleOnlyHours;
            vm.OpenPlaySignupInfo = openPlaySignupInfo;
            vm.OpenPlayHours   = schedule
                .Where(kv => kv.Value.Type == BookingType.AdminHostedOpenPlay && !bundleOnlyHours.ContainsKey(kv.Key))
                .Select(kv => kv.Key).ToList();
            vm.HourlyRates    = schedule.ToDictionary(kv => kv.Key, kv => kv.Value.Rate);
            vm.AvailableHours = Enumerable
                .Range(court.OpeningHour, court.ClosingHour - court.OpeningHour)
                .Where(h => !bookedHours.Contains(h) && !pendingHours.Contains(h) && !blockedHours.Contains(h)
                         && !vm.OpenPlayHours.Contains(h) && !bundleOnlyHours.ContainsKey(h))
                .ToList();
        }

        return View(vm);
    }

    // ── Walk-in booking: confirm customer info + duration for a chosen hour ──

    public async Task<IActionResult> WalkInForm(int courtId, DateOnly date, int startHour, int? endHour)
    {
        var court = await (await MyCourtsAsync()).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        int? fixedEndHour = null;
        if (endHour.HasValue)
        {
            if (endHour.Value <= startHour || endHour.Value > 24)
            {
                TempData["Error"] = "Invalid time slot.";
                return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
            }

            bool slotExists = await _db.CourtTimeSlots.AnyAsync(s =>
                s.CourtId == courtId
                && s.SlotDate == date
                && s.IsActive
                && s.StartHour == startHour
                && s.EndHour == endHour.Value);
            if (!slotExists)
            {
                TempData["Error"] = "This time slot is no longer available.";
                return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
            }

            fixedEndHour = endHour.Value;
        }

        ViewBag.Court     = court;
        ViewBag.Date      = date;
        ViewBag.StartHour = startHour;
        ViewBag.FixedEndHour = fixedEndHour;
        ViewBag.TotalPrice = await _bookingService.GetTotalPriceAsync(
            court,
            date,
            new TimeOnly(startHour % 24, 0),
            new TimeOnly((fixedEndHour ?? (startHour + 1)) % 24, 0));

        var employerOwnerId = await GetEmployerOwnerIdAsync();
        ViewBag.AddOns = employerOwnerId != null
            ? await _bookingService.GetActiveAddOnsAsync(employerOwnerId)
            : new List<AddOnItem>();

        ViewBag.PaymentMethods = await GetAvailablePaymentMethodsAsync(employerOwnerId);

        return View();
    }

    private async Task<List<string>> GetAvailablePaymentMethodsAsync(string? employerOwnerId)
    {
        var settings = employerOwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == employerOwnerId)
            : null;
        var methods = new List<string> { "Cash" };
        if (!string.IsNullOrWhiteSpace(settings?.GCashNumber)) methods.Add("GCash");
        if (!string.IsNullOrWhiteSpace(settings?.MayaNumber)) methods.Add("Maya");
        if (!string.IsNullOrWhiteSpace(settings?.GoTymeNumber)) methods.Add("GoTyme");
        return methods;
    }

    /// <summary>Saves an optional payment-proof screenshot (JPG/PNG/WebP) for a GCash/Maya/GoTyme walk-in or
    /// Open Play sign-up. Returns the stored relative path, or null if no file was provided. Throws
    /// <see cref="InvalidOperationException"/> (caught by the caller) if the file type isn't allowed.</summary>
    private async Task<string?> SavePaymentProofAsync(IFormFile? paymentProof, string prefix)
    {
        if (paymentProof is not { Length: > 0 }) return null;

        var ext = Path.GetExtension(paymentProof.FileName).ToLower();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw new InvalidOperationException("Payment proof must be JPG, PNG, or WebP.");

        var uploadsDir = Path.Combine(UploadsRoot, "uploads", "proofs");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"{prefix}_{Guid.NewGuid():N}.jpg";
        var fullPath = Path.Combine(uploadsDir, fileName);
        byte[] compressed;
        try
        {
            await using var source = paymentProof.OpenReadStream();
            compressed = await _imageCompression.CompressAsync(source);
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            throw new InvalidOperationException("That file doesn't look like a valid image. Please upload a JPG, PNG, or WebP screenshot.");
        }
        await System.IO.File.WriteAllBytesAsync(fullPath, compressed);
        return $"/uploads/proofs/{fileName}";
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWalkIn(
        int courtId, DateOnly date, int startHour, int durationHours, string customerName, string customerEmail, string customerPhone,
        string paymentMethod, string? paymentReference, IFormFile? paymentProof, string? notes, int? fixedEndHour)
    {
        var court = await (await MyCourtsAsync()).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerEmail) || string.IsNullOrWhiteSpace(customerPhone))
        {
            TempData["Error"] = "Customer name, email, and phone are required.";
            return RedirectToAction(nameof(WalkInForm), new { courtId, date, startHour, endHour = fixedEndHour });
        }
        if (!new EmailAddressAttribute().IsValid(customerEmail))
        {
            TempData["Error"] = "Please enter a valid email address.";
            return RedirectToAction(nameof(WalkInForm), new { courtId, date, startHour, endHour = fixedEndHour });
        }

        if (fixedEndHour.HasValue)
        {
            if (fixedEndHour.Value <= startHour || fixedEndHour.Value > 24)
            {
                TempData["Error"] = "Invalid time slot.";
                return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
            }

            bool slotExists = await _db.CourtTimeSlots.AnyAsync(s =>
                s.CourtId == courtId
                && s.SlotDate == date
                && s.IsActive
                && s.StartHour == startHour
                && s.EndHour == fixedEndHour.Value);
            if (!slotExists)
            {
                TempData["Error"] = "This time slot is no longer available. Please choose another time.";
                return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
            }

            durationHours = fixedEndHour.Value - startHour;
        }
        if (durationHours < 1) durationHours = 1;
        if (string.IsNullOrWhiteSpace(paymentMethod)) paymentMethod = "Cash";

        var startTime = new TimeOnly(startHour % 24, 0);
        var endTime   = new TimeOnly((startHour + durationHours) % 24, 0);

        var available = await _bookingService.IsSlotAvailableAsync(courtId, date, startTime, endTime);
        if (!available)
        {
            TempData["Error"] = "This time slot is no longer available. Please choose another time.";
            return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
        }
        if (await _bookingService.HasOpenPlayHoursAsync(court, date, startTime, endTime))
        {
            TempData["Error"] = "This time is reserved for Admin-Hosted Open Play and isn't available for direct booking.";
            return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
        }
        if (await _bookingService.HasBundleOnlyHoursAsync(court, date, startTime, endTime))
        {
            TempData["Error"] = "This time is only available as part of a bundled booking.";
            return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
        }

        ApplicationUser customer;
        try
        {
            customer = await _guestCheckout.GetOrCreateGuestUserAsync(customerName, customerEmail.Trim(), customerPhone);
        }
        catch (Exception ex) when (ex is GuestEmailConflictException or InvalidOperationException)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(WalkInForm), new { courtId, date, startHour, endHour = fixedEndHour });
        }

        var totalPrice = await _bookingService.GetTotalPriceAsync(court, date, startTime, endTime);

        var employerOwnerId = await GetEmployerOwnerIdAsync();
        var (addOns, addOnsTotal) = employerOwnerId != null
            ? await _bookingService.ResolveSelectedAddOnsAsync(employerOwnerId, Request.Form, durationHours)
            : (new List<BookingAddOn>(), 0m);

        // Optional proof-of-payment screenshot for GCash/Maya walk-ins — not required (staff has
        // already confirmed the payment in person), but kept for the owner's records if provided.
        string? proofPath;
        try
        {
            proofPath = await SavePaymentProofAsync(paymentProof, "walkin");
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = $"{ex.Message} — booking was not created.";
            return RedirectToAction(nameof(WalkInForm), new { courtId, date, startHour });
        }

        // Cash is taken and verified in person, so it's confirmed on the spot. Every other
        // method (GCash/Maya/GoTyme/etc.) is just a screen staff glanced at — same as a customer's
        // own screenshot, it still needs the facility owner to actually confirm the payment before
        // the slot counts as booked. Until then it sits Pending, which already blocks the slot for
        // everyone else (see BookingService.IsSlotAvailableAsync) and shows as "Pending" on the grid.
        bool isCash = IsCashPayment(paymentMethod);

        var booking = new Booking
        {
            CourtId       = courtId,
            UserId        = customer.Id,
            FacilityName  = court.FacilityName,
            BookingDate   = date,
            StartTime     = startTime,
            EndTime       = endTime,
            TotalPrice    = totalPrice + addOnsTotal,
            Notes         = notes,
            Status        = isCash ? BookingStatus.Confirmed : BookingStatus.Pending,
            PaymentStatus = isCash ? PaymentStatus.Paid : PaymentStatus.Unpaid,
            PaymentMethod = paymentMethod,
            PaymentReference = paymentReference,
            PaymentProofPath = proofPath,
            PaymentProofSubmittedAt = isCash ? null : DateTime.UtcNow,
            PaidAt        = isCash ? DateTime.UtcNow : null,
            LoggedByStaffId = CurrentStaffId,
            CustomerNameSnapshot = customerName,
            AddOns        = addOns
        };
        await _bookingService.CreateBookingAsync(booking);

        var customerEmailToNotify = customerEmail.Trim();
        if (isCash)
        {
            // Cash walk-ins are immediately confirmed/paid, so send the same customer-facing
            // confirmation right away (including for advance bookings).
            if (!string.IsNullOrWhiteSpace(customerEmailToNotify))
            {
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                await _email.SendBookingConfirmedToCustomerAsync(
                    customerEmailToNotify,
                    customer.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                    booking.Id,
                    court.Name,
                    booking.BookingDate,
                    booking.StartTime,
                    booking.EndTime,
                    booking.TotalPrice,
                    booking.PaymentMethod,
                    booking.PaymentReference,
                    baseUrl,
                    isGuest: customer.IsGuest);
            }
            TempData["Success"] = $"Booked {court.Name} for {customerName} ({TimeDisplay.HourRange(startHour, startHour + durationHours)}) — ₱{booking.TotalPrice:N0} via {paymentMethod} logged.";
        }
        else
        {
            var owner = employerOwnerId != null ? await _userManager.FindByIdAsync(employerOwnerId) : null;
            await SendWalkInPaymentSubmittedNotificationAsync(new List<Booking> { booking }, new List<Court> { court }, owner);
            TempData["Success"] = $"Logged {court.Name} for {customerName} ({TimeDisplay.HourRange(startHour, startHour + durationHours)}) — ₱{booking.TotalPrice:N0} via {paymentMethod}, pending confirmation.";
        }

        return RedirectToAction(nameof(Index));
    }

    private static bool IsCashPayment(string? paymentMethod) =>
        string.IsNullOrWhiteSpace(paymentMethod) || string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tells the facility owner a staff member logged a non-cash walk-in payment that's sitting
    /// Pending until they confirm it — mirrors <c>SendBundleProofSubmittedNotificationAsync</c>'s
    /// "please review" role for customer-submitted proof, just triggered by staff instead.
    /// </summary>
    private async Task SendWalkInPaymentSubmittedNotificationAsync(List<Booking> bookings, List<Court> courts, ApplicationUser? owner)
    {
        try
        {
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email)) return;

            var courtsById  = courts.ToDictionary(c => c.Id);
            var baseUrl     = $"{Request.Scheme}://{Request.Host}";
            var bookingsUrl = $"{baseUrl}/Admin/Bookings?awaitingConfirmation=true";
            var first       = bookings[0];
            var amount      = bookings.Sum(b => b.TotalPrice).ToString("N0");
            var rowsHtml = string.Join("", bookings.Select(b =>
            {
                var courtName = courtsById.TryGetValue(b.CourtId, out var c) ? c.Name : "Court";
                return $"<tr><td style='padding:4px 0;color:#212529;'>{courtName}</td>" +
                       $"<td style='padding:4px 0;color:#6c757d;'>{b.BookingDate:MMM d, yyyy}, {b.StartTime:hh\\:mm tt} – {b.EndTime:hh\\:mm tt}</td>" +
                       $"<td style='padding:4px 0;text-align:right;font-weight:600;'>₱{b.TotalPrice:N0}</td></tr>";
            }));
            var rowsPlain = string.Join("\n", bookings.Select(b =>
            {
                var courtName = courtsById.TryGetValue(b.CourtId, out var c) ? c.Name : "Court";
                return $"- {courtName}: {b.BookingDate:MMM d, yyyy}, {b.StartTime:hh\\:mm tt} – {b.EndTime:hh\\:mm tt} (₱{b.TotalPrice:N0})";
            }));

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:560px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>🔔 Walk-in Payment Needs Confirmation</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>Your staff logged a walk-in booking paid via <strong>{first.PaymentMethod}</strong> — please confirm the payment before it's finalized:</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>{rowsHtml}</table>
      <table style='width:100%;border-collapse:collapse;font-size:14px;margin-top:12px;border-top:1px solid #e9ecef;padding-top:8px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Total</td><td style='padding:5px 0;font-weight:600;color:#198754;'>₱{amount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Customer</td><td style='padding:5px 0;'>{first.CustomerNameSnapshot}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Reference</td><td style='padding:5px 0;font-family:monospace;'>{first.PaymentReference ?? "—"}</td></tr>
      </table>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{bookingsUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Review &amp; Confirm</a>
      </p>
    </div>
  </div>
</body></html>";

            var plain = $"Walk-in Payment Needs Confirmation\n\n{rowsPlain}\n\nTotal: ₱{amount}\nCustomer: {first.CustomerNameSnapshot}\nMethod: {first.PaymentMethod}\n\nReview and confirm: {bookingsUrl}";
            await _email.SendAsync(owner.Email, "🔔 Walk-in payment needs your confirmation", html, plain);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[StaffController] Failed to send walk-in payment submitted notification");
        }
    }

    // ── Walk-in cart: log several slots (any of the employer's courts) as one paid transaction ──
    // The cart itself is client-side (localStorage, wwwroot/js/cart.js) — same mechanism the
    // customer-facing multi-court cart uses (see CartController). Unlike that flow, a staff
    // walk-in is confirmed and marked paid immediately (cash/GCash/Maya taken in person), so
    // there's no separate "Pay" screen — this form collects customer info + one payment method
    // for the whole batch and creates every booking already Confirmed/Paid in one submit.

    private const int MaxWalkInCartItems = 20;

    public async Task<IActionResult> WalkInCartForm()
    {
        var employerOwnerId = await GetEmployerOwnerIdAsync();
        ViewBag.AddOns = employerOwnerId != null
            ? await _bookingService.GetActiveAddOnsAsync(employerOwnerId)
            : new List<AddOnItem>();
        ViewBag.PaymentMethods = await GetAvailablePaymentMethodsAsync(employerOwnerId);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWalkInCart(
        string cartJson, string customerName, string customerEmail, string customerPhone,
        string paymentMethod, string? paymentReference, IFormFile? paymentProof, string? notes)
    {
        List<CartController.CartItemRequest>? items;
        try
        {
            items = System.Text.Json.JsonSerializer.Deserialize<List<CartController.CartItemRequest>>(cartJson ?? "[]",
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException)
        {
            items = null;
        }

        if (items is null || items.Count == 0)
        {
            TempData["Error"] = "The cart is empty.";
            return RedirectToAction(nameof(WalkInCartForm));
        }
        if (items.Count > MaxWalkInCartItems)
        {
            TempData["Error"] = $"A cart can hold at most {MaxWalkInCartItems} slots. Please log in smaller batches.";
            return RedirectToAction(nameof(WalkInCartForm));
        }
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerEmail) || string.IsNullOrWhiteSpace(customerPhone))
        {
            TempData["Error"] = "Customer name, email, and phone are required.";
            return RedirectToAction(nameof(WalkInCartForm));
        }
        if (!new EmailAddressAttribute().IsValid(customerEmail))
        {
            TempData["Error"] = "Please enter a valid email address.";
            return RedirectToAction(nameof(WalkInCartForm));
        }
        if (string.IsNullOrWhiteSpace(paymentMethod)) paymentMethod = "Cash";

        var myCourts = await (await MyCourtsAsync()).ToListAsync();
        var courtIds = items.Select(i => i.CourtId).Distinct().ToList();
        var courtsById = myCourts.Where(c => courtIds.Contains(c.Id)).ToDictionary(c => c.Id);

        var errors = new List<string>();
        foreach (var item in items)
        {
            if (!courtsById.ContainsKey(item.CourtId))
                errors.Add($"Court #{item.CourtId} does not belong to your facility.");
        }
        if (errors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", errors);
            return RedirectToAction(nameof(WalkInCartForm));
        }

        // Re-validate availability & recompute price server-side for every item — same guards
        // as CreateWalkIn, just looped. Abort without creating anything if any single item fails.
        var resolved = new List<(CartController.CartItemRequest Item, Court Court, TimeOnly Start, TimeOnly End, decimal SlotPrice)>();
        foreach (var item in items)
        {
            var court = courtsById[item.CourtId];
            var start = new TimeOnly(item.StartHour % 24, 0);
            var end   = new TimeOnly(item.EndHour % 24, 0);

            if (item.EndHour <= item.StartHour || item.StartHour < court.OpeningHour || item.EndHour > court.ClosingHour)
            {
                errors.Add($"{court.Name} on {item.Date:MMM d} falls outside operating hours.");
                continue;
            }
            if (!await _bookingService.IsSlotAvailableAsync(court.Id, item.Date, start, end))
            {
                errors.Add($"{court.Name} on {item.Date:MMM d} at {TimeDisplay.Hour(item.StartHour)} is no longer available.");
                continue;
            }
            if (await _bookingService.HasOpenPlayHoursAsync(court, item.Date, start, end))
            {
                errors.Add($"{court.Name} on {item.Date:MMM d} at {TimeDisplay.Hour(item.StartHour)} is reserved for Admin-Hosted Open Play.");
                continue;
            }
            if (await _bookingService.HasBundleOnlyHoursAsync(court, item.Date, start, end))
            {
                errors.Add($"{court.Name} on {item.Date:MMM d} at {TimeDisplay.Hour(item.StartHour)} is only available as part of a bundle.");
                continue;
            }

            var price = await _bookingService.GetTotalPriceAsync(court, item.Date, start, end);
            resolved.Add((item, court, start, end, price));
        }

        if (errors.Count > 0)
        {
            TempData["Error"] = "Some slots are no longer available — please remove them and try again: " + string.Join(" ", errors);
            return RedirectToAction(nameof(WalkInCartForm));
        }

        ApplicationUser customer;
        try
        {
            customer = await _guestCheckout.GetOrCreateGuestUserAsync(customerName, customerEmail.Trim(), customerPhone);
        }
        catch (Exception ex) when (ex is GuestEmailConflictException or InvalidOperationException)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(WalkInCartForm));
        }

        string? proofPath;
        try
        {
            proofPath = await SavePaymentProofAsync(paymentProof, "walkincart");
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = $"{ex.Message} — booking was not created.";
            return RedirectToAction(nameof(WalkInCartForm));
        }

        // Same Cash-vs-everything-else rule as CreateWalkIn: cash is confirmed on the spot,
        // any other method needs the owner's confirmation before it's really booked.
        bool isCash = IsCashPayment(paymentMethod);

        var employerOwnerId = await GetEmployerOwnerIdAsync();
        var groupId = Guid.NewGuid();
        var bookings = new List<Booking>();

        foreach (var (item, court, start, end, slotPrice) in resolved)
        {
            var (addOns, addOnsTotal) = employerOwnerId != null
                ? await _bookingService.ResolveAddOnsAsync(
                    employerOwnerId,
                    (item.AddOns ?? new List<CartController.CartAddOnRequest>())
                        .Select(a => new BookingService.AddOnSelection(a.AddOnItemId, a.Quantity, a.Hours)),
                    item.EndHour - item.StartHour)
                : (new List<BookingAddOn>(), 0m);

            bookings.Add(new Booking
            {
                CourtId              = court.Id,
                UserId               = customer.Id,
                FacilityName         = court.FacilityName,
                BookingDate          = item.Date,
                StartTime            = start,
                EndTime              = end,
                TotalPrice           = slotPrice + addOnsTotal,
                Notes                = notes,
                Status               = isCash ? BookingStatus.Confirmed : BookingStatus.Pending,
                PaymentStatus        = isCash ? PaymentStatus.Paid : PaymentStatus.Unpaid,
                PaymentMethod        = paymentMethod,
                PaymentReference     = paymentReference,
                PaymentProofPath     = proofPath,
                PaymentProofSubmittedAt = isCash ? null : DateTime.UtcNow,
                PaidAt               = isCash ? DateTime.UtcNow : null,
                LoggedByStaffId      = CurrentStaffId,
                BundleGroupId        = groupId,
                CustomerNameSnapshot = customerName,
                AddOns               = addOns
            });
        }

        _db.Bookings.AddRange(bookings);
        await _db.SaveChangesAsync();

        var customerEmailToNotify = customerEmail.Trim();
        if (isCash)
        {
            // Cash walk-ins are immediately confirmed/paid — send one confirmation per booking,
            // reusing the same per-booking template the single-slot walk-in already sends.
            if (!string.IsNullOrWhiteSpace(customerEmailToNotify))
            {
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                foreach (var booking in bookings)
                {
                    var court = courtsById[booking.CourtId];
                    await _email.SendBookingConfirmedToCustomerAsync(
                        customerEmailToNotify,
                        customer.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                        booking.Id, court.Name, booking.BookingDate, booking.StartTime, booking.EndTime,
                        booking.TotalPrice, booking.PaymentMethod, booking.PaymentReference, baseUrl,
                        isGuest: customer.IsGuest);
                }
            }
        }
        else
        {
            var owner = employerOwnerId != null ? await _userManager.FindByIdAsync(employerOwnerId) : null;
            await SendWalkInPaymentSubmittedNotificationAsync(bookings, courtsById.Values.ToList(), owner);
        }

        var grandTotal = bookings.Sum(b => b.TotalPrice);
        TempData["Success"] = isCash
            ? $"Logged {bookings.Count} slot{(bookings.Count == 1 ? "" : "s")} for {customerName} — ₱{grandTotal:N0} via {paymentMethod}."
            : $"Logged {bookings.Count} slot{(bookings.Count == 1 ? "" : "s")} for {customerName} — ₱{grandTotal:N0} via {paymentMethod}, pending confirmation.";
        TempData["ClearCart"] = true;
        return RedirectToAction(nameof(Index));
    }

    // ── Standalone add-on rentals ─────────────────────────────────────────────
    // For a customer who just wants to rent add-ons (paddles, shuttlecocks, etc.) at the
    // counter without booking a court or joining Open Play. No time slot/court/date involved —
    // this is a simple point-of-sale style transaction, same payment lifecycle as a walk-in
    // (Cash confirmed on the spot, everything else Pending until the owner confirms).

    public async Task<IActionResult> RentAddOns()
    {
        var employerOwnerId = await GetEmployerOwnerIdAsync();
        ViewBag.AddOns = employerOwnerId != null
            ? await _bookingService.GetActiveAddOnsAsync(employerOwnerId)
            : new List<AddOnItem>();
        ViewBag.PaymentMethods = await GetAvailablePaymentMethodsAsync(employerOwnerId);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAddOnRental(
        string customerName, string customerEmail, string customerPhone,
        string paymentMethod, string? paymentReference, IFormFile? paymentProof, string? notes)
    {
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerEmail) || string.IsNullOrWhiteSpace(customerPhone))
        {
            TempData["Error"] = "Customer name, email, and phone are required.";
            return RedirectToAction(nameof(RentAddOns));
        }
        if (!new EmailAddressAttribute().IsValid(customerEmail))
        {
            TempData["Error"] = "Please enter a valid email address.";
            return RedirectToAction(nameof(RentAddOns));
        }
        if (string.IsNullOrWhiteSpace(paymentMethod)) paymentMethod = "Cash";

        var employerOwnerId = await GetEmployerOwnerIdAsync();
        if (employerOwnerId is null) return Forbid();

        var (items, total) = await _bookingService.ResolveSelectedAddOnRentalItemsAsync(employerOwnerId, Request.Form);
        if (items.Count == 0)
        {
            TempData["Error"] = "Select at least one add-on item and quantity.";
            return RedirectToAction(nameof(RentAddOns));
        }

        ApplicationUser customer;
        try
        {
            customer = await _guestCheckout.GetOrCreateGuestUserAsync(customerName, customerEmail.Trim(), customerPhone);
        }
        catch (Exception ex) when (ex is GuestEmailConflictException or InvalidOperationException)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(RentAddOns));
        }

        string? proofPath;
        try
        {
            proofPath = await SavePaymentProofAsync(paymentProof, "addonrental");
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = $"{ex.Message} — sale was not logged.";
            return RedirectToAction(nameof(RentAddOns));
        }

        // Same Cash-vs-everything-else rule as the walk-in flows: cash is confirmed on the spot,
        // any other method sits Pending until the owner confirms it actually came through.
        bool isCash = IsCashPayment(paymentMethod);

        var rental = new AddOnRental
        {
            OwnerId              = employerOwnerId,
            UserId               = customer.Id,
            CustomerNameSnapshot = customerName,
            TotalPrice           = total,
            Notes                = notes,
            Status               = isCash ? BookingStatus.Confirmed : BookingStatus.Pending,
            PaymentStatus        = isCash ? PaymentStatus.Paid : PaymentStatus.Unpaid,
            PaymentMethod        = paymentMethod,
            PaymentReference     = paymentReference,
            PaymentProofPath     = proofPath,
            PaidAt               = isCash ? DateTime.UtcNow : null,
            LoggedByStaffId      = CurrentStaffId,
            Items                = items
        };
        _db.AddOnRentals.Add(rental);
        await _db.SaveChangesAsync();

        if (!isCash)
        {
            var owner = await _userManager.FindByIdAsync(employerOwnerId);
            await SendAddOnRentalPaymentSubmittedNotificationAsync(rental, owner);
        }

        TempData["Success"] = isCash
            ? $"Logged add-on rental for {customerName} — ₱{total:N0} via {paymentMethod}."
            : $"Logged add-on rental for {customerName} — ₱{total:N0} via {paymentMethod}, pending confirmation.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Tells the facility owner a staff member logged a non-cash add-on rental that's
    /// sitting Pending until they confirm it — same idea as <see cref="SendWalkInPaymentSubmittedNotificationAsync"/>.</summary>
    private async Task SendAddOnRentalPaymentSubmittedNotificationAsync(AddOnRental rental, ApplicationUser? owner)
    {
        try
        {
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email)) return;

            var baseUrl  = $"{Request.Scheme}://{Request.Host}";
            var reviewUrl = $"{baseUrl}/Admin/AddOns";
            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:560px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>🔔 Add-on Rental Needs Confirmation</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>Your staff logged an add-on rental paid via <strong>{rental.PaymentMethod}</strong> — please confirm the payment before it's finalized:</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Customer</td><td style='padding:5px 0;'>{rental.CustomerNameSnapshot}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Amount</td><td style='padding:5px 0;font-weight:600;color:#198754;'>₱{rental.TotalPrice:N0}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Reference</td><td style='padding:5px 0;font-family:monospace;'>{rental.PaymentReference ?? "—"}</td></tr>
      </table>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{reviewUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Review &amp; Confirm</a>
      </p>
    </div>
  </div>
</body></html>";
            var plain = $"Add-on Rental Needs Confirmation\n\nCustomer: {rental.CustomerNameSnapshot}\nAmount: ₱{rental.TotalPrice:N0}\nMethod: {rental.PaymentMethod}\n\nReview and confirm: {reviewUrl}";
            await _email.SendAsync(owner.Email, "🔔 Add-on rental needs your confirmation", html, plain);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[StaffController] Failed to send add-on rental payment submitted notification");
        }
    }

    // ── Walk-in bundle booking ────────────────────────────────────────────────
    // A bundle sells one of its member courts at a flat price during a recurring
    // peak window (see BundleBookingsController for the customer-facing version).
    // Mirrors WalkInForm/CreateWalkIn above, but priced from the CourtBundleRateBlock
    // instead of the court's normal hourly rate, and with no duration picker since
    // the window is fixed by the bundle's own schedule.

    public async Task<IActionResult> WalkInBundleForm(int bundleId, int courtId, DateOnly date, int startHour, int endHour)
    {
        var court = await (await MyCourtsAsync()).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        var bundle = await _db.CourtBundles
            .Include(b => b.Courts).ThenInclude(c => c.Court)
            .FirstOrDefaultAsync(b => b.Id == bundleId && b.IsActive && b.Courts.Any(c => c.CourtId == courtId));
        if (bundle is null) return NotFound();

        var block = await ResolveWalkInBundleBlockAsync(court, bundleId, date, startHour, endHour);
        if (block is null)
        {
            TempData["Error"] = "This bundle window is no longer available.";
            return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
        }

        ViewBag.Bundle     = bundle;
        ViewBag.Court      = court;
        ViewBag.Date       = date;
        ViewBag.StartHour  = startHour;
        ViewBag.EndHour    = endHour;
        ViewBag.TotalPrice = block.FlatPrice;

        var employerOwnerId = await GetEmployerOwnerIdAsync();
        ViewBag.PaymentMethods = await GetAvailablePaymentMethodsAsync(employerOwnerId);

        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWalkInBundle(
        int bundleId, int courtId, DateOnly date, int startHour, int endHour,
        string customerName, string customerEmail, string customerPhone,
        string paymentMethod, string? paymentReference, IFormFile? paymentProof, string? notes)
    {
        var court = await (await MyCourtsAsync()).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerEmail) || string.IsNullOrWhiteSpace(customerPhone))
        {
            TempData["Error"] = "Customer name, email, and phone are required.";
            return RedirectToAction(nameof(WalkInBundleForm), new { bundleId, courtId, date, startHour, endHour });
        }
        if (!new EmailAddressAttribute().IsValid(customerEmail))
        {
            TempData["Error"] = "Please enter a valid email address.";
            return RedirectToAction(nameof(WalkInBundleForm), new { bundleId, courtId, date, startHour, endHour });
        }

        var bundle = await _db.CourtBundles
            .Include(b => b.Courts).ThenInclude(c => c.Court)
            .FirstOrDefaultAsync(b => b.Id == bundleId && b.IsActive && b.Courts.Any(c => c.CourtId == courtId));
        if (bundle is null) return NotFound();

        var block = await ResolveWalkInBundleBlockAsync(court, bundleId, date, startHour, endHour);
        if (block is null)
        {
            TempData["Error"] = "This bundle window is no longer available.";
            return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
        }

        if (string.IsNullOrWhiteSpace(paymentMethod)) paymentMethod = "Cash";

        // EndHour can be 24 (midnight for an overnight window) — TimeOnly only accepts
        // 0-23, so wrap the same way BundleBookingsController does.
        var start = new TimeOnly(startHour % 24, 0);
        var end   = new TimeOnly(endHour % 24, 0);

        if (!await _bookingService.IsSlotAvailableAsync(courtId, date, start, end))
        {
            TempData["Error"] = "This time slot is no longer available. Please choose another time.";
            return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
        }

        ApplicationUser customer;
        try
        {
            customer = await _guestCheckout.GetOrCreateGuestUserAsync(customerName, customerEmail.Trim(), customerPhone);
        }
        catch (Exception ex) when (ex is GuestEmailConflictException or InvalidOperationException)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(WalkInBundleForm), new { bundleId, courtId, date, startHour, endHour });
        }

        string? proofPath;
        try
        {
            proofPath = await SavePaymentProofAsync(paymentProof, "walkin_bundle");
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = $"{ex.Message} — booking was not created.";
            return RedirectToAction(nameof(WalkInBundleForm), new { bundleId, courtId, date, startHour, endHour });
        }

        bool isCash = IsCashPayment(paymentMethod);

        var booking = new Booking
        {
            CourtId              = courtId,
            UserId               = customer.Id,
            FacilityName         = court.FacilityName,
            BookingDate          = date,
            StartTime            = start,
            EndTime              = end,
            TotalPrice           = block.FlatPrice,
            Notes                = notes,
            Status               = isCash ? BookingStatus.Confirmed : BookingStatus.Pending,
            PaymentStatus        = isCash ? PaymentStatus.Paid : PaymentStatus.Unpaid,
            PaymentMethod        = paymentMethod,
            PaymentReference     = paymentReference,
            PaymentProofPath     = proofPath,
            PaymentProofSubmittedAt = isCash ? null : DateTime.UtcNow,
            PaidAt               = isCash ? DateTime.UtcNow : null,
            LoggedByStaffId      = CurrentStaffId,
            CustomerNameSnapshot = customerName,
            CourtBundleId        = bundle.Id,
            BundleGroupId        = Guid.NewGuid()
        };
        await _bookingService.CreateBookingAsync(booking);

        var customerEmailToNotify = customerEmail.Trim();
        if (isCash)
        {
            if (!string.IsNullOrWhiteSpace(customerEmailToNotify))
            {
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                await _email.SendBookingConfirmedToCustomerAsync(
                    customerEmailToNotify,
                    customer.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                    booking.Id,
                    court.Name,
                    booking.BookingDate,
                    booking.StartTime,
                    booking.EndTime,
                    booking.TotalPrice,
                    booking.PaymentMethod,
                    booking.PaymentReference,
                    baseUrl,
                    isGuest: customer.IsGuest);
            }
            TempData["Success"] = $"Booked {court.Name} ({bundle.Name}) for {customerName} ({TimeDisplay.HourRange(startHour, endHour)}) — ₱{booking.TotalPrice:N0} via {paymentMethod} logged.";
        }
        else
        {
            var employerOwnerId = await GetEmployerOwnerIdAsync();
            var owner = employerOwnerId != null ? await _userManager.FindByIdAsync(employerOwnerId) : null;
            await SendWalkInPaymentSubmittedNotificationAsync(new List<Booking> { booking }, new List<Court> { court }, owner);
            TempData["Success"] = $"Logged {court.Name} ({bundle.Name}) for {customerName} ({TimeDisplay.HourRange(startHour, endHour)}) — ₱{booking.TotalPrice:N0} via {paymentMethod}, pending confirmation.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<CourtBundleRateBlock?> ResolveWalkInBundleBlockAsync(Court court, int bundleId, DateOnly date, int startHour, int endHour)
    {
        var resolved = await _bookingService.ResolveBundleForHourAsync(court, date, startHour);
        return resolved is not null
            && resolved.Value.Bundle.Id == bundleId
            && resolved.Value.Block.StartHour == startHour
            && resolved.Value.Block.EndHour == endHour
            ? resolved.Value.Block
            : null;
    }

    // ── Walk-in Open Play sign-up ────────────────────────────────────────────

    public async Task<IActionResult> OpenPlayForm(int courtId, DateOnly date, int startHour, int endHour)
    {
        var court = await (await MyCourtsAsync()).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        // Staff can register a walk-in into any Admin-Hosted Open Play block regardless of whether
        // online self-signup is enabled for customers — unlike the customer-facing sign-up flow,
        // this deliberately does not gate on block.AllowPublicSignup.
        var block = await _bookingService.ResolveScheduleBlockForHourAsync(court, date, startHour);
        if (block is null || block.StartHour != startHour || block.EndHour != endHour)
            return NotFound();

        var spotsRemaining = await _bookingService.GetOpenPlaySpotsRemainingForStaffAsync(block, courtId, date);
        ViewBag.Court          = court;
        ViewBag.Block          = block;
        ViewBag.Date           = date;
        ViewBag.SpotsRemaining = spotsRemaining; // null = unlimited (no MaxPlayers cap configured)
        ViewBag.PaymentMethods = await GetAvailablePaymentMethodsAsync(await GetEmployerOwnerIdAsync());
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOpenPlaySignup(
        int courtId, DateOnly date, int startHour, int endHour, int spotCount, string customerName, string customerEmail, string customerPhone,
        string paymentMethod, string? paymentReference, IFormFile? paymentProof, string? playerNames, string? notes)
    {
        var court = await (await MyCourtsAsync()).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerEmail) || string.IsNullOrWhiteSpace(customerPhone))
        {
            TempData["Error"] = "Customer name, email, and phone are required.";
            return RedirectToAction(nameof(OpenPlayForm), new { courtId, date, startHour, endHour });
        }
        if (!new EmailAddressAttribute().IsValid(customerEmail))
        {
            TempData["Error"] = "Please enter a valid email address.";
            return RedirectToAction(nameof(OpenPlayForm), new { courtId, date, startHour, endHour });
        }

        var block = await _bookingService.ResolveScheduleBlockForHourAsync(court, date, startHour);
        if (block is null || block.StartHour != startHour || block.EndHour != endHour)
        {
            TempData["Error"] = "This Open Play session is no longer available.";
            return RedirectToAction(nameof(NewWalkIn), new { courtId, date = date.ToDateTime(TimeOnly.MinValue) });
        }

        if (spotCount < 1) spotCount = 1;
        var spotsRemaining = await _bookingService.GetOpenPlaySpotsRemainingForStaffAsync(block, courtId, date);
        if (spotsRemaining.HasValue && spotCount > spotsRemaining.Value)
        {
            TempData["Error"] = $"Only {spotsRemaining.Value} spot(s) left for this session.";
            return RedirectToAction(nameof(OpenPlayForm), new { courtId, date, startHour, endHour });
        }
        if (string.IsNullOrWhiteSpace(paymentMethod)) paymentMethod = "Cash";

        ApplicationUser customer;
        try
        {
            customer = await _guestCheckout.GetOrCreateGuestUserAsync(customerName, customerEmail.Trim(), customerPhone);
        }
        catch (Exception ex) when (ex is GuestEmailConflictException or InvalidOperationException)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(OpenPlayForm), new { courtId, date, startHour, endHour });
        }

        string? proofPath;
        try
        {
            proofPath = await SavePaymentProofAsync(paymentProof, "openplay");
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = $"{ex.Message} — sign-up was not created.";
            return RedirectToAction(nameof(OpenPlayForm), new { courtId, date, startHour, endHour });
        }

        var pricePerHead = block.PricePerHead ?? 0;

        var signup = new OpenPlaySignup
        {
            CourtId              = courtId,
            FacilityName         = court.FacilityName,
            UserId               = customer.Id,
            BookingDate          = date,
            StartHour            = startHour,
            EndHour              = endHour,
            SpotCount            = spotCount,
            PricePerHeadSnapshot = pricePerHead,
            TotalPrice           = pricePerHead * spotCount,
            Notes                = notes,
            PlayerNames          = spotCount > 1 && !string.IsNullOrWhiteSpace(playerNames) ? playerNames.Trim() : null,
            Status               = BookingStatus.Confirmed,
            PaymentStatus        = PaymentStatus.Paid,
            PaymentMethod        = paymentMethod,
            PaymentReference     = paymentReference,
            PaymentProofPath     = proofPath,
            PaidAt               = DateTime.UtcNow,
            LoggedByStaffId      = CurrentStaffId,
            CustomerNameSnapshot = customerName
        };
        _db.OpenPlaySignups.Add(signup);
        await _db.SaveChangesAsync();

        var customerEmailToNotify = customerEmail.Trim();
        if (!string.IsNullOrWhiteSpace(customerEmailToNotify))
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            await _email.SendOpenPlayConfirmedToCustomerAsync(
                customerEmailToNotify,
                customer.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                signup.Id,
                court.Name,
                signup.BookingDate,
                signup.StartHour,
                signup.EndHour,
                signup.SpotCount,
                signup.TotalPrice,
                signup.PaymentMethod,
                signup.PaymentReference,
                baseUrl,
                isGuest: customer.IsGuest);
        }

        TempData["Success"] = $"Signed up {customerName} for Open Play ({TimeDisplay.HourRange(startHour, endHour)}, {spotCount} spot{(spotCount != 1 ? "s" : "")}) — ₱{signup.TotalPrice:N0} via {paymentMethod} logged.";
        return RedirectToAction(nameof(Index));
    }

    // ── My cash log ───────────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(int id)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var booking  = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == id && courtIds.Contains(b.CourtId));
        if (booking is null) return NotFound();

        if (booking.ReservedUntil.HasValue && DateTime.UtcNow > booking.ReservedUntil.Value)
        {
            booking.Status = BookingStatus.Cancelled;
            await _db.SaveChangesAsync();
            TempData["Error"] = $"Booking #{id} has expired (payment window elapsed) and has been cancelled.";
            return RedirectToAction(nameof(Bookings), new { status = "Pending" });
        }

        booking.Status        = BookingStatus.Confirmed;
        booking.PaymentStatus = PaymentStatus.Paid;
        booking.PaidAt        = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Booking #{id} confirmed.";
        return RedirectToAction(nameof(Bookings), new { status = "Pending" });
    }

    public async Task<IActionResult> MyCashLog(DateOnly? from, DateOnly? to)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var employerOwnerId = await GetEmployerOwnerIdAsync();
        var bookings = await _bookingService.GetCashLogAsync(courtIds, CurrentStaffId, from, to, employerOwnerId);

        ViewBag.From = from;
        ViewBag.To   = to;
        var confirmed = bookings.Where(b => b.Status != BookingStatus.Pending).ToList();
        ViewBag.GrandTotal   = confirmed.Sum(b => b.TotalPrice);
        ViewBag.CashTotal    = confirmed.Where(b => string.Equals(b.PaymentMethod, "Cash", StringComparison.OrdinalIgnoreCase)).Sum(b => b.TotalPrice);
        ViewBag.DigitalTotal = confirmed.Where(b => !string.Equals(b.PaymentMethod, "Cash", StringComparison.OrdinalIgnoreCase)).Sum(b => b.TotalPrice);
        ViewBag.PendingTotal = bookings.Where(b => b.Status == BookingStatus.Pending).Sum(b => b.TotalPrice);
        ViewBag.PendingCount = bookings.Count(b => b.Status == BookingStatus.Pending);
        return View(bookings);
    }

    /// <summary>Root folder for file uploads — mirrors <c>BookingsController.UploadsRoot</c> so
    /// staff-uploaded payment proofs land in the same persistent-volume-aware location.</summary>
    private static string UploadsRoot =>
        Environment.GetEnvironmentVariable("UPLOADS_ROOT")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
}
