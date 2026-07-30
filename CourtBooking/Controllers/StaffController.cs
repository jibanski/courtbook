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
        var localNow = DateTime.UtcNow.AddHours(8);
        var today = DateOnly.FromDateTime(localNow);

        ViewBag.Courts = await (await MyCourtsAsync()).Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        ViewBag.TodaysBookings = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .Where(b => courtIds.Contains(b.CourtId) && b.BookingDate == today && b.Status != BookingStatus.Cancelled)
            .OrderBy(b => b.StartTime)
            .ToListAsync();

        return View();
    }

    // ── All bookings (read-only) — lets front-desk staff verify a booking went ──
    // through, or look up a customer's booking, without needing Admin access.

    public async Task<IActionResult> Bookings(string? status, DateOnly? date, string? search)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var query = _db.Bookings
            .Where(b => courtIds.Contains(b.CourtId))
            .Include(b => b.Court)
            .Include(b => b.User)
            .Include(b => b.AddOns).ThenInclude(a => a.AddOnItem)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BookingStatus>(status, out var s))
            query = query.Where(b => b.Status == s);
        if (date.HasValue)
            query = query.Where(b => b.BookingDate == date.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b => b.User.FullName.Contains(term)
                                   || (b.User.PhoneNumber != null && b.User.PhoneNumber.Contains(term)));
        }

        var bookings = await query
            .OrderByDescending(b => b.BookingDate).ThenByDescending(b => b.StartTime)
            .ToListAsync();

        var staffIds = bookings.Where(b => b.LoggedByStaffId != null).Select(b => b.LoggedByStaffId!).Distinct().ToList();
        ViewBag.StaffNames = await _db.Users.Where(u => staffIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        ViewBag.SelectedStatus = status;
        ViewBag.SelectedDate   = date;
        ViewBag.Search         = search;
        return View(bookings);
    }

    // ── Walk-in booking: pick a court/date, then a slot ──────────────────────

    public async Task<IActionResult> NewWalkIn(int? courtId, DateTime? date)
    {
        var myCourts = await (await MyCourtsAsync()).Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        ViewBag.Courts = myCourts;

        if (courtId is null)
        {
            ViewBag.SelectedDate = date.HasValue ? DateOnly.FromDateTime(date.Value) : DateOnly.FromDateTime(DateTime.Today);
            return View();
        }

        var court = myCourts.FirstOrDefault(c => c.Id == courtId);
        if (court is null) return NotFound();

        var selectedDate = date.HasValue ? DateOnly.FromDateTime(date.Value) : DateOnly.FromDateTime(DateTime.Today);

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
                    court, selectedDate, new TimeOnly(s.StartHour, 0), new TimeOnly(s.EndHour, 0));
            }
        }
        else
        {
            var bookedHours  = await _bookingService.GetBookedHoursAsync(courtId.Value, selectedDate);
            var blockedHours = await _bookingService.GetBlockedHoursAsync(courtId.Value, selectedDate);
            var schedule     = await _bookingService.GetHourlyScheduleAsync(court, selectedDate);

            var bundleOnlyHours = new Dictionary<int, (CourtBundle Bundle, CourtBundleRateBlock Block)>();
            for (int h = court.OpeningHour; h < court.ClosingHour; h++)
            {
                var match = await _bookingService.ResolveBundleForHourAsync(court, selectedDate, h);
                if (match is not null) bundleOnlyHours[h] = match.Value;
            }

            vm.BookedHours     = bookedHours;
            vm.BlockedHours    = blockedHours;
            vm.BundleOnlyHours = bundleOnlyHours;
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
            court, date, new TimeOnly(startHour, 0), new TimeOnly(startHour + 1, 0));

        var employerOwnerId = await GetEmployerOwnerIdAsync();
        ViewBag.AddOns = employerOwnerId != null
            ? await _bookingService.GetActiveAddOnsAsync(employerOwnerId)
            : new List<AddOnItem>();

        var settings = employerOwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == employerOwnerId)
            : null;
        var methods = new List<string> { "Cash" };
        if (!string.IsNullOrWhiteSpace(settings?.GCashNumber)) methods.Add("GCash");
        if (!string.IsNullOrWhiteSpace(settings?.MayaNumber)) methods.Add("Maya");
        ViewBag.PaymentMethods = methods;

        return View();
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

        var startTime = new TimeOnly(startHour, 0);
        var endTime   = new TimeOnly(startHour + durationHours, 0);

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
        string? proofPath = null;
        if (paymentProof is { Length: > 0 })
        {
            var ext = Path.GetExtension(paymentProof.FileName).ToLower();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".webp")
            {
                var uploadsDir = Path.Combine(UploadsRoot, "uploads", "proofs");
                Directory.CreateDirectory(uploadsDir);
                var fileName = $"walkin_{Guid.NewGuid():N}{ext}";
                var fullPath = Path.Combine(uploadsDir, fileName);
                using (var stream = System.IO.File.Create(fullPath))
                    await paymentProof.CopyToAsync(stream);
                proofPath = $"/uploads/proofs/{fileName}";
            }
            else
            {
                TempData["Error"] = "Payment proof must be JPG, PNG, or WebP — booking was not created.";
                return RedirectToAction(nameof(WalkInForm), new { courtId, date, startHour });
            }
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
            AddOns        = addOns
        };
        await _bookingService.CreateBookingAsync(booking);

        TempData["Success"] = $"Booked {court.Name} for {customerName} ({TimeDisplay.HourRange(startHour, startHour + durationHours)}) — ₱{booking.TotalPrice:N0} via {paymentMethod} logged.";
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
