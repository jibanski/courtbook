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
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace CourtBooking.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BookingService _bookingService;
    private readonly EmailService _email;
    private readonly IConfiguration _config;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(
        ApplicationDbContext db,
        BookingService bookingService,
        EmailService email,
        IConfiguration config,
        UserManager<ApplicationUser> userManager)
    {
        _db             = db;
        _bookingService = bookingService;
        _email          = email;
        _config         = config;
        _userManager    = userManager;
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
        var todayBookings   = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.BookingDate == PhtClock.Today && b.Status != BookingStatus.Cancelled)
                            + await _db.OpenPlaySignups.CountAsync(s => courtIds.Contains(s.CourtId) && s.BookingDate == PhtClock.Today && s.Status != BookingStatus.Cancelled);
        var totalRevenue    = await _db.Bookings.Where(b => courtIds.Contains(b.CourtId) && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed)).SumAsync(b => b.TotalPrice);
        var activeCourts    = await MyCourts.CountAsync(c => c.IsActive);
        var awaitingPayment = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.Status == BookingStatus.Pending && b.PaymentProofSubmittedAt != null);
        var awaitingSignups = await _db.OpenPlaySignups.CountAsync(s => courtIds.Contains(s.CourtId) && s.Status == BookingStatus.Pending && s.PaymentProofSubmittedAt != null);

        ViewBag.TotalBookings   = totalBookings;
        ViewBag.TodayBookings   = todayBookings;
        ViewBag.TotalRevenue    = totalRevenue;
        ViewBag.ActiveCourts    = activeCourts;
        ViewBag.AwaitingPayment = awaitingPayment;
        ViewBag.AwaitingSignups = awaitingSignups;
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
        // DateOnly.ToDateTime() produces Kind=Unspecified, which Npgsql rejects when comparing
        // against a "timestamp with time zone" column (PaidAt) — must be explicitly Utc.
        var rangeFromDt = rangeFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-8); // PHT midnight, as a UTC instant
        var todayDt     = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-8);    // PHT midnight today, as a UTC instant

        var liveBookings = _db.Bookings.Where(b => courtIds.Contains(b.CourtId));
        var liveSignups  = _db.OpenPlaySignups.Where(s => courtIds.Contains(s.CourtId));

        // Open Play sign-ups are a separate entity from regular/bundle bookings but count
        // toward the same revenue and conversion numbers, so project both to the same shape
        // and combine (UNION ALL, via Concat) before aggregating.
        var bookingRows = liveBookings.Select(b => new
        {
            b.BookingDate, b.TotalPrice, b.Status, b.PaymentStatus,
            b.PaidAt, b.PaymentProofSubmittedAt, b.PaymentReference, b.PaymentMethod
        });
        var signupRows = liveSignups.Select(s => new
        {
            s.BookingDate, s.TotalPrice, s.Status, s.PaymentStatus,
            s.PaidAt, s.PaymentProofSubmittedAt, s.PaymentReference, s.PaymentMethod
        });
        var combined = bookingRows.Concat(signupRows);

        var totalBookings   = await combined.CountAsync(x => x.Status != BookingStatus.Cancelled);
        var todayBookings   = await combined.CountAsync(x => x.BookingDate == today && x.Status != BookingStatus.Cancelled);
        var todayRevenue    = await combined
            .Where(x => x.PaidAt != null && x.PaidAt >= todayDt)
            .SumAsync(x => (decimal?)x.TotalPrice) ?? 0m;
        var totalRevenue    = await combined
            .Where(x => x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.Completed)
            .SumAsync(x => (decimal?)x.TotalPrice) ?? 0m;
        var awaitingPayment = await combined.CountAsync(x => x.Status == BookingStatus.Pending && x.PaymentProofSubmittedAt != null);
        var pendingNoProof  = await combined.CountAsync(x => x.Status == BookingStatus.Pending && x.PaymentReference == null);

        // Add-ons (e.g. paddle rentals) only ever attach to regular Bookings, not Open Play
        // sign-ups, so this is a separate query rather than folded into `combined` above.
        // Split out of totalRevenue so the owner can see rental vs. add-on sales separately.
        var addOnsRevenue = await _db.BookingAddOns
            .Where(a => courtIds.Contains(a.Booking.CourtId)
                     && (a.Booking.Status == BookingStatus.Confirmed || a.Booking.Status == BookingStatus.Completed))
            .SumAsync(a => (decimal?)(a.Quantity * a.UnitPrice)) ?? 0m;
        var courtRentalRevenue = totalRevenue - addOnsRevenue;

        var revenueRows = await combined
            .Where(x => x.BookingDate >= rangeFrom && x.BookingDate <= rangeTo
                        && (x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.Completed))
            .GroupBy(x => x.BookingDate)
            .Select(g => new { Day = g.Key, Revenue = g.Sum(x => x.TotalPrice), Count = g.Count() })
            .ToListAsync();

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

        // Payment mix — include legacy paid bookings that have no PaidAt by
        // falling back to BookingDate, matching the 'paidInRange' counter below.
        var rangeToExclusiveDt = rangeTo.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-8);
        var methodRows = await combined
            .Where(x => x.PaymentStatus == PaymentStatus.Paid
                        && ((x.PaidAt != null && x.PaidAt >= rangeFromDt && x.PaidAt < rangeToExclusiveDt)
                            || (x.PaidAt == null && x.BookingDate >= rangeFrom && x.BookingDate <= rangeTo)))
            .GroupBy(x => x.PaymentMethod ?? "Unknown")
            .Select(g => new { Method = g.Key, Count = g.Count(), Revenue = g.Sum(x => x.TotalPrice) })
            .ToListAsync();

        var bookingsInRange = await combined
            .CountAsync(x => x.BookingDate >= rangeFrom && x.BookingDate <= rangeTo && x.Status != BookingStatus.Cancelled);
        var paidInRange = await combined
            .CountAsync(x => x.BookingDate >= rangeFrom && x.BookingDate <= rangeTo && x.PaymentStatus == PaymentStatus.Paid);
        var conversion = bookingsInRange > 0 ? Math.Round(paidInRange * 100.0 / bookingsInRange, 1) : 0.0;

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
                courtRentalRevenue,
                addOnsRevenue
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

    public async Task<IActionResult> Bookings(string? status, DateOnly? date, bool? awaitingConfirmation, string? search)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var query = _db.Bookings
            .Where(b => courtIds.Contains(b.CourtId))
            .Include(b => b.Court).Include(b => b.User).Include(b => b.CourtBundle)
            .Include(b => b.AddOns).ThenInclude(a => a.AddOnItem)
            .AsQueryable();

        if (awaitingConfirmation == true)
            query = query.Where(b => b.Status == BookingStatus.Pending && b.PaymentProofSubmittedAt != null);
        else if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BookingStatus>(status, out var s))
            query = query.Where(b => b.Status == s);

        if (date.HasValue)
            query = query.Where(b => b.BookingDate == date.Value);

        var bookings = await query.OrderByDescending(b => b.PaymentProofSubmittedAt ?? b.CreatedAt).ToListAsync();

        // The "All Bookings" table (not the awaiting-confirmation card list, which has its
        // own dedicated flow on the Open Play Sign-ups page) also lists Open Play sign-ups
        // so the owner has one consolidated view of everything booked on their courts.
        if (awaitingConfirmation != true)
        {
            var signupQuery = _db.OpenPlaySignups
                .Where(sg => courtIds.Contains(sg.CourtId))
                .Include(sg => sg.Court).Include(sg => sg.User).AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BookingStatus>(status, out var signupStatus))
                signupQuery = signupQuery.Where(sg => sg.Status == signupStatus);
            if (date.HasValue)
                signupQuery = signupQuery.Where(sg => sg.BookingDate == date.Value);

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
                .OrderByDescending(r => r.BookingDate)
                .ThenByDescending(r => r.StartTime)
                .ToList();
        }

        ViewBag.SelectedStatus       = status;
        ViewBag.SelectedDate         = date;
        ViewBag.Search               = search;
        ViewBag.AwaitingConfirmation = awaitingConfirmation;
        ViewBag.PendingPaymentCount  = await _db.Bookings.CountAsync(b => courtIds.Contains(b.CourtId) && b.Status == BookingStatus.Pending && b.PaymentProofSubmittedAt != null);
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
        var openPlaySignupInfo = new Dictionary<int, (int MaxPlayers, int Taken)>();
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
                    openPlaySignupInfo[h] = (max, max - remaining);
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
        ViewBag.RateTiers      = (await _bookingService.GetRateTiersAsync(id)).OrderBy(t => t.StartHour).ToList();
        ViewBag.ScheduleBlocks = (await _bookingService.GetScheduleBlocksAsync(id)).OrderBy(b => b.StartHour).ToList();
        return View();
    }

    private static string NormalizeDays(string[]? days) =>
        string.Join(",", (days ?? Array.Empty<string>())
            .Select(d => d.Trim())
            .Where(d => d.Length > 0)
            .Distinct());

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
        bool allowPublicSignup = false, int? maxPlayers = null, decimal? pricePerHead = null)
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
        if (type != BookingType.AdminHostedOpenPlay) { allowPublicSignup = false; maxPlayers = null; pricePerHead = null; }
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
            PricePerHead      = pricePerHead
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
        if (block is null) return NotFound();

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
        bool allowPublicSignup = false, int? maxPlayers = null, decimal? pricePerHead = null)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var block = await _db.CourtScheduleBlocks.FirstOrDefaultAsync(b => b.Id == id && myCourtIds.Contains(b.CourtId));
        if (block is null) return NotFound();

        var court = await MyCourts.FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null) return NotFound();

        var daysCsv = NormalizeDays(days);
        if ((daysCsv.Length == 0 && !includeHolidays) || endHour <= startHour || startHour < 0 || endHour > 24)
        {
            TempData["Error"] = "Pick at least one day (or include holidays) and a valid hour range.";
            return RedirectToAction(nameof(Schedule), new { id = courtId });
        }

        if (type != BookingType.AdminHostedOpenPlay) { allowPublicSignup = false; maxPlayers = null; pricePerHead = null; }
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
        if (block is not null)
        {
            _db.CourtScheduleBlocks.Remove(block);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Schedule block removed.";
        }
        return RedirectToAction(nameof(Schedule), new { id = courtId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleScheduleBlock(int id, int courtId)
    {
        var myCourtIds = await GetMyCourtIdsAsync();
        var block = await _db.CourtScheduleBlocks.FirstOrDefaultAsync(b => b.Id == id && myCourtIds.Contains(b.CourtId));
        if (block is not null)
        {
            block.IsActive = !block.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = block.IsActive ? "Schedule block enabled." : "Schedule block paused.";
        }
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
        ViewBag.RateBlocks = (await _bookingService.GetBundleRateBlocksAsync(id)).OrderBy(b => b.StartHour).ToList();
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
    public async Task<IActionResult> EditBundleRateBlock(int id, int bundleId, string daysOfWeek, int startHour, int endHour, decimal flatPrice, bool includeHolidays)
    {
        var bundle = await _db.CourtBundles
            .FirstOrDefaultAsync(b => b.Id == bundleId && b.OwnerId == CurrentUserId);
        if (bundle is null) return NotFound();

        var block = await _db.CourtBundleRateBlocks
            .FirstOrDefaultAsync(b => b.Id == id && b.CourtBundleId == bundleId);
        if (block is null) return NotFound();

        if (string.IsNullOrWhiteSpace(daysOfWeek) || startHour >= endHour || flatPrice <= 0)
        {
            TempData["Error"] = "Select at least one day, set valid hours, and enter a price > 0.";
            return RedirectToAction(nameof(EditBundleRateBlock), new { id, bundleId });
        }

        block.DaysOfWeek = daysOfWeek.Trim();
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
            var baseUrl    = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var bundleName = first.CourtBundleId.HasValue
                ? (await _db.CourtBundles.FindAsync(first.CourtBundleId.Value))?.Name ?? "Bundle"
                : "Bundle";
            var courtNames = string.Join(", ", rows.Select(r => r.Court?.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
            var combinedTotal = rows.Sum(r => r.TotalPrice);
            await _email.SendBookingConfirmedToCustomerAsync(
                first.User.Email!,
                first.User.FirstName,
                first.Id,
                $"{bundleName} ({courtNames})",
                first.BookingDate,
                first.StartTime,
                first.EndTime,
                combinedTotal,
                first.PaymentMethod,
                first.PaymentReference,
                baseUrl,
                first.User.IsGuest);
        }

        TempData["Success"] = "Bundle booking confirmed — the customer has been emailed a confirmation.";
        return RedirectToAction(nameof(Bookings), new { awaitingConfirmation = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectBundlePayment(Guid groupId)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var rows = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .Where(b => b.BundleGroupId == groupId && courtIds.Contains(b.CourtId))
            .ToListAsync();
        if (rows.Count == 0) return NotFound();

        var first         = rows[0];
        var customerEmail = first.User?.Email;
        var customerName  = first.User?.FirstName;
        var courtName     = string.Join(", ", rows.Select(r => r.Court?.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        var bookingDate   = first.BookingDate;
        var startTime     = first.StartTime;
        var endTime       = first.EndTime;

        foreach (var booking in rows)
        {
            booking.Status           = BookingStatus.Cancelled;
            booking.PaymentReference = null;
            booking.PaymentProofPath = null;
        }
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(customerEmail))
            await SendPaymentRejectedEmailAsync(customerEmail!, customerName, first.Id,
                courtName, bookingDate, startTime, endTime);

        TempData["Error"] = "Bundle booking rejected and cancelled — the customer has been notified.";
        return RedirectToAction(nameof(Bookings), new { awaitingConfirmation = true });
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSignupStatus(int id, BookingStatus status)
    {
        var courtIds = await GetMyCourtIdsAsync();
        var signup   = await _db.OpenPlaySignups.FirstOrDefaultAsync(sg => sg.Id == id && courtIds.Contains(sg.CourtId));
        if (signup is null) return NotFound();
        signup.Status = status;
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
        var bookings = await _bookingService.GetCashLogAsync(courtIds, staffId, from, to);

        var staffIds = bookings.Select(b => b.LoggedByStaffId!).Distinct().ToList();
        var staffNames = await _db.Users
            .Where(u => staffIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        ViewBag.StaffNames = staffNames;
        ViewBag.StaffList  = await _db.Users.Where(u => u.EmployerOwnerId == CurrentUserId).ToListAsync();
        ViewBag.From       = from;
        ViewBag.To         = to;
        ViewBag.StaffId    = staffId;
        ViewBag.GrandTotal = bookings.Sum(b => b.TotalPrice);
        return View(bookings);
    }

    // ── Add-on rentals catalog (e.g. paddles) ────────────────────────────────────

    public async Task<IActionResult> AddOns()
    {
        ViewBag.AddOnList = await _db.AddOnItems
            .Where(a => a.OwnerId == CurrentUserId)
            .OrderBy(a => a.Name)
            .ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAddOn(string name, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name) || price < 0)
        {
            TempData["Error"] = "Name is required and price can't be negative.";
            return RedirectToAction(nameof(AddOns));
        }

        _db.AddOnItems.Add(new AddOnItem { OwnerId = CurrentUserId, Name = name.Trim(), Price = price });
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
