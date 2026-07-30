using CourtBooking.Data;
using CourtBooking.Helpers;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly UserManager<ApplicationUser> _userManager;

    public StaffController(
        ApplicationDbContext db,
        BookingService bookingService,
        GuestCheckoutService guestCheckout,
        UserManager<ApplicationUser> userManager)
    {
        _db             = db;
        _bookingService = bookingService;
        _guestCheckout  = guestCheckout;
        _userManager    = userManager;
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

        ViewBag.Rows = rows.OrderByDescending(r => r.BookingDate).ThenByDescending(r => r.StartTime).ToList();
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
            AddOnsSummary = b.AddOns.Any() ? string.Join(", ", b.AddOns.Select(a => $"{a.Quantity}x {a.AddOnItem.Name}")) : null
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
            var blockedHours = await _bookingService.GetBlockedHoursAsync(courtId.Value, selectedDate);
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
            vm.BlockedHours    = blockedHours;
            vm.BundleOnlyHours = bundleOnlyHours;
            vm.OpenPlaySignupInfo = openPlaySignupInfo;
            vm.OpenPlayHours   = schedule
                .Where(kv => kv.Value.Type == BookingType.AdminHostedOpenPlay && !bundleOnlyHours.ContainsKey(kv.Key))
                .Select(kv => kv.Key).ToList();
            vm.HourlyRates    = schedule.ToDictionary(kv => kv.Key, kv => kv.Value.Rate);
            vm.AvailableHours = Enumerable
                .Range(court.OpeningHour, court.ClosingHour - court.OpeningHour)
                .Where(h => !bookedHours.Contains(h) && !blockedHours.Contains(h)
                         && !vm.OpenPlayHours.Contains(h) && !bundleOnlyHours.ContainsKey(h))
                .ToList();
        }

        return View(vm);
    }

    // ── Walk-in booking: confirm customer info + duration for a chosen hour ──

    public async Task<IActionResult> WalkInForm(int courtId, DateOnly date, int startHour)
    {
        var court = await (await MyCourtsAsync()).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        ViewBag.Court     = court;
        ViewBag.Date      = date;
        ViewBag.StartHour = startHour;
        ViewBag.TotalPrice = await _bookingService.GetTotalPriceAsync(
            court, date, new TimeOnly(startHour % 24, 0), new TimeOnly((startHour + 1) % 24, 0));

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
        return methods;
    }

    /// <summary>Saves an optional payment-proof screenshot (JPG/PNG/WebP) for a GCash/Maya walk-in or
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
        var fileName = $"{prefix}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsDir, fileName);
        using (var stream = System.IO.File.Create(fullPath))
            await paymentProof.CopyToAsync(stream);
        return $"/uploads/proofs/{fileName}";
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWalkIn(
        int courtId, DateOnly date, int startHour, int durationHours, string customerName, string customerPhone,
        string paymentMethod, string? paymentReference, IFormFile? paymentProof, string? notes)
    {
        var court = await (await MyCourtsAsync()).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerPhone))
        {
            TempData["Error"] = "Customer name and phone are required.";
            return RedirectToAction(nameof(WalkInForm), new { courtId, date, startHour });
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

        // Synthesize a stable, collision-free identity for the walk-in customer from their phone
        // number — GuestCheckoutService keys on email, and a real customer's email never matches
        // this pattern, so repeat walk-ins by the same phone reuse the same shadow account.
        var digitsOnly = new string(customerPhone.Where(char.IsDigit).ToArray());
        var syntheticEmail = $"walkin+{digitsOnly}@walkin.courtbook.local";
        var customer = await _guestCheckout.GetOrCreateGuestUserAsync(customerName, syntheticEmail, customerPhone);

        var totalPrice = await _bookingService.GetTotalPriceAsync(court, date, startTime, endTime);

        var employerOwnerId = await GetEmployerOwnerIdAsync();
        var (addOns, addOnsTotal) = employerOwnerId != null
            ? await _bookingService.ResolveSelectedAddOnsAsync(employerOwnerId, Request.Form)
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
            Status        = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            PaymentMethod = paymentMethod,
            PaymentReference = paymentReference,
            PaymentProofPath = proofPath,
            PaidAt        = DateTime.UtcNow,
            LoggedByStaffId = CurrentStaffId,
            CustomerNameSnapshot = customerName,
            AddOns        = addOns
        };
        await _bookingService.CreateBookingAsync(booking);

        TempData["Success"] = $"Booked {court.Name} for {customerName} ({TimeDisplay.HourRange(startHour, startHour + durationHours)}) — ₱{booking.TotalPrice:N0} via {paymentMethod} logged.";
        return RedirectToAction(nameof(Index));
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
        int courtId, DateOnly date, int startHour, int endHour, int spotCount, string customerName, string customerPhone,
        string paymentMethod, string? paymentReference, IFormFile? paymentProof, string? playerNames, string? notes)
    {
        var court = await (await MyCourtsAsync()).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerPhone))
        {
            TempData["Error"] = "Customer name and phone are required.";
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

        var digitsOnly = new string(customerPhone.Where(char.IsDigit).ToArray());
        var syntheticEmail = $"walkin+{digitsOnly}@walkin.courtbook.local";
        var customer = await _guestCheckout.GetOrCreateGuestUserAsync(customerName, syntheticEmail, customerPhone);

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

        TempData["Success"] = $"Signed up {customerName} for Open Play ({TimeDisplay.HourRange(startHour, endHour)}, {spotCount} spot{(spotCount != 1 ? "s" : "")}) — ₱{signup.TotalPrice:N0} via {paymentMethod} logged.";
        return RedirectToAction(nameof(Index));
    }

    // ── My cash log ───────────────────────────────────────────────────────────

    public async Task<IActionResult> MyCashLog(DateOnly? from, DateOnly? to)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var bookings = await _bookingService.GetCashLogAsync(courtIds, CurrentStaffId, from, to);

        ViewBag.From = from;
        ViewBag.To   = to;
        ViewBag.GrandTotal = bookings.Sum(b => b.TotalPrice);
        return View(bookings);
    }

    /// <summary>Root folder for file uploads — mirrors <c>BookingsController.UploadsRoot</c> so
    /// staff-uploaded payment proofs land in the same persistent-volume-aware location.</summary>
    private static string UploadsRoot =>
        Environment.GetEnvironmentVariable("UPLOADS_ROOT")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
}
