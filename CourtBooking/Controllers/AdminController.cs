using CourtBooking.Data;
using CourtBooking.Filters;
using CourtBooking.Helpers;
using CourtBooking.Models;
using CourtBooking.Services;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace CourtBooking.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;
    private readonly GuestCheckoutService _guestCheckout;
    private readonly EmailService _email;
    private readonly IConfiguration _config;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AdminController> _logger;
    private readonly ImageCompressionService _imageCompression;

    public AdminController(
        ApplicationDbContext db,
        BookingService bookingService,
        GuestCheckoutService guestCheckout,
        EmailService email,
        IConfiguration config,
        UserManager<ApplicationUser> userManager,
        ILogger<AdminController> logger,
        ImageCompressionService imageCompression)
    {
        _db             = db;
        _bookingService = bookingService;
        _guestCheckout  = guestCheckout;
        _email          = email;
        _config         = config;
        _userManager    = userManager;
        _logger         = logger;
        _imageCompression = imageCompression;
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

        // Sequential awaits — EF Core DbContext is not thread-safe; Task.WhenAll on the same context causes errors
        var totalBookings   = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.Status != BookingStatus.Cancelled);
        var todayBookings   = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.BookingDate == PhtClock.Today && b.Status != BookingStatus.Cancelled)
                            + await _db.OpenPlaySignups.CountAsync(s => courtIds.Contains(s.CourtId) && s.BookingDate == PhtClock.Today && s.Status != BookingStatus.Cancelled);
        var totalRevenue    = await _db.Bookings.Where(b => courtIds.Contains(b.CourtId) && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed)).SumAsync(b => b.TotalPrice);
        var activeCourts    = await MyCourts.CountAsync(c => c.IsActive);
        var awaitingPayment    = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.Status == BookingStatus.Pending && b.PaymentProofSubmittedAt != null);
        var awaitingSignups    = await _db.OpenPlaySignups.CountAsync(s => courtIds.Contains(s.CourtId) && s.Status == BookingStatus.Pending && s.PaymentProofSubmittedAt != null);
        var awaitingAddOnRentals = await _db.AddOnRentals.CountAsync(r => r.OwnerId == CurrentUserId && r.Status == BookingStatus.Pending);
        var settings        = await GetMySettingsAsync();

        ViewBag.TotalBookings   = totalBookings;
        ViewBag.TodayBookings   = todayBookings;
        ViewBag.TotalRevenue    = totalRevenue;
        ViewBag.ActiveCourts    = activeCourts;
        ViewBag.AwaitingPayment = awaitingPayment;
        ViewBag.AwaitingSignups = awaitingSignups;
        ViewBag.AwaitingAddOnRentals = awaitingAddOnRentals;
        ViewBag.FacilitySettings = settings;

        // ── Setup Checklist (shown on dashboard until all required items are done) ──
        var hasCourt   = activeCourts > 0;
        var hasPayment = !string.IsNullOrWhiteSpace(settings?.GCashNumber)
                         || !string.IsNullOrWhiteSpace(settings?.MayaNumber)
                         || !string.IsNullOrWhiteSpace(settings?.GoTymeNumber)
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

        var recentBookingsRaw = await _db.Bookings
            .Where(b => courtIds.Contains(b.CourtId))
            .Include(b => b.Court).Include(b => b.User).Include(b => b.CourtBundle)
            .Include(b => b.AddOns).ThenInclude(a => a.AddOnItem)
            .OrderByDescending(b => b.CreatedAt)
            .Take(10)
            .ToListAsync();
        var recentSignupsRaw = await _db.OpenPlaySignups
            .Where(sg => courtIds.Contains(sg.CourtId))
            .Include(sg => sg.Court).Include(sg => sg.User)
            .OrderByDescending(sg => sg.CreatedAt)
            .Take(10)
            .ToListAsync();
        var recentRows = (await BuildAdminBookingRowsAsync(recentBookingsRaw, recentSignupsRaw))
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .ToList();

        return View(recentRows);
    }

    /// <summary>
    /// Merges regular/bundle bookings and Open Play sign-ups into one unified row list —
    /// shared by the dashboard's "Recent Bookings" widget and the "All Bookings" page so
    /// Open Play never silently disappears from either view.
    /// </summary>
    private async Task<List<AdminBookingRow>> BuildAdminBookingRowsAsync(List<Booking> bookings, List<OpenPlaySignup> signups)
    {
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
            CourtId = b.CourtId,
            CourtName = b.Court.Name,
            BundleName = b.CourtBundle?.Name,
            BookingDate = b.BookingDate,
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            CreatedAt = b.CreatedAt,
            TotalPrice = b.TotalPrice,
            Status = b.Status,
            PaymentStatus = b.PaymentStatus,
            HasPaymentProof = b.HasPaymentProof,
            PaymentMethod = b.PaymentMethod,
            PaymentReference = b.PaymentReference,
            PaymentProofPath = b.PaymentProofPath,
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
            CourtId = sg.CourtId,
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
            PaymentReference = sg.PaymentReference,
            PaymentProofPath = sg.PaymentProofPath,
            BookedByStaffName = sg.LoggedByStaffId != null && staffNames.TryGetValue(sg.LoggedByStaffId, out var sgn) ? sgn : null
        }));

        return rows;
    }

    // ── Real-time analytics ───────────────────────────────────────────────────

    /// <summary>Analytics dashboard. Charts are populated by AnalyticsData() via polling.</summary>
    public async Task<IActionResult> Analytics()
    {
        ViewBag.FacilitySettings = await GetMySettingsAsync();
        ViewBag.Courts    = await MyCourts.OrderBy(c => c.Name).ToListAsync();
        var today = PhtClock.Today;
        ViewBag.DefaultFrom = today.AddDays(-29).ToString("yyyy-MM-dd");
        ViewBag.DefaultTo   = today.ToString("yyyy-MM-dd");
        return View();
    }

    /// <summary>
    /// JSON endpoint backing /admin/analytics. Auto-refreshed every 10s by the page.
    /// Returns counters, revenue series/payment-method breakdown/conversion for the selected
    /// court + date range (defaults to all courts, last 30 days, when omitted) — the range/court
    /// filter lets the owner narrow in on a specific period or court to trace a discrepancy.
    /// </summary>
    /// <summary>One row of the shared shape Bookings/OpenPlaySignups/AddOnRentals are projected
    /// to for analytics, so the three sources can be combined and aggregated once in memory
    /// instead of via a repeatedly-re-issued SQL UNION (see <see cref="AnalyticsData"/>).</summary>
    private sealed record AnalyticsRow(
        DateOnly BookingDate, decimal TotalPrice, BookingStatus Status, PaymentStatus PaymentStatus,
        DateTime? PaidAt, bool HasProof, string? PaymentReference, string? PaymentMethod,
        int? CourtId, string? LoggedByStaffId)
    {
        /// <summary>The date every range filter/breakdown below buckets a row into — when it was
        /// paid (PHT calendar day), falling back to the court's BookingDate for unpaid rows (which
        /// have no PaidAt yet). Using BookingDate for paid rows too would put a sale in a different
        /// day's bucket than the one its payment method/status actually landed in, causing widgets
        /// like Payment Mix (paid-date based) and Status Breakdown (was BookingDate-only) to
        /// disagree on the same range.</summary>
        public DateOnly EffectiveDate => PaidAt.HasValue ? DateOnly.FromDateTime(PaidAt.Value.AddHours(8)) : BookingDate;
    }

    /// <summary>One add-on line (from a Booking or a standalone rental) for the Top Add-On Items
    /// breakdown — kept separate from <see cref="AnalyticsRow"/> since it aggregates per item, not
    /// per sale.</summary>
    private sealed record AddOnItemRow(int AddOnItemId, string Name, int Quantity, decimal Revenue, DateOnly BookingDate, DateTime? PaidAt, BookingStatus Status)
    {
        public DateOnly EffectiveDate => PaidAt.HasValue ? DateOnly.FromDateTime(PaidAt.Value.AddHours(8)) : BookingDate;
    }

    [HttpGet]
    public async Task<IActionResult> AnalyticsData(int? courtId, DateOnly? from, DateOnly? to)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var courtIds = courtId.HasValue && myCourtIds.Contains(courtId.Value)
            ? new List<int> { courtId.Value }
            : myCourtIds;

        var today = PhtClock.Today;
        var rangeTo   = to ?? today;
        var rangeFrom = from ?? rangeTo.AddDays(-29);
        if (rangeFrom > rangeTo) (rangeFrom, rangeTo) = (rangeTo, rangeFrom);
        var todayDt     = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-8);    // PHT midnight today, as a UTC instant
        var tomorrowDt  = todayDt.AddDays(1);
        // PHT calendar-day bounds of the selected range, as UTC instants — used to bound the
        // PaidAt side of the EffectiveDate condition below.
        var rangeFromUtc        = rangeFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-8);
        var rangeToExclusiveUtc = rangeTo.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-8);

        // All-time / today counters are aggregated in SQL (COUNT/SUM return one row each) instead
        // of pulling every booking/signup/rental ever made into memory — this endpoint is polled
        // every 10s by the dashboard, so a full-history fetch here scaled with total data transferred
        // (Supabase egress) on every poll, forever, as a facility's history grew.
        var totalBookings = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.Status != BookingStatus.Cancelled)
                          + await _db.OpenPlaySignups.CountAsync(s => courtIds.Contains(s.CourtId) && s.Status != BookingStatus.Cancelled)
                          + (courtId.HasValue ? 0 : await _db.AddOnRentals.CountAsync(r => r.OwnerId == CurrentUserId && r.Status != BookingStatus.Cancelled));
        var todayBookings = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.BookingDate == today && b.Status != BookingStatus.Cancelled)
                          + await _db.OpenPlaySignups.CountAsync(s => courtIds.Contains(s.CourtId) && s.BookingDate == today && s.Status != BookingStatus.Cancelled)
                          + (courtId.HasValue ? 0 : await _db.AddOnRentals.CountAsync(r => r.OwnerId == CurrentUserId && r.CreatedAt >= todayDt && r.CreatedAt < tomorrowDt));
        var todayRevenue = (await _db.Bookings.Where(b => courtIds.Contains(b.CourtId) && b.PaidAt != null && b.PaidAt >= todayDt).SumAsync(b => (decimal?)b.TotalPrice) ?? 0m)
                         + (await _db.OpenPlaySignups.Where(s => courtIds.Contains(s.CourtId) && s.PaidAt != null && s.PaidAt >= todayDt).SumAsync(s => (decimal?)s.TotalPrice) ?? 0m)
                         + (courtId.HasValue ? 0m : (await _db.AddOnRentals.Where(r => r.OwnerId == CurrentUserId && r.PaidAt != null && r.PaidAt >= todayDt).SumAsync(r => (decimal?)r.TotalPrice) ?? 0m));
        var totalRevenue = (await _db.Bookings.Where(b => courtIds.Contains(b.CourtId) && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed)).SumAsync(b => (decimal?)b.TotalPrice) ?? 0m)
                         + (await _db.OpenPlaySignups.Where(s => courtIds.Contains(s.CourtId) && (s.Status == BookingStatus.Confirmed || s.Status == BookingStatus.Completed)).SumAsync(s => (decimal?)s.TotalPrice) ?? 0m)
                         + (courtId.HasValue ? 0m : (await _db.AddOnRentals.Where(r => r.OwnerId == CurrentUserId && (r.Status == BookingStatus.Confirmed || r.Status == BookingStatus.Completed)).SumAsync(r => (decimal?)r.TotalPrice) ?? 0m));
        var awaitingPayment = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.Status == BookingStatus.Pending && b.PaymentProofSubmittedAt != null)
                             + await _db.OpenPlaySignups.CountAsync(s => courtIds.Contains(s.CourtId) && s.Status == BookingStatus.Pending && s.PaymentProofSubmittedAt != null)
                             + (courtId.HasValue ? 0 : await _db.AddOnRentals.CountAsync(r => r.OwnerId == CurrentUserId && r.Status == BookingStatus.Pending && r.PaymentProofPath != null));
        var pendingNoProof = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.Status == BookingStatus.Pending && b.PaymentReference == null)
                            + await _db.OpenPlaySignups.CountAsync(s => courtIds.Contains(s.CourtId) && s.Status == BookingStatus.Pending && s.PaymentReference == null)
                            + (courtId.HasValue ? 0 : await _db.AddOnRentals.CountAsync(r => r.OwnerId == CurrentUserId && r.Status == BookingStatus.Pending && r.PaymentReference == null));

        // Add-ons attached to a Booking, plus standalone add-on rentals (same "all courts only"
        // scoping as above) — split out of totalRevenue so the owner can see rental vs. add-on
        // sales separately. Both are all-time SQL aggregates, same reasoning as above.
        var bookingAddOnsRevenue = await _db.BookingAddOns
            .Where(a => courtIds.Contains(a.Booking.CourtId)
                     && (a.Booking.Status == BookingStatus.Confirmed || a.Booking.Status == BookingStatus.Completed))
            .SumAsync(a => (decimal?)(a.Quantity * a.UnitPrice)) ?? 0m;
        var standaloneAddOnRentalsRevenue = courtId.HasValue ? 0m : (await _db.AddOnRentals
            .Where(r => r.OwnerId == CurrentUserId && (r.Status == BookingStatus.Confirmed || r.Status == BookingStatus.Completed))
            .SumAsync(r => (decimal?)r.TotalPrice) ?? 0m);
        var addOnsRevenue = bookingAddOnsRevenue + standaloneAddOnRentalsRevenue;
        var courtRentalRevenue = totalRevenue - addOnsRevenue;

        // Everything below only needs rows whose EffectiveDate falls inside the selected range
        // (default: last 30 days), so each source is filtered at the DB level to that window
        // instead of loading a facility's entire booking/signup/rental history into memory —
        // this is what actually keeps this endpoint's egress bounded regardless of how much
        // history has built up, since it's polled every 10s while the dashboard is open.
        var bookingRows = await _db.Bookings
            .Where(b => courtIds.Contains(b.CourtId)
                     && ((b.PaidAt != null && b.PaidAt >= rangeFromUtc && b.PaidAt < rangeToExclusiveUtc)
                         || (b.PaidAt == null && b.BookingDate >= rangeFrom && b.BookingDate <= rangeTo)))
            .Select(b => new AnalyticsRow(b.BookingDate, b.TotalPrice, b.Status, b.PaymentStatus,
                b.PaidAt, b.PaymentProofSubmittedAt != null, b.PaymentReference, b.PaymentMethod,
                b.CourtId, b.LoggedByStaffId))
            .ToListAsync();
        var signupRows = await _db.OpenPlaySignups
            .Where(s => courtIds.Contains(s.CourtId)
                     && ((s.PaidAt != null && s.PaidAt >= rangeFromUtc && s.PaidAt < rangeToExclusiveUtc)
                         || (s.PaidAt == null && s.BookingDate >= rangeFrom && s.BookingDate <= rangeTo)))
            .Select(s => new AnalyticsRow(s.BookingDate, s.TotalPrice, s.Status, s.PaymentStatus,
                s.PaidAt, s.PaymentProofSubmittedAt != null, s.PaymentReference, s.PaymentMethod,
                s.CourtId, s.LoggedByStaffId))
            .ToListAsync();
        // Standalone add-on rentals (e.g. paddle-only counter sales) have no court/slot, so they
        // can't be scoped to a specific court — only fold them in for the "all courts" view.
        // DateOnly.FromDateTime() is computed after the round trip (not inside the Select sent
        // to the DB) — Npgsql failed to translate it into valid SQL ("operator does not exist"),
        // even though the same expression translates fine against SQLite locally.
        var addOnRentalRows = courtId.HasValue
            ? new List<AnalyticsRow>()
            : (await _db.AddOnRentals
                .Where(r => r.OwnerId == CurrentUserId
                         && ((r.PaidAt != null && r.PaidAt >= rangeFromUtc && r.PaidAt < rangeToExclusiveUtc)
                             || (r.PaidAt == null && r.CreatedAt >= rangeFromUtc && r.CreatedAt < rangeToExclusiveUtc)))
                .Select(r => new { r.CreatedAt, r.TotalPrice, r.Status, r.PaymentStatus,
                    r.PaidAt, HasProof = r.PaymentProofPath != null, r.PaymentReference, r.PaymentMethod, r.LoggedByStaffId })
                .ToListAsync())
                .Select(r => new AnalyticsRow(DateOnly.FromDateTime(r.CreatedAt.AddHours(8)), r.TotalPrice,
                    r.Status, r.PaymentStatus, r.PaidAt, r.HasProof, r.PaymentReference, r.PaymentMethod,
                    null, r.LoggedByStaffId))
                .ToList();

        var combined = bookingRows.Concat(signupRows).Concat(addOnRentalRows).ToList();

        var revenueRows = combined
            .Where(x => x.EffectiveDate >= rangeFrom && x.EffectiveDate <= rangeTo
                        && (x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.Completed))
            .GroupBy(x => x.EffectiveDate)
            .Select(g => new { Day = g.Key, Revenue = g.Sum(x => x.TotalPrice), Count = g.Count() })
            .ToList();

        var revenueByDay = new List<object>();
        for (var d = rangeFrom; d <= rangeTo; d = d.AddDays(1))
        {
            var row = revenueRows.FirstOrDefault(r => r.Day == d);
            revenueByDay.Add(new
            {
                date    = d.ToString("yyyy-MM-dd"),
                revenue = row?.Revenue ?? 0m,
                count   = row?.Count   ?? 0
            });
        }

        // Payment mix — bucketed by EffectiveDate (paid date, falling back to BookingDate),
        // matching every other range breakdown below so they never disagree on the same range.
        var methodRows = combined
            .Where(x => x.PaymentStatus == PaymentStatus.Paid && x.EffectiveDate >= rangeFrom && x.EffectiveDate <= rangeTo)
            .GroupBy(x => x.PaymentMethod ?? "Unknown")
            .Select(g => new { Method = g.Key, Count = g.Count(), Revenue = g.Sum(x => x.TotalPrice) })
            .ToList();

        var bookingsInRange = combined
            .Count(x => x.EffectiveDate >= rangeFrom && x.EffectiveDate <= rangeTo && x.Status != BookingStatus.Cancelled);
        var paidInRange = combined
            .Count(x => x.EffectiveDate >= rangeFrom && x.EffectiveDate <= rangeTo && x.PaymentStatus == PaymentStatus.Paid);
        var conversion = bookingsInRange > 0 ? Math.Round(paidInRange * 100.0 / bookingsInRange, 1) : 0.0;
        // Paid revenue for the selected range — distinct from totalRevenue (all-time) and
        // todayRevenue (today only); this is what the "Selected Range" cards surface.
        var rangeRevenue = revenueRows.Sum(r => r.Revenue);

        // Status mix within the selected range, so filtering also answers "how many of these
        // were cancelled / still pending" instead of just the pass/fail conversion percentage.
        var statusBreakdown = combined
            .Where(x => x.EffectiveDate >= rangeFrom && x.EffectiveDate <= rangeTo)
            .GroupBy(x => x.Status)
            .Select(g => new { status = g.Key.ToString(), count = g.Count(), revenue = g.Sum(x => x.TotalPrice) })
            .OrderByDescending(g => g.count)
            .ToList();

        // Per-court comparison — lets a multi-court owner see which court is actually earning
        // without having to flip through the Court filter one at a time. Standalone add-on
        // rentals have no CourtId, so they land in their own "Add-ons / Other" bucket.
        var courtNames = await _db.Courts.Where(c => courtIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name);
        var courtBreakdown = combined
            .Where(x => x.EffectiveDate >= rangeFrom && x.EffectiveDate <= rangeTo
                        && (x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.Completed))
            .GroupBy(x => x.CourtId)
            .Select(g => new
            {
                court   = g.Key.HasValue ? (courtNames.TryGetValue(g.Key.Value, out var n) ? n : $"Court #{g.Key}") : "Add-ons / Other",
                count   = g.Count(),
                revenue = g.Sum(x => x.TotalPrice)
            })
            .OrderByDescending(g => g.revenue)
            .ToList();

        // Per-staff comparison — only counts staff-logged sales (walk-ins/counter sales), same
        // scope as the Sales Log, so it complements rather than duplicates it.
        var staffIds = combined.Where(x => x.LoggedByStaffId != null)
            .Select(x => x.LoggedByStaffId!).Distinct().ToList();
        var staffNames = await _db.Users.Where(u => staffIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);
        var staffBreakdown = combined
            .Where(x => x.LoggedByStaffId != null && x.EffectiveDate >= rangeFrom && x.EffectiveDate <= rangeTo
                        && (x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.Completed))
            .GroupBy(x => x.LoggedByStaffId)
            .Select(g => new
            {
                staff   = staffNames.TryGetValue(g.Key!, out var n) ? n : "Unknown",
                count   = g.Count(),
                revenue = g.Sum(x => x.TotalPrice)
            })
            .OrderByDescending(g => g.revenue)
            .ToList();

        // Top add-on items — separate from courtRentalRevenue/addOnsRevenue above, this breaks
        // that lump sum down by item so the owner can see what's actually selling. Filtered to the
        // same EffectiveDate-in-range window as everything else above, for the same egress reason.
        var bookingAddOnItemRows = (await _db.BookingAddOns
            .Where(a => courtIds.Contains(a.Booking.CourtId)
                     && ((a.Booking.PaidAt != null && a.Booking.PaidAt >= rangeFromUtc && a.Booking.PaidAt < rangeToExclusiveUtc)
                         || (a.Booking.PaidAt == null && a.Booking.BookingDate >= rangeFrom && a.Booking.BookingDate <= rangeTo)))
            .Select(a => new { a.AddOnItemId, Name = a.AddOnItem.Name, a.Quantity, a.UnitPrice,
                a.Booking.BookingDate, a.Booking.PaidAt, a.Booking.Status })
            .ToListAsync())
            .Select(a => new AddOnItemRow(a.AddOnItemId, a.Name, a.Quantity, a.Quantity * a.UnitPrice,
                a.BookingDate, a.PaidAt, a.Status))
            .ToList();
        var standaloneAddOnItemRows = courtId.HasValue
            ? new List<AddOnItemRow>()
            : (await _db.AddOnRentalItems
                .Where(i => i.AddOnRental.OwnerId == CurrentUserId
                         && ((i.AddOnRental.PaidAt != null && i.AddOnRental.PaidAt >= rangeFromUtc && i.AddOnRental.PaidAt < rangeToExclusiveUtc)
                             || (i.AddOnRental.PaidAt == null && i.AddOnRental.CreatedAt >= rangeFromUtc && i.AddOnRental.CreatedAt < rangeToExclusiveUtc)))
                .Select(i => new { i.AddOnItemId, Name = i.AddOnItem.Name, i.Quantity, i.UnitPrice,
                    i.AddOnRental.CreatedAt, i.AddOnRental.PaidAt, i.AddOnRental.Status })
                .ToListAsync())
                .Select(i => new AddOnItemRow(i.AddOnItemId, i.Name, i.Quantity, i.Quantity * i.UnitPrice,
                    DateOnly.FromDateTime(i.CreatedAt.AddHours(8)), i.PaidAt, i.Status))
                .ToList();
        var topAddOnItems = bookingAddOnItemRows.Concat(standaloneAddOnItemRows)
            .Where(x => x.EffectiveDate >= rangeFrom && x.EffectiveDate <= rangeTo
                        && (x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.Completed))
            .GroupBy(x => new { x.AddOnItemId, x.Name })
            .Select(g => new { name = g.Key.Name, quantity = g.Sum(x => x.Quantity), revenue = g.Sum(x => x.Revenue) })
            .OrderByDescending(g => g.revenue)
            .Take(8)
            .ToList();

        return Json(new
        {
            generatedAt = DateTime.UtcNow,
            rangeFrom = rangeFrom.ToString("yyyy-MM-dd"),
            rangeTo   = rangeTo.ToString("yyyy-MM-dd"),
            counters = new
            {
                totalBookings,
                todayBookings,
                todayRevenue,
                totalRevenue,
                awaitingPayment,
                pendingNoProof,
                conversionPct    = conversion,
                paidInRange,
                bookingsInRange,
                rangeRevenue,
                courtRentalRevenue,
                addOnsRevenue
            },
            revenueByDay,
            methodBreakdown = methodRows.Select(r => new
            {
                method  = r.Method,
                count   = r.Count,
                revenue = r.Revenue
            }),
            statusBreakdown,
            courtBreakdown,
            staffBreakdown,
            topAddOnItems
        });
    }

    public async Task<IActionResult> Bookings(string? status, DateOnly? dateFrom, DateOnly? dateTo, bool? awaitingConfirmation, string? search, DateOnly? weekStart, string? view)
    {
        // List and Calendar are two full page navigations (separate GET requests, not client-side
        // tabs), so only the block the visitor actually asked for needs to run each request —
        // this used to always fetch both regardless of which one was being viewed.
        bool calendarView = string.Equals(view, "calendar", StringComparison.OrdinalIgnoreCase);

        // The list view previously had no default date bound, so with no filter set it fetched
        // the entire booking/signup history (with several joined tables) on every page view —
        // the biggest remaining Supabase egress source as a facility's history grows. Default to
        // the last 30 days when neither end is specified; owners can still widen/clear it manually.
        if (awaitingConfirmation != true && !calendarView && !dateFrom.HasValue && !dateTo.HasValue)
        {
            dateTo   = PhtClock.Today;
            dateFrom = dateTo.Value.AddDays(-29);
        }

        var courtIds = await GetMyCourtIdsAsync();
        List<Booking> bookings = new();
        List<OpenPlaySignup> awaitingSignups = new();

        if (awaitingConfirmation == true || !calendarView)
        {
            var query = _db.Bookings
                .Where(b => courtIds.Contains(b.CourtId))
                .Include(b => b.Court).Include(b => b.User).Include(b => b.CourtBundle)
                .Include(b => b.AddOns).ThenInclude(a => a.AddOnItem)
                .AsQueryable();

            if (awaitingConfirmation == true)
                query = query.Where(b => b.Status == BookingStatus.Pending && b.PaymentProofSubmittedAt != null);
            else if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BookingStatus>(status, out var s))
                query = query.Where(b => b.Status == s);

            if (dateFrom.HasValue) query = query.Where(b => b.BookingDate >= dateFrom.Value);
            if (dateTo.HasValue)   query = query.Where(b => b.BookingDate <= dateTo.Value);

            bookings = await query.OrderByDescending(b => b.PaymentProofSubmittedAt ?? b.CreatedAt).ToListAsync();
        }

        if (awaitingConfirmation == true)
        {
            awaitingSignups = await _db.OpenPlaySignups
                .Where(sg => courtIds.Contains(sg.CourtId)
                          && sg.Status == BookingStatus.Pending
                          && sg.PaymentStatus == PaymentStatus.Unpaid
                          && sg.PaymentProofSubmittedAt != null)
                .Include(sg => sg.Court)
                .Include(sg => sg.User)
                .OrderByDescending(sg => sg.PaymentProofSubmittedAt ?? sg.CreatedAt)
                .ToListAsync();
        }

        // The "All Bookings" table (not the awaiting-confirmation card list, which has its
        // own dedicated flow on the Open Play Sign-ups page) also lists Open Play sign-ups
        // so the owner has one consolidated view of everything booked on their courts.
        if (awaitingConfirmation != true && !calendarView)
        {
            var signupQuery = _db.OpenPlaySignups
                .Where(sg => courtIds.Contains(sg.CourtId))
                .Include(sg => sg.Court).Include(sg => sg.User).AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BookingStatus>(status, out var signupStatus))
                signupQuery = signupQuery.Where(sg => sg.Status == signupStatus);
            if (dateFrom.HasValue) signupQuery = signupQuery.Where(sg => sg.BookingDate >= dateFrom.Value);
            if (dateTo.HasValue)   signupQuery = signupQuery.Where(sg => sg.BookingDate <= dateTo.Value);

            var signups = await signupQuery.ToListAsync();

            var rows = await BuildAdminBookingRowsAsync(bookings, signups);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                rows = rows.Where(r => r.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                     || (r.CustomerPhone != null && r.CustomerPhone.Contains(term, StringComparison.OrdinalIgnoreCase)))
                           .ToList();
            }

            ViewBag.Rows = rows
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
        }

        if (awaitingConfirmation != true && calendarView)
        {
            // Calendar view: a full Sun–Sat week of bookings/sign-ups across all the owner's
            // courts, independent of the list filters above, so switching tabs always shows a
            // whole week to browse rather than whatever the list happens to be filtered to.
            var calAnchor = weekStart ?? PhtClock.Today;
            // Clamp so the week-start/end arithmetic here and the +/-7 day nav links in the
            // view never overflow DateOnly's range (e.g. hand-edited query string or repeated
            // prev/next clicks near the year 1/9999 boundary).
            if (calAnchor < DateOnly.MinValue.AddDays(14)) calAnchor = DateOnly.MinValue.AddDays(14);
            if (calAnchor > DateOnly.MaxValue.AddDays(-14)) calAnchor = DateOnly.MaxValue.AddDays(-14);
            var calWeekStart = calAnchor.AddDays(-(int)calAnchor.DayOfWeek);
            var calWeekEnd = calWeekStart.AddDays(6);

            var calBookings = await _db.Bookings
                .Where(b => courtIds.Contains(b.CourtId) && b.BookingDate >= calWeekStart && b.BookingDate <= calWeekEnd)
                .Include(b => b.Court).Include(b => b.User).Include(b => b.CourtBundle)
                .Include(b => b.AddOns).ThenInclude(a => a.AddOnItem)
                .ToListAsync();
            var calSignups = await _db.OpenPlaySignups
                .Where(sg => courtIds.Contains(sg.CourtId) && sg.BookingDate >= calWeekStart && sg.BookingDate <= calWeekEnd)
                .Include(sg => sg.Court).Include(sg => sg.User)
                .ToListAsync();

            ViewBag.CalendarWeekStart = calWeekStart;
            ViewBag.CalendarRows = (await BuildAdminBookingRowsAsync(calBookings, calSignups))
                .OrderBy(r => r.BookingDate).ThenBy(r => r.StartTime).ToList();
        }

        ViewBag.SelectedStatus       = status;
        ViewBag.SelectedDateFrom     = dateFrom;
        ViewBag.SelectedDateTo       = dateTo;
        ViewBag.Search               = search;
        ViewBag.AwaitingConfirmation = awaitingConfirmation;
        var pendingBookingCount = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId)
                                                                  && b.Status == BookingStatus.Pending
                                                                  && b.PaymentStatus == PaymentStatus.Unpaid
                                                                  && b.PaymentProofSubmittedAt != null);
        var pendingSignupCount = await _db.OpenPlaySignups.CountAsync(sg => courtIds.Contains(sg.CourtId)
                                                                    && sg.Status == BookingStatus.Pending
                                                                    && sg.PaymentStatus == PaymentStatus.Unpaid
                                                                    && sg.PaymentProofSubmittedAt != null);
        ViewBag.PendingPaymentCount  = pendingBookingCount + pendingSignupCount;
        ViewBag.AwaitingSignups      = awaitingSignups;
        ViewBag.PendingSignupCount   = pendingSignupCount;
        return View(bookings);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(int id)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var booking  = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == id && courtIds.Contains(b.CourtId));
        if (booking is null) return NotFound();

        // Check if the 15-minute reservation window has expired
        if (booking.ReservedUntil.HasValue && DateTime.UtcNow > booking.ReservedUntil.Value)
        {
            booking.Status = BookingStatus.Cancelled;
            await _db.SaveChangesAsync();
            TempData["Error"] = $"Booking #{id} has expired (payment window elapsed) and has been automatically cancelled. The slot is now available for other customers.";
            return RedirectToAction(nameof(Bookings), new { awaitingConfirmation = true });
        }

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

        // Now that the owner has confirmed the payment, email the customer their
        // "Booking Confirmed" receipt. Fire-and-forget; never throws.
        if (booking.Court is not null && !string.IsNullOrWhiteSpace(booking.User?.Email))
        {
            var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            await _email.SendBookingConfirmedToCustomerAsync(
                booking.User.Email!,
                booking.User.FirstName,
                booking.Id,
                booking.Court.Name,
                booking.BookingDate,
                booking.StartTime,
                booking.EndTime,
                booking.TotalPrice,
                booking.PaymentMethod,
                booking.PaymentReference,
                baseUrl,
                booking.User.IsGuest);
        }

        TempData["Success"] = $"Booking #{id} confirmed — the customer has been emailed a confirmation.";
        return RedirectToAction(nameof(Bookings), new { awaitingConfirmation = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectPayment(int id)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var booking  = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == id && courtIds.Contains(b.CourtId));
        if (booking is null) return NotFound();

        // Capture details for the customer email before we clear the proof fields.
        var customerEmail = booking.User?.Email;
        var customerName  = booking.User?.FirstName;
        var courtName     = booking.Court?.Name ?? "your court";
        var bookingDate   = booking.BookingDate;
        var startTime     = booking.StartTime;
        var endTime       = booking.EndTime;

        booking.Status           = BookingStatus.Cancelled;
        booking.PaymentReference = null;
        booking.PaymentProofPath = null;
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(customerEmail))
            await SendPaymentRejectedEmailAsync(customerEmail!, customerName, booking.Id,
                courtName, bookingDate, startTime, endTime);

        TempData["Error"] = $"Booking #{id} rejected and cancelled — the customer has been notified.";
        return RedirectToAction(nameof(Bookings), new { awaitingConfirmation = true });
    }

    /// <summary>
    /// Emails the customer when the owner rejects their submitted payment and cancels
    /// the booking. Safe to fire-and-forget; never throws.
    /// </summary>
    private async Task SendPaymentRejectedEmailAsync(
        string toEmail, string? firstName, int bookingId, string courtName,
        DateOnly bookingDate, TimeOnly startTime, TimeOnly endTime)
    {
        try
        {
            var greeting  = string.IsNullOrWhiteSpace(firstName) ? "Hi there" : $"Hi {firstName}";
            var dateLabel = bookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel = $"{startTime:hh\\:mm tt} – {endTime:hh\\:mm tt}";
            var baseUrl   = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var contact   = _config["Subscription:ContactEmail"] ?? "courtbooksolutions@gmail.com";
            var browseUrl = $"{baseUrl}/Courts";

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#dc3545;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.9;letter-spacing:.5px;text-transform:uppercase;'>Booking Update</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>Payment Not Confirmed</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>{greeting}, unfortunately the facility could not confirm your payment for the booking below, so it has been <strong>cancelled</strong>.</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Court</td>     <td style='font-weight:600;padding:5px 0;'>{courtName}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Date</td>      <td style='font-weight:600;padding:5px 0;'>{dateLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Time</td>      <td style='padding:5px 0;'>{timeLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Booking #</td> <td style='padding:5px 0;'>#{bookingId}</td></tr>
      </table>
      <p style='margin:16px 0 0;font-size:14px;'>If you believe this is a mistake, please contact the facility or email us at <a href='mailto:{contact}' style='color:#0d6efd;'>{contact}</a>. You're welcome to book another slot below.</p>
      <p style='margin:20px 0 0;text-align:center;'>
        <a href='{browseUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Browse Courts</a>
      </p>
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      Automated notification · Booking #{bookingId}
    </div>
  </div>
</body></html>";

            var plain = $"Payment Not Confirmed — Booking #{bookingId}\n\n{greeting}, the facility could not confirm your payment for {courtName} on {dateLabel} ({timeLabel}), so the booking has been cancelled.\n\nIf you think this is a mistake, contact the facility or email {contact}.\n\nBrowse courts: {browseUrl}";

            await _email.SendAsync(toEmail, $"Booking #{bookingId} — Payment Not Confirmed", html, plain);
        }
        catch
        {
            // Never let a failed notification block the rejection itself.
        }
    }

    public async Task<IActionResult> Courts()
    {
        var courts = await MyCourts.ToListAsync();
        var courtIds = courts.Select(c => c.Id).ToList();
        ViewBag.AwaitingSignups = await _db.OpenPlaySignups
            .CountAsync(s => courtIds.Contains(s.CourtId) && s.Status == BookingStatus.Pending && s.PaymentProofSubmittedAt != null);
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

        var selectedDate = date ?? PhtClock.Today;
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

        // Recurring weekly default: which hours this date defaults to Admin-Hosted Open Play,
        // and which are sellable only as part of a flat-price multi-court bundle.
        var schedule = await _bookingService.GetHourlyScheduleAsync(court, selectedDate);
        var bundleOnlyHours = new Dictionary<int, string>();
        var openPlaySignupInfo = new Dictionary<int, (CourtScheduleBlock Block, int MaxPlayers, int Taken)>();
        for (int h = court.OpeningHour; h < court.ClosingHour; h++)
        {
            var match = await _bookingService.ResolveBundleForHourAsync(court, selectedDate, h);
            if (match is not null) { bundleOnlyHours[h] = match.Value.Bundle.Name; continue; }

            if (schedule.TryGetValue(h, out var s) && s.Type == BookingType.AdminHostedOpenPlay)
            {
                var block = await _bookingService.ResolveScheduleBlockForHourAsync(court, selectedDate, h);
                    if (block is { AllowPublicSignup: true, MaxPlayers: { } max })
                {
                    var remaining = await _bookingService.GetOpenPlaySpotsRemainingAsync(block, id, selectedDate);
                        openPlaySignupInfo[h] = (block, max, max - remaining);
                }
            }
        }
        var openPlayHours = schedule
            .Where(kv => kv.Value.Type == BookingType.AdminHostedOpenPlay && !bundleOnlyHours.ContainsKey(kv.Key))
            .Select(kv => kv.Key)
            .ToHashSet();

        ViewBag.Court             = court;
        ViewBag.Date              = selectedDate;
        ViewBag.BundleOnlyHours   = bundleOnlyHours;
        ViewBag.OpenPlaySignupInfo = openPlaySignupInfo;
        ViewBag.BookedHours       = bookedHours.ToHashSet();
        ViewBag.BlockedHours      = blockedHours;
        ViewBag.ActiveRangeBlocks = activeRangeBlocks;
        ViewBag.OpenPlayHours     = openPlayHours;

        // Tier-aware total price per slot for this date (falls back to court.PricePerHour * duration
        // when no CourtRateTier covers the slot's hours). Keyed by slot Id.
        var slotPrices = new Dictionary<int, decimal>();
        foreach (var s in slots)
        {
            slotPrices[s.Id] = await _bookingService.GetTotalPriceAsync(
                court, selectedDate, new TimeOnly(s.StartHour % 24, 0), new TimeOnly(s.EndHour % 24, 0));
        }
        ViewBag.SlotPrices = slotPrices;

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

        bool rateChanged = existing.PricePerHour != court.PricePerHour;

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

        if (rateChanged)
        {
            var resynced = await _bookingService.ResyncUnpaidPricesAsync(existing.Id);
            TempData["Success"] = resynced > 0
                ? $"Court updated successfully. {resynced} unpaid booking(s) updated to the new rate."
                : "Court updated successfully.";
        }
        else
        {
            TempData["Success"] = "Court updated successfully.";
        }
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
            TempData["Error"] = $"Slot {TimeDisplay.HourRange(startHour, endHour)} already exists for this date.";
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
        TempData["Success"] = $"Slot {TimeDisplay.HourRange(startHour, endHour)} added for {slotDate:MMM d, yyyy}.";
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

    // ── Recurring Weekly Schedule & Rate Tiers ───────────────────────────────────

    public async Task<IActionResult> Schedule(int id)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == id);
        if (court is null) return NotFound();

        ViewBag.Court          = court;
        ViewBag.RateTiers      = (await _bookingService.GetRateTiersAsync(id))
            .OrderBy(t => t.StartHour).ThenBy(t => t.EndHour).ThenBy(t => t.DaysOfWeek).ToList();
        ViewBag.ScheduleBlocks = (await _bookingService.GetScheduleBlocksAsync(id))
            .OrderBy(b => b.StartHour).ThenBy(b => b.EndHour).ThenBy(b => b.DaysOfWeek).ToList();
        return View();
    }

    private static string NormalizeDays(string[]? days) =>
        string.Join(",", (days ?? Array.Empty<string>())
            .Select(d => d.Trim())
            .Where(d => d.Length > 0)
            .Distinct());

    private static int DaysSortKey(string daysCsv)
    {
        var order = daysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(DaySortIndex)
            .DefaultIfEmpty(7);
        return order.Min();
    }

    private static string NormalizeDaysForSort(string daysCsv) =>
        string.Join(",", daysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(DaySortIndex)
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase));

    private static int DaySortIndex(string day) => day.ToUpperInvariant() switch
    {
        "MON" => 0,
        "TUE" => 1,
        "WED" => 2,
        "THU" => 3,
        "FRI" => 4,
        "SAT" => 5,
        "SUN" => 6,
        _ => 7
    };

    private static bool DaysOverlap(string daysA, bool includeHolidaysA, string daysB, bool includeHolidaysB)
    {
        if (includeHolidaysA && includeHolidaysB) return true;
        var setA = daysA.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var setB = daysB.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return setA.Intersect(setB, StringComparer.OrdinalIgnoreCase).Any();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRateTier(int courtId, string[] days, bool includeHolidays, int startHour, int endHour, decimal pricePerHour)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        var daysCsv = NormalizeDays(days);
        if ((daysCsv.Length == 0 && !includeHolidays) || endHour <= startHour || startHour < 0 || endHour > 24)
        {
            TempData["Error"] = "Pick at least one day (or include holidays) and a valid hour range.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        var existing = await _bookingService.GetRateTiersAsync(courtId);
        bool overlaps = existing.Any(t =>
            startHour < t.EndHour && endHour > t.StartHour &&
            DaysOverlap(t.DaysOfWeek, t.IncludeHolidays, daysCsv, includeHolidays));
        if (overlaps)
        {
            TempData["Error"] = "This rate tier overlaps an existing tier on one of the selected days.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        _db.CourtRateTiers.Add(new CourtRateTier
        {
            CourtId         = courtId,
            DaysOfWeek      = daysCsv,
            IncludeHolidays = includeHolidays,
            StartHour       = startHour,
            EndHour         = endHour,
            PricePerHour    = pricePerHour
        });
        await _db.SaveChangesAsync();
        var resynced = await _bookingService.ResyncUnpaidPricesAsync(courtId);

        var message = "Rate tier added.";
        if (OutOfHoursWarning(court, startHour, endHour) is { } warn) message = $"Rate tier added. {warn}";
        if (resynced > 0) message += $" {resynced} unpaid booking(s) updated to the new rate.";
        TempData["Success"] = message;
        return RedirectToAction(nameof(Schedule), new { id = courtId });
    }

    /// <summary>
    /// The availability grid only ever renders hours in [Court.OpeningHour, Court.ClosingHour), so a
    /// tier/block outside that range silently has no visible effect. Warn the owner instead of letting
    /// them wonder why nothing changed.
    /// </summary>
    private static string? OutOfHoursWarning(Court court, int startHour, int endHour) =>
        (startHour < court.OpeningHour || endHour > court.ClosingHour)
            ? $"Note: this court's operating hours are {TimeDisplay.HourRange(court.OpeningHour, court.ClosingHour)}, " +
              $"so the portion outside that range ({TimeDisplay.HourRange(startHour, endHour)}) won't appear on the availability grid " +
              "until you extend the court's hours."
            : null;

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRateTier(int id, int courtId)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var tier = await _db.CourtRateTiers.FirstOrDefaultAsync(t => t.Id == id && myCourtIds.Contains(t.CourtId));
        if (tier is not null)
        {
            _db.CourtRateTiers.Remove(tier);
            await _db.SaveChangesAsync();
            var resynced = await _bookingService.ResyncUnpaidPricesAsync(courtId);
            TempData["Success"] = resynced > 0
                ? $"Rate tier removed. {resynced} unpaid booking(s) updated to the new rate."
                : "Rate tier removed.";
        }
        return RedirectToAction(nameof(Schedule), new { id = courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditRateTier(int id, int courtId, string[] days, bool includeHolidays, int startHour, int endHour, decimal pricePerHour)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var tier = await _db.CourtRateTiers.FirstOrDefaultAsync(t => t.Id == id && myCourtIds.Contains(t.CourtId));
        if (tier is null) return NotFound();

        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        var daysCsv = NormalizeDays(days);
        if ((daysCsv.Length == 0 && !includeHolidays) || endHour <= startHour || startHour < 0 || endHour > 24)
        {
            TempData["Error"] = "Pick at least one day (or include holidays) and a valid hour range.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        var existing = await _bookingService.GetRateTiersAsync(courtId);
        bool overlaps = existing.Any(t => t.Id != id &&
            startHour < t.EndHour && endHour > t.StartHour &&
            DaysOverlap(t.DaysOfWeek, t.IncludeHolidays, daysCsv, includeHolidays));
        if (overlaps)
        {
            TempData["Error"] = "This rate tier overlaps an existing tier on one of the selected days.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        tier.DaysOfWeek      = daysCsv;
        tier.IncludeHolidays = includeHolidays;
        tier.StartHour       = startHour;
        tier.EndHour         = endHour;
        tier.PricePerHour    = pricePerHour;
        await _db.SaveChangesAsync();

        var resynced = await _bookingService.ResyncUnpaidPricesAsync(courtId);
        var message = resynced > 0
            ? $"Rate tier updated. {resynced} unpaid booking(s) updated to the new rate."
            : "Rate tier updated.";
        if (OutOfHoursWarning(court, startHour, endHour) is { } warn) message += $" {warn}";
        TempData["Success"] = message;
        return RedirectToAction(nameof(Schedule), new { id = courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddScheduleBlock(
        int courtId, string[] days, bool includeHolidays, int startHour, int endHour, BookingType type,
        bool allowPublicSignup = false, int? maxPlayers = null, decimal? pricePerHead = null, string? description = null)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        var daysCsv = NormalizeDays(days);
        if ((daysCsv.Length == 0 && !includeHolidays) || endHour <= startHour || startHour < 0 || endHour > 24)
        {
            TempData["Error"] = "Pick at least one day (or include holidays) and a valid hour range.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        // Price/Head and Max Players are captured for any Admin-Hosted Open Play block (front-desk
        // staff can charge and cap walk-in registrations there regardless of public sign-up) — but
        // public online sign-up specifically still needs both a capacity and a price to turn on.
        if (type != BookingType.AdminHostedOpenPlay) { allowPublicSignup = false; maxPlayers = null; pricePerHead = null; description = null; }
        if (allowPublicSignup && (!maxPlayers.HasValue || maxPlayers.Value < 1 || !pricePerHead.HasValue || pricePerHead.Value < 0))
        {
            TempData["Error"] = "To enable public sign-up, set a Max Players (at least 1) and a Price/Head.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        var existing = await _bookingService.GetScheduleBlocksAsync(courtId);
        bool overlaps = existing.Any(b =>
            startHour < b.EndHour && endHour > b.StartHour &&
            DaysOverlap(b.DaysOfWeek, b.IncludeHolidays, daysCsv, includeHolidays));
        if (overlaps)
        {
            TempData["Error"] = "This schedule block overlaps an existing block on one of the selected days.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        _db.CourtScheduleBlocks.Add(new CourtScheduleBlock
        {
            CourtId           = courtId,
            DaysOfWeek        = daysCsv,
            IncludeHolidays   = includeHolidays,
            StartHour         = startHour,
            EndHour           = endHour,
            Type              = type,
            AllowPublicSignup = allowPublicSignup,
            MaxPlayers        = maxPlayers,
            PricePerHead      = pricePerHead,
            Description       = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = OutOfHoursWarning(court, startHour, endHour) is { } warn
            ? $"Schedule block added. {warn}"
            : "Schedule block added.";
        return RedirectToAction(nameof(Schedule), new { id = courtId });
    }

    /// <summary>Lets an owner update an Admin-Hosted Open Play block's per-head price, capacity, and
    /// public sign-up toggle without deleting and re-adding the whole recurring schedule block.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditScheduleBlockPricing(int id, int courtId, decimal? pricePerHead, int? maxPlayers, bool allowPublicSignup = false)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var block = await _db.CourtScheduleBlocks.FirstOrDefaultAsync(b => b.Id == id && myCourtIds.Contains(b.CourtId));
        if (block is null)
        {
            TempData["Error"] = "Schedule block not found or you no longer have access to it.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        courtId = block.CourtId;

        if (block.Type != BookingType.AdminHostedOpenPlay)
        {
            TempData["Error"] = "Only Admin-Hosted Open Play blocks have a per-head price.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }
        if (allowPublicSignup && (!maxPlayers.HasValue || maxPlayers.Value < 1 || !pricePerHead.HasValue || pricePerHead.Value < 0))
        {
            TempData["Error"] = "To enable public sign-up, set a Max Players (at least 1) and a Price/Head.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        block.PricePerHead      = pricePerHead;
        block.MaxPlayers        = maxPlayers;
        block.AllowPublicSignup = allowPublicSignup;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Open Play pricing updated.";
        return RedirectToAction(nameof(Schedule), new { id = courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditScheduleBlock(
        int id, int courtId, string[] days, bool includeHolidays, int startHour, int endHour, BookingType type,
        bool allowPublicSignup = false, int? maxPlayers = null, decimal? pricePerHead = null, string? description = null)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var block = await _db.CourtScheduleBlocks.FirstOrDefaultAsync(b => b.Id == id && myCourtIds.Contains(b.CourtId));
        if (block is null)
        {
            TempData["Error"] = "Schedule block not found or you no longer have access to it.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        courtId = block.CourtId;

        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        var daysCsv = NormalizeDays(days);
        if ((daysCsv.Length == 0 && !includeHolidays) || endHour <= startHour || startHour < 0 || endHour > 24)
        {
            TempData["Error"] = "Pick at least one day (or include holidays) and a valid hour range.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        if (type != BookingType.AdminHostedOpenPlay) { allowPublicSignup = false; maxPlayers = null; pricePerHead = null; description = null; }
        if (allowPublicSignup && (!maxPlayers.HasValue || maxPlayers.Value < 1 || !pricePerHead.HasValue || pricePerHead.Value < 0))
        {
            TempData["Error"] = "To enable public sign-up, set a Max Players (at least 1) and a Price/Head.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        var existing = await _bookingService.GetScheduleBlocksAsync(courtId);
        bool overlaps = existing.Any(b => b.Id != id &&
            startHour < b.EndHour && endHour > b.StartHour &&
            DaysOverlap(b.DaysOfWeek, b.IncludeHolidays, daysCsv, includeHolidays));
        if (overlaps)
        {
            TempData["Error"] = "This schedule block overlaps an existing block on one of the selected days.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        block.DaysOfWeek        = daysCsv;
        block.IncludeHolidays   = includeHolidays;
        block.StartHour         = startHour;
        block.EndHour           = endHour;
        block.Type              = type;
        block.AllowPublicSignup = allowPublicSignup;
        block.MaxPlayers        = maxPlayers;
        block.PricePerHead      = pricePerHead;
        block.Description       = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await _db.SaveChangesAsync();

        TempData["Success"] = OutOfHoursWarning(court, startHour, endHour) is { } warn
            ? $"Schedule block updated. {warn}"
            : "Schedule block updated.";
        return RedirectToAction(nameof(Schedule), new { id = courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScheduleBlock(int id, int courtId)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var block = await _db.CourtScheduleBlocks.FirstOrDefaultAsync(b => b.Id == id && myCourtIds.Contains(b.CourtId));
        if (block is null)
        {
            TempData["Error"] = "Schedule block not found or it may have already been removed.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        _db.CourtScheduleBlocks.Remove(block);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Schedule block removed.";
        courtId = block.CourtId;
        return RedirectToAction(nameof(Schedule), new { id = courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleScheduleBlock(int id, int courtId)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var block = await _db.CourtScheduleBlocks.FirstOrDefaultAsync(b => b.Id == id && myCourtIds.Contains(b.CourtId));
        if (block is null)
        {
            TempData["Error"] = "Schedule block not found or you no longer have access to it.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        block.IsActive = !block.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = block.IsActive ? "Schedule block enabled." : "Schedule block paused.";
        courtId = block.CourtId;
        return RedirectToAction(nameof(Schedule), new { id = courtId });
    }

    // ── Facility Holidays ─────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddHoliday(DateOnly date, string? label)
    {
        bool duplicate = await _db.FacilityHolidays.AnyAsync(h => h.OwnerId == CurrentUserId && h.Date == date);
        if (duplicate)
        {
            TempData["Error"] = "That date is already marked as a holiday.";
        }
        else
        {
            _db.FacilityHolidays.Add(new FacilityHoliday
            {
                OwnerId = CurrentUserId,
                Date    = date,
                Label   = string.IsNullOrWhiteSpace(label) ? null : label.Trim()
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Holiday added.";
        }
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteHoliday(int id)
    {
        var holiday = await _db.FacilityHolidays.FirstOrDefaultAsync(h => h.Id == id && h.OwnerId == CurrentUserId);
        if (holiday is not null)
        {
            _db.FacilityHolidays.Remove(holiday);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Holiday removed.";
        }
        return RedirectToAction(nameof(Settings));
    }

    // ── Bundled Multi-Court "Peak Hours" Booking ─────────────────────────────────

    public async Task<IActionResult> Bundles()
    {
        var bundles = await _db.CourtBundles
            .Where(b => b.OwnerId == CurrentUserId)
            .Include(b => b.Courts).ThenInclude(c => c.Court)
            .ToListAsync();
        ViewBag.MyCourts = await MyCourts.ToListAsync();
        return View(bundles);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBundle(string name, int[] courtIds)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var validCourtIds = (courtIds ?? Array.Empty<int>()).Where(myCourtIds.Contains).Distinct().ToList();

        if (string.IsNullOrWhiteSpace(name) || validCourtIds.Count < 1)
        {
            TempData["Error"] = "Give the bundle a name and pick at least 1 of your courts.";
            return RedirectToAction(nameof(Bundles));
        }

        var bundle = new CourtBundle { OwnerId = CurrentUserId, Name = name.Trim() };
        bundle.Courts = validCourtIds.Select(cid => new CourtBundleCourt { CourtId = cid }).ToList();
        _db.CourtBundles.Add(bundle);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Bundle '{bundle.Name}' created.";
        return RedirectToAction(nameof(Bundles));
    }

    public async Task<IActionResult> EditBundle(int id)
    {
        var bundle = await _db.CourtBundles
            .Include(b => b.Courts).ThenInclude(bc => bc.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.OwnerId == CurrentUserId);
        if (bundle is null) return NotFound();

        var myCourts = await _db.Courts
            .Where(c => c.OwnerId == CurrentUserId)
            .OrderBy(c => c.Name)
            .ToListAsync();
        ViewBag.MyCourts = myCourts;
        return View(bundle);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBundle(int id, string name, int[] courtIds)
    {
        var bundle = await _db.CourtBundles
            .Include(b => b.Courts)
            .FirstOrDefaultAsync(b => b.Id == id && b.OwnerId == CurrentUserId);
        if (bundle is null) return NotFound();

        if (string.IsNullOrWhiteSpace(name) || courtIds.Length == 0)
        {
            TempData["Error"] = "Give the bundle a name and pick at least 1 of your courts.";
            return RedirectToAction(nameof(EditBundle), new { id });
        }

        var myCourts = await _db.Courts
            .Where(c => c.OwnerId == CurrentUserId)
            .Select(c => c.Id)
            .ToListAsync();
        var validCourtIds = courtIds.Where(cid => myCourts.Contains(cid)).Distinct().ToList();

        if (validCourtIds.Count == 0)
        {
            TempData["Error"] = "Give the bundle a name and pick at least 1 of your courts.";
            return RedirectToAction(nameof(EditBundle), new { id });
        }

        bundle.Name = name.Trim();
        bundle.Courts = validCourtIds.Select(cid => new CourtBundleCourt { CourtId = cid }).ToList();
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Bundle '{bundle.Name}' updated.";
        return RedirectToAction(nameof(Bundles));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBundle(int id)
    {
        var bundle = await _db.CourtBundles.FirstOrDefaultAsync(b => b.Id == id && b.OwnerId == CurrentUserId);
        if (bundle is not null)
        {
            bundle.IsActive = !bundle.IsActive;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Bundles));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBundle(int id)
    {
        var bundle = await _db.CourtBundles.FirstOrDefaultAsync(b => b.Id == id && b.OwnerId == CurrentUserId);
        if (bundle is not null)
        {
            _db.CourtBundles.Remove(bundle);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Bundle removed.";
        }
        return RedirectToAction(nameof(Bundles));
    }

    public async Task<IActionResult> BundleSchedule(int id)
    {
        var bundle = await _db.CourtBundles
            .Include(b => b.Courts).ThenInclude(c => c.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.OwnerId == CurrentUserId);
        if (bundle is null) return NotFound();

        ViewBag.Bundle    = bundle;
        ViewBag.RateBlocks = (await _bookingService.GetBundleRateBlocksAsync(id))
            .OrderBy(b => DaysSortKey(b.DaysOfWeek)).ThenBy(b => NormalizeDaysForSort(b.DaysOfWeek))
            .ThenBy(b => b.StartHour).ThenBy(b => b.EndHour).ToList();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBundleRateBlock(int bundleId, string[] days, bool includeHolidays, int startHour, int endHour, decimal flatPrice)
    {
        var bundle = await _db.CourtBundles
            .Include(b => b.Courts).ThenInclude(c => c.Court)
            .FirstOrDefaultAsync(b => b.Id == bundleId && b.OwnerId == CurrentUserId);
        if (bundle is null) return NotFound();

        var daysCsv = NormalizeDays(days);
        if ((daysCsv.Length == 0 && !includeHolidays) || endHour <= startHour || startHour < 0 || endHour > 24)
        {
            TempData["Error"] = "Pick at least one day (or include holidays) and a valid hour range.";
            return RedirectToAction(nameof(BundleSchedule), new { id = bundleId });
        }

        var existing = await _bookingService.GetBundleRateBlocksAsync(bundleId);
        bool overlaps = existing.Any(b =>
            startHour < b.EndHour && endHour > b.StartHour &&
            DaysOverlap(b.DaysOfWeek, b.IncludeHolidays, daysCsv, includeHolidays));
        if (overlaps)
        {
            TempData["Error"] = "This window overlaps an existing bundle window on one of the selected days.";
            return RedirectToAction(nameof(BundleSchedule), new { id = bundleId });
        }

        _db.CourtBundleRateBlocks.Add(new CourtBundleRateBlock
        {
            CourtBundleId   = bundleId,
            DaysOfWeek      = daysCsv,
            IncludeHolidays = includeHolidays,
            StartHour       = startHour,
            EndHour         = endHour,
            FlatPrice       = flatPrice
        });
        await _db.SaveChangesAsync();

        var outOfHoursCourts = bundle.Courts
            .Select(c => c.Court)
            .Where(c => startHour < c.OpeningHour || endHour > c.ClosingHour)
            .Select(c => c.Name)
            .ToList();
        TempData["Success"] = outOfHoursCourts.Count > 0
            ? $"Bundle window added. Note: {string.Join(", ", outOfHoursCourts)} don't operate the full {TimeDisplay.HourRange(startHour, endHour)} window, so this bundle won't be sellable until their hours are extended."
            : "Bundle window added.";
        return RedirectToAction(nameof(BundleSchedule), new { id = bundleId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBundleRateBlock(int id, int bundleId)
    {
        var block = await _db.CourtBundleRateBlocks
            .FirstOrDefaultAsync(b => b.Id == id && b.CourtBundleId == bundleId &&
                                       b.CourtBundle.OwnerId == CurrentUserId);
        if (block is not null)
        {
            _db.CourtBundleRateBlocks.Remove(block);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Bundle window removed.";
        }
        return RedirectToAction(nameof(BundleSchedule), new { id = bundleId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBundleRateBlock(int id, int bundleId)
    {
        var block = await _db.CourtBundleRateBlocks
            .FirstOrDefaultAsync(b => b.Id == id && b.CourtBundleId == bundleId &&
                                       b.CourtBundle.OwnerId == CurrentUserId);
        if (block is not null)
        {
            block.IsActive = !block.IsActive;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(BundleSchedule), new { id = bundleId });
    }

    [HttpGet]
    public async Task<IActionResult> EditBundleRateBlock(int id, int bundleId)
    {
        var bundle = await _db.CourtBundles
            .FirstOrDefaultAsync(b => b.Id == bundleId && b.OwnerId == CurrentUserId);
        if (bundle is null) return NotFound();

        var block = await _db.CourtBundleRateBlocks
            .FirstOrDefaultAsync(b => b.Id == id && b.CourtBundleId == bundleId);
        if (block is null) return NotFound();

        ViewBag.Bundle = bundle;
        return View(block);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBundleRateBlock(int id, int bundleId, string[] days, int startHour, int endHour, decimal flatPrice, bool includeHolidays)
    {
        var bundle = await _db.CourtBundles
            .FirstOrDefaultAsync(b => b.Id == bundleId && b.OwnerId == CurrentUserId);
        if (bundle is null) return NotFound();

        var block = await _db.CourtBundleRateBlocks
            .FirstOrDefaultAsync(b => b.Id == id && b.CourtBundleId == bundleId);
        if (block is null) return NotFound();

        var daysCsv = NormalizeDays(days);
        if ((daysCsv.Length == 0 && !includeHolidays) || startHour >= endHour || startHour < 0 || endHour > 24 || flatPrice <= 0)
        {
            TempData["Error"] = "Pick at least one day (or include holidays), valid hours, and a price > 0.";
            return RedirectToAction(nameof(EditBundleRateBlock), new { id, bundleId });
        }

        var existing = await _bookingService.GetBundleRateBlocksAsync(bundleId);
        bool overlaps = existing.Any(b =>
            b.Id != id &&
            startHour < b.EndHour && endHour > b.StartHour &&
            DaysOverlap(b.DaysOfWeek, b.IncludeHolidays, daysCsv, includeHolidays));
        if (overlaps)
        {
            TempData["Error"] = "This window overlaps an existing bundle window on one of the selected days.";
            return RedirectToAction(nameof(EditBundleRateBlock), new { id, bundleId });
        }

        block.DaysOfWeek = daysCsv;
        block.StartHour = startHour;
        block.EndHour = endHour;
        block.FlatPrice = flatPrice;
        block.IncludeHolidays = includeHolidays;

        await _db.SaveChangesAsync();

        TempData["Success"] = "Peak window updated.";
        return RedirectToAction(nameof(BundleSchedule), new { id = bundleId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmBundlePayment(Guid groupId)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var rows = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .Where(b => b.BundleGroupId == groupId && courtIds.Contains(b.CourtId))
            .ToListAsync();
        if (rows.Count == 0) return NotFound();

        var settings = await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == CurrentUserId);
        foreach (var booking in rows)
        {
            booking.Status        = BookingStatus.Confirmed;
            booking.PaymentStatus  = PaymentStatus.Paid;
            booking.PaidAt         = DateTime.UtcNow;

            if (settings?.IsCommissionModel == true && booking.TotalPrice > 0)
            {
                var commission = Math.Round(booking.TotalPrice * settings.CommissionRate / 100m, 2);
                booking.CommissionAmount        = commission;
                settings.CommissionBalanceOwed += commission;
            }
        }
        await _db.SaveChangesAsync();

        var first = rows[0];
        if (!string.IsNullOrWhiteSpace(first.User?.Email))
        {
            var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            await SendGroupPaymentConfirmedEmailAsync(rows, first.User, baseUrl);
        }

        TempData["Success"] = "Booking confirmed — the customer has been emailed a confirmation.";
        return RedirectToAction(nameof(Bookings), new { awaitingConfirmation = true });
    }

    /// <summary>
    /// Confirmation email for a <see cref="Booking.BundleGroupId"/> group — a real CourtBundle
    /// purchase (every row sharing one date/time window) or an ad-hoc multi-court cart checkout
    /// (rows can each have their own date/time). Lists every row individually rather than
    /// picking one row's date/time to stand in for the whole group, which would silently drop
    /// the other slots' schedule info whenever they differ.
    /// </summary>
    private async Task SendGroupPaymentConfirmedEmailAsync(List<Booking> rows, ApplicationUser user, string baseUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(user.Email)) return;

            rows = rows.OrderBy(r => r.BookingDate).ThenBy(r => r.StartTime).ToList();
            var first        = rows[0];
            var greeting     = string.IsNullOrWhiteSpace(user.FirstName) ? "Hi there" : $"Hi {user.FirstName}";
            var amount        = rows.Sum(r => r.TotalPrice).ToString("N0");
            var method        = string.IsNullOrWhiteSpace(first.PaymentMethod) ? "Online payment" : first.PaymentMethod;
            var refLine       = string.IsNullOrWhiteSpace(first.PaymentReference) ? "" :
                                $"<tr><td style='color:#6c757d;padding:5px 0;'>Reference</td><td style='padding:5px 0;font-family:monospace;font-size:13px;'>{first.PaymentReference}</td></tr>";
            var myBookings    = $"{baseUrl.TrimEnd('/')}/Bookings/My";
            var myBookingsButton = user.IsGuest ? "" : $@"
      <p style='margin:20px 0 0;text-align:center;'>
        <a href='{myBookings}' style='display:inline-block;background:#198754;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>View My Bookings</a>
      </p>";
            var rowsHtml = string.Join("", rows.Select(r =>
                $"<tr><td style='padding:4px 0;color:#212529;'>{r.Court?.Name ?? "Court"}</td>" +
                $"<td style='padding:4px 0;color:#6c757d;'>{r.BookingDate:MMM d, yyyy}, {r.StartTime:hh\\:mm tt} – {r.EndTime:hh\\:mm tt}</td>" +
                $"<td style='padding:4px 0;text-align:right;font-weight:600;'>₱{r.TotalPrice:N0}</td></tr>"));
            var rowsPlain = string.Join("\n", rows.Select(r =>
                $"- {r.Court?.Name ?? "Court"}: {r.BookingDate:MMM d, yyyy}, {r.StartTime:hh\\:mm tt} – {r.EndTime:hh\\:mm tt} (₱{r.TotalPrice:N0})"));

            var subjectLabel = rows.Count == 1 ? (first.Court?.Name ?? "Booking") : $"{rows.Count} slots";

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:560px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#198754;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.9;letter-spacing:.5px;text-transform:uppercase;'>Booking Confirmed</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>✅ Payment Received</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>{greeting}, your payment has been received and your booking is now <strong style='color:#198754;'>confirmed</strong>.</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>{rowsHtml}</table>
      <table style='width:100%;border-collapse:collapse;font-size:14px;margin-top:12px;border-top:1px solid #e9ecef;padding-top:8px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Total</td> <td style='padding:5px 0;font-weight:600;color:#198754;'>₱{amount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Method</td><td style='padding:5px 0;'>{method}</td></tr>
        {refLine}
      </table>{myBookingsButton}
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      Automated confirmation
    </div>
  </div>
</body></html>";

            var plain = $"Payment Received — {subjectLabel} Confirmed\n\n{greeting},\n\n{rowsPlain}\n\nTotal: ₱{amount} via {method}. Your booking is now confirmed."
                      + (user.IsGuest ? "" : $"\n\nView your bookings: {myBookings}");

            await _email.SendAsync(user.Email!, $"✅ Booking Confirmed — {subjectLabel}", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AdminController] Failed to send group payment confirmed email");
        }
    }

    public async Task<IActionResult> RejectBundlePayment(Guid groupId)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var rows = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .Where(b => b.BundleGroupId == groupId && courtIds.Contains(b.CourtId))
            .ToListAsync();
        if (rows.Count == 0) return NotFound();

        rows = rows.OrderBy(r => r.BookingDate).ThenBy(r => r.StartTime).ToList();
        var first         = rows[0];
        var customerEmail = first.User?.Email;
        var customerName  = first.User?.FirstName;

        foreach (var booking in rows)
        {
            booking.Status           = BookingStatus.Cancelled;
            booking.PaymentReference = null;
            booking.PaymentProofPath = null;
        }
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(customerEmail))
            await SendGroupPaymentRejectedEmailAsync(customerEmail!, customerName, rows);

        TempData["Error"] = "Booking rejected and cancelled — the customer has been notified.";
        return RedirectToAction(nameof(Bookings), new { awaitingConfirmation = true });
    }

    /// <summary>Group-aware counterpart to <see cref="SendPaymentRejectedEmailAsync"/> — lists every
    /// row in the <see cref="Booking.BundleGroupId"/> group individually instead of picking one row's
    /// date/time to stand in for the whole group (see <see cref="SendGroupPaymentConfirmedEmailAsync"/>
    /// for why that's wrong once rows can have different dates/times, as an ad-hoc cart checkout does).</summary>
    private async Task SendGroupPaymentRejectedEmailAsync(string toEmail, string? firstName, List<Booking> rows)
    {
        try
        {
            var greeting  = string.IsNullOrWhiteSpace(firstName) ? "Hi there" : $"Hi {firstName}";
            var baseUrl   = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var browseUrl = $"{baseUrl}/Courts";
            var rowsHtml = string.Join("", rows.Select(r =>
                $"<tr><td style='padding:4px 0;color:#212529;'>{r.Court?.Name ?? "Court"}</td>" +
                $"<td style='padding:4px 0;color:#6c757d;'>{r.BookingDate:MMM d, yyyy}, {r.StartTime:hh\\:mm tt} – {r.EndTime:hh\\:mm tt}</td></tr>"));
            var rowsPlain = string.Join("\n", rows.Select(r =>
                $"- {r.Court?.Name ?? "Court"}: {r.BookingDate:MMM d, yyyy}, {r.StartTime:hh\\:mm tt} – {r.EndTime:hh\\:mm tt}"));

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#dc3545;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.9;letter-spacing:.5px;text-transform:uppercase;'>Booking Update</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>Payment Not Confirmed</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>{greeting}, unfortunately the facility could not confirm your payment for the booking below, so it has been <strong>cancelled</strong>.</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>{rowsHtml}</table>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{browseUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Browse Courts</a>
      </p>
    </div>
  </div>
</body></html>";

            var plain = $"Payment Not Confirmed\n\n{greeting},\n\n{rowsPlain}\n\nThis booking has been cancelled. Browse courts: {browseUrl}";
            await _email.SendAsync(toEmail, "Booking Update — Payment Not Confirmed", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AdminController] Failed to send group payment rejected email");
        }
    }

    // ── Open Play Sign-ups ───────────────────────────────────────────────────────

    public async Task<IActionResult> OpenPlaySignups()
    {
        var courtIds = await GetMyCourtIdsAsync();
        var signups = await _db.OpenPlaySignups
            .Include(s => s.Court)
            .Include(s => s.User)
            .Where(s => courtIds.Contains(s.CourtId) && s.Status != BookingStatus.Cancelled)
            .OrderBy(s => s.BookingDate).ThenBy(s => s.StartHour)
            .ToListAsync();

        // Group into sessions so the roster + headcount-vs-max reads naturally.
        var sessions = signups
            .GroupBy(s => (s.CourtId, s.BookingDate, s.StartHour, s.EndHour))
            .Select(g => new OpenPlaySessionRow
            {
                CourtId     = g.Key.CourtId,
                Court       = g.First().Court,
                BookingDate = g.Key.BookingDate,
                StartHour   = g.Key.StartHour,
                EndHour     = g.Key.EndHour,
                Signups     = g.OrderBy(s => s.CreatedAt).ToList(),
                Taken       = g.Sum(s => s.SpotCount)
            })
            .ToList();

        ViewBag.Sessions = sessions;
        ViewBag.PendingSignupCount = signups.Count(s => s.Status == BookingStatus.Pending && s.PaymentProofSubmittedAt != null);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmSignupPayment(int id)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var signup = await _db.OpenPlaySignups
            .Include(s => s.Court)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id && courtIds.Contains(s.CourtId));
        if (signup is null) return NotFound();

        signup.Status        = BookingStatus.Confirmed;
        signup.PaymentStatus = PaymentStatus.Paid;
        signup.PaidAt         = DateTime.UtcNow;

        var settings = await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == CurrentUserId);
        if (settings?.IsCommissionModel == true && signup.TotalPrice > 0)
        {
            var commission = Math.Round(signup.TotalPrice * settings.CommissionRate / 100m, 2);
            signup.CommissionAmount        = commission;
            settings.CommissionBalanceOwed += commission;
        }

        await _db.SaveChangesAsync();

        if (signup.Court is not null && !string.IsNullOrWhiteSpace(signup.User?.Email))
        {
            var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            await _email.SendBookingConfirmedToCustomerAsync(
                signup.User.Email!,
                signup.User.FirstName,
                signup.Id,
                $"Open Play — {signup.Court.Name} ({signup.SpotCount} spot{(signup.SpotCount != 1 ? "s" : "")})",
                signup.BookingDate,
                new TimeOnly(signup.StartHour % 24, 0),
                new TimeOnly(signup.EndHour % 24, 0),
                signup.TotalPrice,
                signup.PaymentMethod,
                signup.PaymentReference,
                baseUrl,
                signup.User.IsGuest);
        }

        TempData["Success"] = "Sign-up confirmed — the customer has been emailed a confirmation.";
        return RedirectToAction(nameof(OpenPlaySignups));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectSignupPayment(int id)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var signup = await _db.OpenPlaySignups
            .Include(s => s.Court)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == id && courtIds.Contains(s.CourtId));
        if (signup is null) return NotFound();

        var customerEmail = signup.User?.Email;
        var customerName  = signup.User?.FirstName;
        var courtName     = signup.Court?.Name ?? "the session";
        var bookingDate   = signup.BookingDate;
        var startTime     = new TimeOnly(signup.StartHour % 24, 0);
        var endTime       = new TimeOnly(signup.EndHour % 24, 0);

        signup.Status           = BookingStatus.Cancelled;
        signup.PaymentReference = null;
        signup.PaymentProofPath = null;
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(customerEmail))
            await SendPaymentRejectedEmailAsync(customerEmail!, customerName, signup.Id,
                courtName, bookingDate, startTime, endTime);

        TempData["Error"] = "Sign-up rejected and cancelled — the customer has been notified.";
        return RedirectToAction(nameof(OpenPlaySignups));
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    public async Task<IActionResult> Settings()
    {
        var settings = await GetMySettingsAsync() ?? new FacilitySettings();
        ViewBag.Holidays = await _db.FacilityHolidays
            .Where(h => h.OwnerId == CurrentUserId)
            .OrderBy(h => h.Date)
            .ToListAsync();
        return View(settings);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(FacilitySettings model, IFormFile? logo,
        IFormFile? gcashQr, IFormFile? mayaQr, IFormFile? gotymeQr, string[]? paymentMethods)
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
        bool isNew = settings is null;
        if (isNew)
        {
            settings = new FacilitySettings { OwnerId = CurrentUserId };
            _db.FacilitySettings.Add(settings);
        }

        settings!.FacilityName       = model.FacilityName;
        settings.Address             = model.Address;
        settings.GCashNumber         = model.GCashNumber;
        settings.GCashName           = model.GCashName;
        settings.MayaNumber          = model.MayaNumber;
        settings.MayaName            = model.MayaName;
        settings.GoTymeNumber        = model.GoTymeNumber;
        settings.GoTymeName          = model.GoTymeName;

        if (gcashQr is { Length: > 0 })
            settings.GCashQrCodePath = await SaveQrCodeAsync(gcashQr, "gcash", settings.GCashQrCodePath);
        if (mayaQr is { Length: > 0 })
            settings.MayaQrCodePath  = await SaveQrCodeAsync(mayaQr,  "maya",  settings.MayaQrCodePath);
        if (gotymeQr is { Length: > 0 })
            settings.GoTymeQrCodePath = await SaveQrCodeAsync(gotymeQr, "gotyme", settings.GoTymeQrCodePath);
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
        settings.HouseRules   = string.IsNullOrWhiteSpace(model.HouseRules)   ? null : model.HouseRules.Trim();

        if (logo is { Length: > 0 })
            settings.BrandLogoUrl = await SaveBrandLogoAsync(logo, settings.BrandLogoUrl);

        if (!ModelState.IsValid) return View(settings);

        await _db.SaveChangesAsync();
        TempData["Success"] = "Settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    // ── Owner-initiated facility deactivation ─────────────────────────────────

    /// <summary>
    /// Takes the owner's facility offline: courts are hidden from customers and no
    /// new bookings are accepted. Reversible via <see cref="ReactivateFacility"/>.
    /// Existing bookings are preserved. Does not touch admin suspension.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateFacility()
    {
        var settings = await GetMySettingsAsync();
        if (settings is null)
        {
            TempData["Error"] = "No facility found to deactivate.";
            return RedirectToAction(nameof(Settings));
        }

        settings.IsDeactivated = true;
        settings.DeactivatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Your facility has been deactivated and is now hidden from customers. "
                            + "You can reactivate it anytime.";
        return RedirectToAction(nameof(Settings));
    }

    /// <summary>Brings a previously deactivated facility back online.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivateFacility()
    {
        var settings = await GetMySettingsAsync();
        if (settings is null)
        {
            TempData["Error"] = "No facility found to reactivate.";
            return RedirectToAction(nameof(Settings));
        }

        settings.IsDeactivated = false;
        settings.DeactivatedAt = null;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Your facility is back online and visible to customers again.";
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
        var fileName = $"court_{courtId}_{Guid.NewGuid():N}.jpg";
        var fullPath = Path.Combine(dir, fileName);
        byte[] compressed;
        try
        {
            await using var source = photo.OpenReadStream();
            compressed = await _imageCompression.CompressAsync(source);
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            return existing;
        }
        await System.IO.File.WriteAllBytesAsync(fullPath, compressed);
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

    /// <summary>
    /// Returns a description of the first existing Confirmed/Completed booking or Open Play
    /// sign-up that overlaps the proposed block range, or null if the range is clear. Pending
    /// bookings are deliberately excluded — they can still expire on their own and shouldn't
    /// block an admin from blocking that time.
    /// </summary>
    private async Task<string?> FindBlockConflictAsync(int courtId,
        DateOnly startDate, int startHour, DateOnly endDate, int endHour)
    {
        static DateTime ToInstant(DateOnly date, int hour) =>
            date.AddDays(hour / 24).ToDateTime(new TimeOnly(hour % 24, 0));
        var blockStart = ToInstant(startDate, startHour);
        var blockEnd   = ToInstant(endDate, endHour);

        var bookings = await _db.Bookings
            .Where(b => b.CourtId == courtId
                     && b.BookingDate >= startDate && b.BookingDate <= endDate
                     && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed))
            .AsNoTracking()
            .ToListAsync();

        foreach (var b in bookings)
        {
            var bStartHour = b.StartTime.Hour;
            var bEndHour   = b.EndTime == TimeOnly.MinValue ? 24 : b.EndTime.Hour;
            if (ToInstant(b.BookingDate, bStartHour) < blockEnd && ToInstant(b.BookingDate, bEndHour) > blockStart)
                return $"{b.BookingDate:MMM d} {TimeDisplay.HourRange(bStartHour, bEndHour)} is already booked";
        }

        var signups = await _db.OpenPlaySignups
            .Where(o => o.CourtId == courtId
                     && o.BookingDate >= startDate && o.BookingDate <= endDate
                     && (o.Status == BookingStatus.Confirmed || o.Status == BookingStatus.Completed))
            .AsNoTracking()
            .ToListAsync();

        foreach (var o in signups)
        {
            if (ToInstant(o.BookingDate, o.StartHour) < blockEnd && ToInstant(o.BookingDate, o.EndHour) > blockStart)
                return $"{o.BookingDate:MMM d} {TimeDisplay.HourRange(o.StartHour, o.EndHour)} already has an Open Play sign-up";
        }

        return null;
    }

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

        // Basic validation. Hour can be 24 (midnight/end-of-day, e.g. an "8pm-12am" block) — TimeOnly
        // only accepts 0-23, so an hour of 24 means "midnight at the start of the next calendar day".
        static DateTime ToInstant(DateOnly date, int hour) =>
            date.AddDays(hour / 24).ToDateTime(new TimeOnly(hour % 24, 0));
        var startDt = ToInstant(startDate, startHour);
        var endDt   = ToInstant(endDate, endHour);
        if (endDt <= startDt)
        {
            TempData["Error"] = "End must be after start.";
            return RedirectToAction(nameof(BlockCourt), new { id = courtId });
        }

        var conflict = await FindBlockConflictAsync(courtId, startDate, startHour, endDate, endHour);
        if (conflict is not null)
        {
            TempData["Error"] = $"Can't block {court.Name} — {conflict}.";
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

        TempData["Success"] = $"Court blocked from {startDate:MMM d} {TimeDisplay.Hour(startHour)} to {endDate:MMM d} {TimeDisplay.Hour(endHour)}.";
        return RedirectToAction(nameof(BlockCourt), new { id = courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCourtBlock(int id, int courtId,
        DateOnly startDate, int startHour,
        DateOnly endDate,   int endHour,
        string?  reason)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var blk = await _db.CourtBlocks.FirstOrDefaultAsync(b =>
            b.Id == id && myCourtIds.Contains(b.CourtId));
        if (blk is null)
        {
            TempData["Error"] = "Block not found.";
            return RedirectToAction(nameof(BlockCourt), new { id = courtId });
        }

        static DateTime ToInstant(DateOnly date, int hour) =>
            date.AddDays(hour / 24).ToDateTime(new TimeOnly(hour % 24, 0));
        if (ToInstant(endDate, endHour) <= ToInstant(startDate, startHour))
        {
            TempData["Error"] = "End must be after start.";
            return RedirectToAction(nameof(BlockCourt), new { id = courtId });
        }

        var conflict = await FindBlockConflictAsync(blk.CourtId, startDate, startHour, endDate, endHour);
        if (conflict is not null)
        {
            TempData["Error"] = $"Can't update block — {conflict}.";
            return RedirectToAction(nameof(BlockCourt), new { id = courtId });
        }

        blk.StartDate = startDate;
        blk.StartHour = startHour;
        blk.EndDate   = endDate;
        blk.EndHour   = endHour;
        blk.Reason    = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await _db.SaveChangesAsync();

        TempData["Success"] = "Block updated.";
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

    // ── Bulk block across multiple courts at once ────────────────────────────

    public async Task<IActionResult> BlockCourts()
    {
        var courts = await MyCourts.OrderBy(c => c.Name).ToListAsync();
        var courtIds = courts.Select(c => c.Id).ToList();

        var blocks = await _db.CourtBlocks
            .Where(b => courtIds.Contains(b.CourtId))
            .OrderByDescending(b => b.StartDate).ThenByDescending(b => b.StartHour)
            .ToListAsync();

        ViewBag.Courts     = courts;
        ViewBag.BlockCourt = courts.ToDictionary(c => c.Id, c => c.Name);
        return View(blocks);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCourtBlockBulk(int[] courtIds, string? mode,
        DateOnly startDate, int startHour,
        DateOnly endDate,   int endHour,
        string?  reason)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var targetIds  = (courtIds ?? Array.Empty<int>()).Where(myCourtIds.Contains).Distinct().ToList();

        if (!targetIds.Any())
        {
            TempData["Error"] = "Select at least one court to block.";
            return RedirectToAction(nameof(BlockCourts));
        }

        static DateTime ToInstant(DateOnly date, int hour) =>
            date.AddDays(hour / 24).ToDateTime(new TimeOnly(hour % 24, 0));

        if (string.Equals(mode, "custom", StringComparison.OrdinalIgnoreCase))
            return await AddCourtBlockBulkCustom(targetIds);

        var startDt = ToInstant(startDate, startHour);
        var endDt   = ToInstant(endDate, endHour);
        if (endDt <= startDt)
        {
            TempData["Error"] = "End must be after start.";
            return RedirectToAction(nameof(BlockCourts));
        }

        var reasonTrimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var courtNamesForBulk = await _db.Courts.Where(c => targetIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);
        var addedBulk  = new List<int>();
        var errorsBulk = new List<string>();
        foreach (var cid in targetIds)
        {
            var conflict = await FindBlockConflictAsync(cid, startDate, startHour, endDate, endHour);
            if (conflict is not null)
            {
                errorsBulk.Add($"{courtNamesForBulk.GetValueOrDefault(cid, $"Court #{cid}")}: {conflict}.");
                continue;
            }

            _db.CourtBlocks.Add(new CourtBlock
            {
                CourtId   = cid,
                StartDate = startDate,
                StartHour = startHour,
                EndDate   = endDate,
                EndHour   = endHour,
                Reason    = reasonTrimmed
            });
            addedBulk.Add(cid);
        }

        if (addedBulk.Any())
            await _db.SaveChangesAsync();

        if (addedBulk.Any())
            TempData["Success"] = $"Blocked {addedBulk.Count} court{(addedBulk.Count == 1 ? "" : "s")} from " +
                                   $"{startDate:MMM d} {TimeDisplay.Hour(startHour)} to {endDate:MMM d} {TimeDisplay.Hour(endHour)}.";
        if (errorsBulk.Any())
            TempData["Error"] = string.Join(" ", errorsBulk);
        return RedirectToAction(nameof(BlockCourts));
    }

    // Each court in targetIds gets its own Start/End Date+Hour, read from
    // per-court form fields named "startDate_{courtId}", "startHour_{courtId}", etc.
    private async Task<IActionResult> AddCourtBlockBulkCustom(List<int> targetIds)
    {
        static DateTime ToInstant(DateOnly date, int hour) =>
            date.AddDays(hour / 24).ToDateTime(new TimeOnly(hour % 24, 0));

        var courtNames = await _db.Courts.Where(c => targetIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var added  = new List<string>();
        var errors = new List<string>();

        foreach (var cid in targetIds)
        {
            var name = courtNames.GetValueOrDefault(cid, $"Court #{cid}");

            var sd = Request.Form[$"startDate_{cid}"].ToString();
            var sh = Request.Form[$"startHour_{cid}"].ToString();
            var ed = Request.Form[$"endDate_{cid}"].ToString();
            var eh = Request.Form[$"endHour_{cid}"].ToString();
            var rs = Request.Form[$"reason_{cid}"].ToString();

            if (!DateOnly.TryParse(sd, out var sDate) || !int.TryParse(sh, out var sHour) ||
                !DateOnly.TryParse(ed, out var eDate) || !int.TryParse(eh, out var eHour))
            {
                errors.Add($"{name}: invalid date/time.");
                continue;
            }

            if (ToInstant(eDate, eHour) <= ToInstant(sDate, sHour))
            {
                errors.Add($"{name}: end must be after start.");
                continue;
            }

            var conflict = await FindBlockConflictAsync(cid, sDate, sHour, eDate, eHour);
            if (conflict is not null)
            {
                errors.Add($"{name}: {conflict}.");
                continue;
            }

            _db.CourtBlocks.Add(new CourtBlock
            {
                CourtId   = cid,
                StartDate = sDate,
                StartHour = sHour,
                EndDate   = eDate,
                EndHour   = eHour,
                Reason    = string.IsNullOrWhiteSpace(rs) ? null : rs.Trim()
            });
            added.Add(name);
        }

        if (added.Any())
            await _db.SaveChangesAsync();

        if (added.Any())
            TempData["Success"] = $"Blocked {added.Count} court{(added.Count == 1 ? "" : "s")} with custom timing: {string.Join(", ", added)}.";
        if (errors.Any())
            TempData["Error"] = string.Join(" ", errors);

        return RedirectToAction(nameof(BlockCourts));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCourtBlockBulk(int id,
        DateOnly startDate, int startHour,
        DateOnly endDate,   int endHour,
        string?  reason)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var blk = await _db.CourtBlocks.FirstOrDefaultAsync(b =>
            b.Id == id && myCourtIds.Contains(b.CourtId));
        if (blk is null)
        {
            TempData["Error"] = "Block not found.";
            return RedirectToAction(nameof(BlockCourts));
        }

        static DateTime ToInstant(DateOnly date, int hour) =>
            date.AddDays(hour / 24).ToDateTime(new TimeOnly(hour % 24, 0));
        if (ToInstant(endDate, endHour) <= ToInstant(startDate, startHour))
        {
            TempData["Error"] = "End must be after start.";
            return RedirectToAction(nameof(BlockCourts));
        }

        var conflict = await FindBlockConflictAsync(blk.CourtId, startDate, startHour, endDate, endHour);
        if (conflict is not null)
        {
            TempData["Error"] = $"Can't update block — {conflict}.";
            return RedirectToAction(nameof(BlockCourts));
        }

        blk.StartDate = startDate;
        blk.StartHour = startHour;
        blk.EndDate   = endDate;
        blk.EndHour   = endHour;
        blk.Reason    = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await _db.SaveChangesAsync();

        TempData["Success"] = "Block updated.";
        return RedirectToAction(nameof(BlockCourts));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCourtBlockBulk(int id)
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
        return RedirectToAction(nameof(BlockCourts));
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
        // This dropdown is the only status-setter that doesn't already pair with a payment
        // update (ConfirmPayment/webhooks/walk-ins all flip both together) — without this,
        // forcing a still-Unpaid booking to Confirmed here leaves Payment stuck showing
        // "Submitted"/"Unpaid" forever even though the booking itself now reads Confirmed.
        if ((status == BookingStatus.Confirmed || status == BookingStatus.Completed) && booking.PaymentStatus != PaymentStatus.Paid)
        {
            booking.PaymentStatus = PaymentStatus.Paid;
            booking.PaidAt ??= DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Booking status updated.";
        return RedirectToAction(nameof(Bookings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSignupStatus(int id, BookingStatus status)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var signup   = await _db.OpenPlaySignups.FirstOrDefaultAsync(sg => sg.Id == id && courtIds.Contains(sg.CourtId));
        if (signup is null) return NotFound();
        signup.Status = status;
        if ((status == BookingStatus.Confirmed || status == BookingStatus.Completed) && signup.PaymentStatus != PaymentStatus.Paid)
        {
            signup.PaymentStatus = PaymentStatus.Paid;
            signup.PaidAt ??= DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Sign-up status updated.";
        return RedirectToAction(nameof(Bookings));
    }

    // ── Staff accounts (front-desk role, scoped to this owner) ──────────────────

    public async Task<IActionResult> Staff()
    {
        ViewBag.StaffList = await _db.Users
            .Where(u => u.EmployerOwnerId == CurrentUserId)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStaff(string firstName, string lastName, string email, string phone, string password)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            TempData["Error"] = "Name, email, and password are required.";
            return RedirectToAction(nameof(Staff));
        }

        var staff = new ApplicationUser
        {
            UserName        = email.Trim(),
            Email           = email.Trim(),
            FirstName       = firstName.Trim(),
            LastName        = lastName?.Trim() ?? "",
            PhoneNumber     = phone?.Trim(),
            EmailConfirmed  = true,
            EmployerOwnerId = CurrentUserId
        };

        var result = await _userManager.CreateAsync(staff, password);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Staff));
        }

        await _userManager.AddToRoleAsync(staff, "Staff");
        TempData["Success"] = $"Staff account created for {staff.FullName}.";
        return RedirectToAction(nameof(Staff));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStaffActive(string id)
    {
        var staff = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.EmployerOwnerId == CurrentUserId);
        if (staff is null) return NotFound();

        bool isCurrentlyDisabled = staff.LockoutEnd.HasValue && staff.LockoutEnd > DateTimeOffset.UtcNow;
        if (isCurrentlyDisabled)
        {
            await _userManager.SetLockoutEndDateAsync(staff, null);
            TempData["Success"] = $"{staff.FullName} can log in again.";
        }
        else
        {
            await _userManager.SetLockoutEnabledAsync(staff, true);
            await _userManager.SetLockoutEndDateAsync(staff, DateTimeOffset.MaxValue);
            TempData["Success"] = $"{staff.FullName}'s access has been disabled.";
        }
        return RedirectToAction(nameof(Staff));
    }

    // ── Cash reconciliation: every staff member's logged cash bookings ──────────

    public async Task<IActionResult> CashLog(DateOnly? from, DateOnly? to, string? staffId)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var bookings = await _bookingService.GetCashLogAsync(courtIds, staffId, from, to, CurrentUserId);

        var staffIds = bookings.Select(b => b.LoggedByStaffId!).Distinct().ToList();
        var staffNames = await _db.Users
            .Where(u => staffIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        ViewBag.StaffNames = staffNames;
        ViewBag.StaffList  = await _db.Users.Where(u => u.EmployerOwnerId == CurrentUserId).ToListAsync();
        ViewBag.From       = from;
        ViewBag.To         = to;
        ViewBag.StaffId    = staffId;
        var confirmed = bookings.Where(b => b.Status != BookingStatus.Pending).ToList();
        ViewBag.GrandTotal   = confirmed.Sum(b => b.TotalPrice);
        ViewBag.CashTotal    = confirmed.Where(b => string.Equals(b.PaymentMethod, "Cash", StringComparison.OrdinalIgnoreCase)).Sum(b => b.TotalPrice);
        ViewBag.DigitalTotal = confirmed.Where(b => !string.Equals(b.PaymentMethod, "Cash", StringComparison.OrdinalIgnoreCase)).Sum(b => b.TotalPrice);
        ViewBag.PendingTotal = bookings.Where(b => b.Status == BookingStatus.Pending).Sum(b => b.TotalPrice);
        ViewBag.PendingCount = bookings.Count(b => b.Status == BookingStatus.Pending);
        return View(bookings);
    }

    // ── Add-on rentals catalog (e.g. paddles) ────────────────────────────────────

    public async Task<IActionResult> AddOns()
    {
        ViewBag.AddOnList = await _db.AddOnItems
            .Where(a => a.OwnerId == CurrentUserId)
            .OrderBy(a => a.Name)
            .ToListAsync();

        ViewBag.RentalList = await _db.AddOnRentals
            .Where(r => r.OwnerId == CurrentUserId)
            .Include(r => r.User)
            .Include(r => r.Items).ThenInclude(i => i.AddOnItem)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync();

        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAddOn(string name, decimal price, AddOnPricingType pricingType = AddOnPricingType.PerUnit)
    {
        if (string.IsNullOrWhiteSpace(name) || price < 0)
        {
            TempData["Error"] = "Name is required and price can't be negative.";
            return RedirectToAction(nameof(AddOns));
        }

        _db.AddOnItems.Add(new AddOnItem { OwnerId = CurrentUserId, Name = name.Trim(), Price = price, PricingType = pricingType });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Add-on '{name}' created.";
        return RedirectToAction(nameof(AddOns));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAddOn(int id)
    {
        var item = await _db.AddOnItems.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == CurrentUserId);
        if (item is null) return NotFound();
        item.IsActive = !item.IsActive;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(AddOns));
    }

    // ── Walk-in booking (owner logging a court booking for a customer themselves) ──
    // Same idea as StaffController.NewWalkIn/WalkInForm/CreateWalkIn — for when the owner uses
    // their own Admin account as the front desk (e.g. booking a customer who calls in or walks
    // up). Cash is confirmed on the spot; any other method sits Pending until the owner confirms
    // it via the existing Bookings/ConfirmPayment page — there's no separate owner to notify
    // here since Admin *is* the owner. Reuses the Staff views directly (same pattern as
    // RentAddOns below) since they only reference relative asp-action links.

    public async Task<IActionResult> NewWalkIn(DateTime? date)
    {
        var myCourts = await MyCourts.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        ViewBag.Courts = myCourts;

        ViewBag.Settings = await GetMySettingsAsync();
        ViewBag.RateRanges = await _bookingService.GetRateRangesAsync(myCourts);
        var myCourtIds = myCourts.Select(c => c.Id).ToList();
        ViewBag.CourtRateTiers = await _db.CourtRateTiers
            .Where(t => myCourtIds.Contains(t.CourtId))
            .OrderBy(t => t.CourtId).ThenBy(t => t.StartHour)
            .ToListAsync();

        var todayPht = PhtClock.Today;
        var selectedDate = date.HasValue ? DateOnly.FromDateTime(date.Value) : todayPht;
        ViewBag.SelectedDate = selectedDate;

        var courtAvailability = new List<CourtAvailabilityViewModel>();
        foreach (var court in myCourts)
            courtAvailability.Add(await BuildStaffCourtAvailabilityAsync(court, selectedDate));

        return View("~/Views/Staff/NewWalkIn.cshtml", courtAvailability);
    }

    /// <summary>Mirrors StaffController.BuildStaffCourtAvailabilityAsync — Open Play blocks are
    /// surfaced here even when <c>AllowPublicSignup</c> is off, since the owner can always
    /// register a walk-in into an Open Play block regardless of online self-signup settings.</summary>
    private async Task<CourtAvailabilityViewModel> BuildStaffCourtAvailabilityAsync(Court court, DateOnly selectedDate)
    {
        var slots = await _db.CourtTimeSlots
            .Where(s => s.CourtId == court.Id && s.IsActive && s.SlotDate == selectedDate)
            .OrderBy(s => s.StartHour)
            .ToListAsync();

        var vm = new CourtAvailabilityViewModel { Court = court, Date = selectedDate };
        (vm.RateRangeMin, vm.RateRangeMax) = await _bookingService.GetRateRangeAsync(court);

        if (slots.Any())
        {
            vm.TimeSlots = slots;
            vm.UnavailableSlotIds = await _bookingService.GetUnavailableSlotIdsAsync(court.Id, selectedDate, slots);
            foreach (var s in slots)
            {
                vm.SlotPrices[s.Id] = await _bookingService.GetTotalPriceAsync(
                    court, selectedDate, new TimeOnly(s.StartHour % 24, 0), new TimeOnly(s.EndHour % 24, 0));
            }

            return vm;
        }

        var bookedHours  = await _bookingService.GetBookedHoursAsync(court.Id, selectedDate);
        var pendingHours = await _bookingService.GetPendingHoursAsync(court.Id, selectedDate);
        var pendingBundleWindows = await _bookingService.GetPendingBundleWindowsAsync(court.Id, selectedDate);
        var blockedHours = await _bookingService.GetBlockedHoursAsync(court.Id, selectedDate);
        var blockReasons = await _bookingService.GetBlockReasonsAsync(court.Id, selectedDate);
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
                    // Owner can always register a walk-in into an Open Play block, same as staff —
                    // regardless of whether online self-signup is enabled for customers.
                    var spotsRemaining = await _bookingService.GetOpenPlaySpotsRemainingForStaffAsync(block, court.Id, selectedDate);
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

        return vm;
    }

    public async Task<IActionResult> WalkInForm(int courtId, DateOnly date, int startHour, int? endHour)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
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

        ViewBag.AddOns = await _bookingService.GetActiveAddOnsAsync(CurrentUserId);
        ViewBag.PaymentMethods = await GetAvailablePaymentMethodsAsync(CurrentUserId);

        return View("~/Views/Staff/WalkInForm.cshtml");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWalkIn(
        int courtId, DateOnly date, int startHour, int durationHours, string customerName, string customerEmail, string customerPhone,
        string paymentMethod, string? paymentReference, IFormFile? paymentProof, string? notes, int? fixedEndHour)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
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
        var (addOns, addOnsTotal) = await _bookingService.ResolveSelectedAddOnsAsync(CurrentUserId, Request.Form, durationHours);

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

        bool isCash = IsCashPayment(paymentMethod);

        var booking = new Booking
        {
            CourtId       = courtId,
            UserId        = customer.Id,
            FacilityName  = court.FacilityName,
            CourtName     = court.Name,
            CustomerName  = customerName,
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
            LoggedByStaffId = CurrentUserId,
            CustomerNameSnapshot = customerName,
            AddOns        = addOns
        };
        await _bookingService.CreateBookingAsync(booking);

        var customerEmailToNotify = customerEmail.Trim();
        if (isCash && !string.IsNullOrWhiteSpace(customerEmailToNotify))
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            await _email.SendBookingConfirmedToCustomerAsync(
                customerEmailToNotify,
                customer.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                booking.Id, court.Name, booking.BookingDate, booking.StartTime, booking.EndTime,
                booking.TotalPrice, booking.PaymentMethod, booking.PaymentReference, baseUrl,
                isGuest: customer.IsGuest);
        }

        TempData["Success"] = isCash
            ? $"Booked {court.Name} for {customerName} ({TimeDisplay.HourRange(startHour, startHour + durationHours)}) — ₱{booking.TotalPrice:N0} via {paymentMethod} logged."
            : $"Logged {court.Name} for {customerName} ({TimeDisplay.HourRange(startHour, startHour + durationHours)}) — ₱{booking.TotalPrice:N0} via {paymentMethod}, pending confirmation.";

        return RedirectToAction(nameof(Index));
    }

    // ── Walk-in cart: log several slots (any of the owner's own courts) as one paid transaction ──
    // Mirrors StaffController.WalkInCartForm/CreateWalkInCart.

    private const int MaxWalkInCartItems = 20;

    public async Task<IActionResult> WalkInCartForm()
    {
        ViewBag.AddOns = await _bookingService.GetActiveAddOnsAsync(CurrentUserId);
        ViewBag.PaymentMethods = await GetAvailablePaymentMethodsAsync(CurrentUserId);
        return View("~/Views/Staff/WalkInCartForm.cshtml");
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

        var myCourts = await MyCourts.ToListAsync();
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

        var resolved = new List<(CartController.CartItemRequest Item, Court Court, TimeOnly Start, TimeOnly End, decimal SlotPrice, CourtBundle? Bundle)>();
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

            if (item.CourtBundleId.HasValue)
            {
                var bundleMatch = await ResolveWalkInBundleBlockAsync(court, item.CourtBundleId.Value, item.Date, item.StartHour, item.EndHour);
                if (bundleMatch is null)
                {
                    errors.Add($"{court.Name} on {item.Date:MMM d} — that bundle window is no longer available.");
                    continue;
                }
                var bundle = await _db.CourtBundles.FirstOrDefaultAsync(b => b.Id == item.CourtBundleId.Value && b.IsActive);
                if (bundle is null)
                {
                    errors.Add($"{court.Name} on {item.Date:MMM d} — that bundle is no longer available.");
                    continue;
                }
                resolved.Add((item, court, start, end, bundleMatch.FlatPrice, bundle));
                continue;
            }

            if (await _bookingService.HasBundleOnlyHoursAsync(court, item.Date, start, end))
            {
                errors.Add($"{court.Name} on {item.Date:MMM d} at {TimeDisplay.Hour(item.StartHour)} is only available as part of a bundle.");
                continue;
            }

            var price = await _bookingService.GetTotalPriceAsync(court, item.Date, start, end);
            resolved.Add((item, court, start, end, price, null));
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

        bool isCash = IsCashPayment(paymentMethod);
        var groupId = Guid.NewGuid();
        var bookings = new List<Booking>();

        foreach (var (item, court, start, end, slotPrice, bundle) in resolved)
        {
            var (addOns, addOnsTotal) = await _bookingService.ResolveAddOnsAsync(
                CurrentUserId,
                (item.AddOns ?? new List<CartController.CartAddOnRequest>())
                    .Select(a => new BookingService.AddOnSelection(a.AddOnItemId, a.Quantity, a.Hours)),
                item.EndHour - item.StartHour);

            bookings.Add(new Booking
            {
                CourtId              = court.Id,
                UserId               = customer.Id,
                FacilityName         = court.FacilityName,
                CourtName            = court.Name,
                CustomerName         = customerName,
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
                LoggedByStaffId      = CurrentUserId,
                BundleGroupId        = groupId,
                CourtBundleId        = bundle?.Id,
                CustomerNameSnapshot = customerName,
                AddOns               = addOns
            });
        }

        _db.Bookings.AddRange(bookings);
        await _db.SaveChangesAsync();

        var customerEmailToNotify = customerEmail.Trim();
        if (isCash && !string.IsNullOrWhiteSpace(customerEmailToNotify))
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

        var grandTotal = bookings.Sum(b => b.TotalPrice);
        TempData["Success"] = isCash
            ? $"Logged {bookings.Count} slot{(bookings.Count == 1 ? "" : "s")} for {customerName} — ₱{grandTotal:N0} via {paymentMethod}."
            : $"Logged {bookings.Count} slot{(bookings.Count == 1 ? "" : "s")} for {customerName} — ₱{grandTotal:N0} via {paymentMethod}, pending confirmation.";
        TempData["ClearCart"] = true;
        return RedirectToAction(nameof(Index));
    }

    // ── Walk-in bundle booking ────────────────────────────────────────────────
    // Mirrors StaffController.WalkInBundleForm/CreateWalkInBundle.

    public async Task<IActionResult> WalkInBundleForm(int bundleId, int courtId, DateOnly date, int startHour, int endHour)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
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
        ViewBag.PaymentMethods = await GetAvailablePaymentMethodsAsync(CurrentUserId);

        return View("~/Views/Staff/WalkInBundleForm.cshtml");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWalkInBundle(
        int bundleId, int courtId, DateOnly date, int startHour, int endHour,
        string customerName, string customerEmail, string customerPhone,
        string paymentMethod, string? paymentReference, IFormFile? paymentProof, string? notes)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
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
            CourtName            = court.Name,
            CustomerName         = customerName,
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
            LoggedByStaffId      = CurrentUserId,
            CustomerNameSnapshot = customerName,
            CourtBundleId        = bundle.Id,
            BundleGroupId        = Guid.NewGuid()
        };
        await _bookingService.CreateBookingAsync(booking);

        var customerEmailToNotify = customerEmail.Trim();
        if (isCash && !string.IsNullOrWhiteSpace(customerEmailToNotify))
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            await _email.SendBookingConfirmedToCustomerAsync(
                customerEmailToNotify,
                customer.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                booking.Id, court.Name, booking.BookingDate, booking.StartTime, booking.EndTime,
                booking.TotalPrice, booking.PaymentMethod, booking.PaymentReference, baseUrl,
                isGuest: customer.IsGuest);
        }

        TempData["Success"] = isCash
            ? $"Booked {court.Name} ({bundle.Name}) for {customerName} ({TimeDisplay.HourRange(startHour, endHour)}) — ₱{booking.TotalPrice:N0} via {paymentMethod} logged."
            : $"Logged {court.Name} ({bundle.Name}) for {customerName} ({TimeDisplay.HourRange(startHour, endHour)}) — ₱{booking.TotalPrice:N0} via {paymentMethod}, pending confirmation.";

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
    // Mirrors StaffController.OpenPlayForm/CreateOpenPlaySignup.

    public async Task<IActionResult> OpenPlayForm(int courtId, DateOnly date, int startHour, int endHour)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        var block = await _bookingService.ResolveScheduleBlockForHourAsync(court, date, startHour);
        if (block is null || block.StartHour != startHour || block.EndHour != endHour)
            return NotFound();

        var spotsRemaining = await _bookingService.GetOpenPlaySpotsRemainingForStaffAsync(block, courtId, date);
        ViewBag.Court          = court;
        ViewBag.Block          = block;
        ViewBag.Date           = date;
        ViewBag.SpotsRemaining = spotsRemaining; // null = unlimited (no MaxPlayers cap configured)
        ViewBag.PaymentMethods = await GetAvailablePaymentMethodsAsync(CurrentUserId);
        return View("~/Views/Staff/OpenPlayForm.cshtml");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOpenPlaySignup(
        int courtId, DateOnly date, int startHour, int endHour, int spotCount, string customerName, string customerEmail, string customerPhone,
        string paymentMethod, string? paymentReference, IFormFile? paymentProof, string? playerNames, string? notes)
    {
        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
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
            CourtName            = court.Name,
            CustomerName         = customerName,
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
            LoggedByStaffId      = CurrentUserId,
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

    // ── Rent add-ons directly (owner logging a counter sale themselves) ─────────
    // Same idea as StaffController.RentAddOns/CreateAddOnRental, just for the owner acting as
    // their own front desk. Since Admin *is* the facility owner, non-cash sales are still logged
    // Pending (for the Cash/Sales Log paper trail) but there's no separate owner to notify.

    public async Task<IActionResult> RentAddOns()
    {
        ViewBag.AddOns = await _bookingService.GetActiveAddOnsAsync(CurrentUserId);
        ViewBag.PaymentMethods = await GetAvailablePaymentMethodsAsync(CurrentUserId);
        return View("~/Views/Staff/RentAddOns.cshtml");
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

        var (items, total) = await _bookingService.ResolveSelectedAddOnRentalItemsAsync(CurrentUserId, Request.Form);
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

        bool isCash = IsCashPayment(paymentMethod);

        var rental = new AddOnRental
        {
            OwnerId              = CurrentUserId,
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
            LoggedByStaffId      = CurrentUserId,
            Items                = items
        };
        _db.AddOnRentals.Add(rental);
        await _db.SaveChangesAsync();

        TempData["Success"] = isCash
            ? $"Logged add-on rental for {customerName} — ₱{total:N0} via {paymentMethod}."
            : $"Logged add-on rental for {customerName} — ₱{total:N0} via {paymentMethod}, pending confirmation.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<string>> GetAvailablePaymentMethodsAsync(string ownerId)
    {
        var settings = await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == ownerId);
        var methods = new List<string> { "Cash" };
        if (!string.IsNullOrWhiteSpace(settings?.GCashNumber)) methods.Add("GCash");
        if (!string.IsNullOrWhiteSpace(settings?.MayaNumber)) methods.Add("Maya");
        if (!string.IsNullOrWhiteSpace(settings?.GoTymeNumber)) methods.Add("GoTyme");
        return methods;
    }

    private static bool IsCashPayment(string? paymentMethod) =>
        string.IsNullOrWhiteSpace(paymentMethod) || string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase);

    /// <summary>Saves an optional payment-proof screenshot (JPG/PNG/WebP) — mirrors
    /// <c>StaffController.SavePaymentProofAsync</c> for the owner's own add-on rental flow.</summary>
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
    public async Task<IActionResult> ConfirmAddOnRentalPayment(int id)
    {
        var rental = await _db.AddOnRentals
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == CurrentUserId);
        if (rental is null) return NotFound();

        rental.Status        = BookingStatus.Confirmed;
        rental.PaymentStatus = PaymentStatus.Paid;
        rental.PaidAt         = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Add-on rental #{id} confirmed.";
        return RedirectToAction(nameof(AddOns));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectAddOnRentalPayment(int id)
    {
        var rental = await _db.AddOnRentals.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == CurrentUserId);
        if (rental is null) return NotFound();

        rental.Status           = BookingStatus.Cancelled;
        rental.PaymentReference = null;
        rental.PaymentProofPath = null;
        await _db.SaveChangesAsync();

        TempData["Error"] = $"Add-on rental #{id} rejected and cancelled.";
        return RedirectToAction(nameof(AddOns));
    }

    // ── CSV export ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> ExportBookings(string? status, DateOnly? dateFrom, DateOnly? dateTo)
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
        if (dateFrom.HasValue) query = query.Where(b => b.BookingDate >= dateFrom.Value);
        if (dateTo.HasValue)   query = query.Where(b => b.BookingDate <= dateTo.Value);

        var bookings = await query.OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime).ToListAsync();

        var staffIds = bookings.Where(b => b.LoggedByStaffId != null).Select(b => b.LoggedByStaffId!).Distinct().ToList();
        var staffNames = await _db.Users.Where(u => staffIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        static string Csv(string? field)
        {
            field ??= "";
            return field.Contains(',') || field.Contains('"') || field.Contains('\n')
                ? "\"" + field.Replace("\"", "\"\"") + "\""
                : field;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Booking ID,Date,Start,End,Court,Customer,Phone,Court Rental,Add-ons,Add-ons Total,Total Paid,Payment Method,Payment Reference,Payment Status,Status,Booked By,Booked On");
        foreach (var b in bookings)
        {
            var addOnsSummary = string.Join("; ", b.AddOns.Select(a => $"{a.Quantity}x {a.AddOnItem.Name}"));
            var addOnsTotal = b.AddOns.Sum(a => a.Quantity * a.UnitPrice);
            var courtRental = b.TotalPrice - addOnsTotal;
            var bookedBy = b.LoggedByStaffId != null && staffNames.TryGetValue(b.LoggedByStaffId, out var n) ? n : "";

            sb.AppendLine(string.Join(",", new[]
            {
                Csv(b.Id.ToString()),
                Csv(b.BookingDate.ToString("yyyy-MM-dd")),
                Csv(b.StartTime.ToString("HH:mm")),
                Csv(b.EndTime.ToString("HH:mm")),
                Csv(b.Court.Name),
                Csv(b.CustomerNameSnapshot ?? b.User.FullName),
                Csv(b.User.PhoneNumber),
                Csv(courtRental.ToString("F2")),
                Csv(addOnsSummary),
                Csv(addOnsTotal.ToString("F2")),
                Csv(b.TotalPrice.ToString("F2")),
                Csv(b.PaymentMethod),
                Csv(b.PaymentReference),
                Csv(b.PaymentStatus.ToString()),
                Csv(b.Status.ToString()),
                Csv(bookedBy),
                Csv(b.CreatedAt.AddHours(8).ToString("yyyy-MM-dd HH:mm"))
            }));
        }

        var fileName = $"bookings-{DateTime.Now:yyyyMMdd}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }
}
