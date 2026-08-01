using CourtBooking.Data;
using CourtBooking.Helpers;
using CourtBooking.Models;
using CourtBooking.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Controllers;

/// <summary>
/// Customer-facing flow for booking a <see cref="CourtBundle"/> — a flat-price package
/// covering every member court at once during one of the bundle's recurring peak windows.
/// Mirrors <see cref="BookingsController"/>'s manual GCash/Maya payment flow, but every
/// action operates on the whole group of per-court <see cref="Booking"/> rows created
/// together (linked by <see cref="Booking.BundleGroupId"/>) instead of a single row.
/// PayMongo instant checkout is intentionally not supported for bundles yet.
/// </summary>
[Authorize]
public class BundleBookingsController : Controller
{
    private readonly ApplicationDbContext         _db;
    private readonly BookingService               _bookingService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration               _config;
    private readonly EmailService                 _email;
    private readonly GuestCheckoutService         _guestCheckout;
    private readonly ILogger<BundleBookingsController> _logger;

    public BundleBookingsController(
        ApplicationDbContext db,
        BookingService bookingService,
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        EmailService email,
        GuestCheckoutService guestCheckout,
        ILogger<BundleBookingsController> logger)
    {
        _db             = db;
        _bookingService = bookingService;
        _userManager    = userManager;
        _config         = config;
        _email          = email;
        _guestCheckout  = guestCheckout;
        _logger         = logger;
    }

    [AllowAnonymous]
    public async Task<IActionResult> Create(int bundleId, DateOnly date, int startHour, int endHour)
    {
        var bundle = await _db.CourtBundles
            .Include(b => b.Courts).ThenInclude(c => c.Court)
            .FirstOrDefaultAsync(b => b.Id == bundleId && b.IsActive);
        if (bundle is null) return NotFound();

        var block = await _db.CourtBundleRateBlocks.FirstOrDefaultAsync(b =>
            b.CourtBundleId == bundleId && b.IsActive && b.StartHour == startHour && b.EndHour == endHour);
        if (block is null) return NotFound();

        ViewBag.Bundle    = bundle;
        ViewBag.Block     = block;
        ViewBag.Date      = date;
        // EndHour can be 24 (representing midnight/12am for an overnight block, e.g. 8pm-12am) —
        // TimeOnly only accepts 0-23, so wrap with % 24 the same way TimeDisplay.Hour does.
        ViewBag.Available = await _bookingService.IsBundleWindowFullyAvailableAsync(
            bundle, date, new TimeOnly(startHour % 24, 0), new TimeOnly(endHour % 24, 0));
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> Create(
        int bundleId, DateOnly date, int startHour, int endHour, string? notes,
        string? guestName, string? guestEmail, string? guestPhone)
    {
        bool isGuest = User.Identity?.IsAuthenticated != true;
        if (isGuest && (string.IsNullOrWhiteSpace(guestName) || string.IsNullOrWhiteSpace(guestEmail) || string.IsNullOrWhiteSpace(guestPhone)))
        {
            TempData["Error"] = "Please enter your name, email, and phone number.";
            return RedirectToAction(nameof(Create), new { bundleId, date, startHour, endHour });
        }

        var bundle = await _db.CourtBundles
            .Include(b => b.Courts).ThenInclude(c => c.Court)
            .FirstOrDefaultAsync(b => b.Id == bundleId && b.IsActive);
        if (bundle is null) return NotFound();

        var block = await _db.CourtBundleRateBlocks.FirstOrDefaultAsync(b =>
            b.CourtBundleId == bundleId && b.IsActive && b.StartHour == startHour && b.EndHour == endHour);
        if (block is null)
        {
            TempData["Error"] = "This bundle window is no longer available.";
            return RedirectToAction(nameof(Create), new { bundleId, date, startHour, endHour });
        }

        var localNow = PhtClock.Now;
        var todayPht = DateOnly.FromDateTime(localNow);
        if (date < todayPht || (date == todayPht && (startHour * 60) <= (localNow.Hour * 60 + localNow.Minute + 20)))
        {
            TempData["Error"] = "This time slot is too soon. Please book at least 20 minutes in advance.";
            return RedirectToAction(nameof(Create), new { bundleId, date, startHour, endHour });
        }

        // EndHour can be 24 (midnight/12am for an overnight block, e.g. 8pm-12am) — TimeOnly only
        // accepts 0-23, so wrap with % 24 the same way TimeDisplay.Hour and the GET action above do.
        var start = new TimeOnly(startHour % 24, 0);
        var end   = new TimeOnly(endHour % 24, 0);

        var memberCourts = bundle.Courts.Select(c => c.Court).ToList();
        if (memberCourts.Any(c => startHour < c.OpeningHour || endHour > c.ClosingHour))
        {
            TempData["Error"] = "This window falls outside one of the bundle's courts' operating hours.";
            return RedirectToAction(nameof(Create), new { bundleId, date, startHour, endHour });
        }

        if (!await _bookingService.IsBundleWindowFullyAvailableAsync(bundle, date, start, end))
        {
            TempData["Error"] = "One or more courts in this bundle are no longer free for this window.";
            return RedirectToAction(nameof(Create), new { bundleId, date, startHour, endHour });
        }

        string userId;
        if (isGuest)
        {
            try
            {
                var guestUser = await _guestCheckout.GetOrCreateGuestUserAsync(guestName!, guestEmail!, guestPhone!);
                userId = guestUser.Id;
            }
            catch (GuestEmailConflictException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Create), new { bundleId, date, startHour, endHour });
            }
        }
        else
        {
            userId = _userManager.GetUserId(User)!;
        }

        var groupId      = Guid.NewGuid();
        var guestToken   = isGuest ? Guid.NewGuid() : (Guid?)null;
        var share        = Math.Round(block.FlatPrice / memberCourts.Count, 2);
        var facilityName = await _db.FacilitySettings
            .Where(s => s.OwnerId == bundle.OwnerId)
            .Select(s => s.FacilityName)
            .FirstOrDefaultAsync();

        var bookings = memberCourts.Select(c => new Booking
        {
            CourtId       = c.Id,
            FacilityName  = facilityName,
            UserId        = userId,
            BookingDate   = date,
            StartTime     = start,
            EndTime       = end,
            TotalPrice    = share,
            Notes         = notes,
            Status        = BookingStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            CourtBundleId = bundle.Id,
            BundleGroupId = groupId,
            GuestAccessToken = guestToken,
            ReservedUntil = DateTime.UtcNow.AddMinutes(15)
        }).ToList();

        _db.Bookings.AddRange(bookings);
        await _db.SaveChangesAsync();

        var customer = await _userManager.FindByIdAsync(userId);
        var owner    = await _userManager.FindByIdAsync(bundle.OwnerId);
        await SendNewBundleBookingNotificationAsync(bookings, bundle, memberCourts, customer, owner);

        if (isGuest)
        {
            await SendGuestAccessLinkEmailAsync(bookings, bundle, customer);
            return RedirectToAction(nameof(GuestPay), new { token = guestToken });
        }

        return RedirectToAction(nameof(Pay), new { groupId });
    }

    public async Task<IActionResult> Pay(Guid groupId)
    {
        var userId = _userManager.GetUserId(User)!;
        var rows = await _db.Bookings
            .Include(b => b.Court)
            .Where(b => b.BundleGroupId == groupId && b.UserId == userId)
            .ToListAsync();
        if (rows.Count == 0) return NotFound();

        var first    = rows[0];
        var settings = (first.Court?.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == first.Court.OwnerId)
            : null) ?? new FacilitySettings();

        ViewBag.Settings      = settings;
        ViewBag.CombinedTotal  = rows.Sum(r => r.TotalPrice);
        ViewBag.CourtNames     = string.Join(", ", rows.Select(r => r.Court?.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        ViewBag.GroupId        = groupId;
        var user = await _userManager.GetUserAsync(User);
        ViewBag.CustomerFullName = first.CustomerNameSnapshot ?? user?.FullName;
        return View(first);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitProof(Guid groupId, string method, string? reference, IFormFile? screenshot, string? fullName)
    {
        var userId = _userManager.GetUserId(User)!;
        var rows = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .Where(b => b.BundleGroupId == groupId && b.UserId == userId && b.PaymentStatus == PaymentStatus.Unpaid)
            .ToListAsync();
        if (rows.Count == 0) return NotFound();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            TempData["Error"] = "Full name is required.";
            return RedirectToAction(nameof(Pay), new { groupId });
        }
        foreach (var row in rows)
            row.CustomerNameSnapshot = fullName.Trim();

        // Check if any of the bookings in this bundle have expired
        var expiredRows = rows.Where(b => b.ReservedUntil.HasValue && DateTime.UtcNow > b.ReservedUntil.Value).ToList();
        if (expiredRows.Count > 0)
        {
            foreach (var row in expiredRows)
            {
                row.Status = BookingStatus.Cancelled;
            }
            await _db.SaveChangesAsync();
            TempData["Error"] = "One or more slots in this bundle have expired (15-minute payment window elapsed). The bundle has been released. Please book again.";
            return RedirectToAction(nameof(Index));
        }

        if (screenshot is null || screenshot.Length == 0)
        {
            TempData["Error"] = "Please upload a screenshot of your payment confirmation.";
            return RedirectToAction(nameof(Pay), new { groupId });
        }

        var ext = Path.GetExtension(screenshot.FileName).ToLower();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
        {
            TempData["Error"] = "Screenshot must be JPG, PNG, or WebP.";
            return RedirectToAction(nameof(Pay), new { groupId });
        }

        var uploadsDir = Path.Combine(UploadsRoot, "uploads", "proofs");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"bundle_{groupId:N}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsDir, fileName);
        using (var stream = System.IO.File.Create(fullPath))
            await screenshot.CopyToAsync(stream);
        var screenshotPath = $"/uploads/proofs/{fileName}";

        foreach (var booking in rows)
        {
            booking.PaymentMethod           = method;
            booking.PaymentReference        = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
            booking.PaymentProofPath        = screenshotPath;
            booking.PaymentProofSubmittedAt = DateTime.UtcNow;
            booking.Status                  = BookingStatus.Pending;
            booking.PaymentStatus           = PaymentStatus.Unpaid;
        }
        await _db.SaveChangesAsync();

        var first    = rows[0];
        var customer = await _userManager.FindByIdAsync(userId);
        var owner    = first.Court?.OwnerId is { } ownerId ? await _userManager.FindByIdAsync(ownerId) : null;
        await SendBundleProofSubmittedNotificationAsync(rows, customer, owner);

        TempData["Success"] = "Payment submitted! Your bundle is reserved while the facility reviews your payment. "
                            + "You'll get a confirmation email once it's approved.";
        return RedirectToAction("My", "Bookings");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid groupId)
    {
        var userId = _userManager.GetUserId(User)!;
        var rows = await _db.Bookings
            .Where(b => b.BundleGroupId == groupId && b.UserId == userId)
            .ToListAsync();
        if (rows.Count == 0) return NotFound();

        if (rows[0].BookingDate <= PhtClock.Today)
        {
            TempData["Error"] = "Cannot cancel a past or same-day booking.";
            return RedirectToAction("My", "Bookings");
        }

        foreach (var booking in rows)
            booking.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Bundle booking cancelled successfully.";
        return RedirectToAction("My", "Bookings");
    }

    // ── Guest checkout (no account) ───────────────────────────────────────────
    // Mirrors Pay/SubmitProof/Cancel above exactly, but scoped by the unguessable
    // GuestAccessToken emailed to the guest instead of a logged-in session.

    [AllowAnonymous]
    public async Task<IActionResult> GuestPay(Guid token)
    {
        var rows = await _db.Bookings
            .Include(b => b.Court)
            .Where(b => b.GuestAccessToken == token)
            .ToListAsync();
        if (rows.Count == 0) return NotFound();

        var first    = rows[0];
        var settings = (first.Court?.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == first.Court.OwnerId)
            : null) ?? new FacilitySettings();

        ViewBag.Settings      = settings;
        ViewBag.CombinedTotal = rows.Sum(r => r.TotalPrice);
        ViewBag.CourtNames    = string.Join(", ", rows.Select(r => r.Court?.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        ViewBag.GroupId       = first.BundleGroupId;
        ViewBag.GuestToken    = token;
        return View("Pay", first);
    }

    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> GuestSubmitProof(Guid token, string method, string? reference, IFormFile? screenshot, string? fullName)
    {
        var rows = await _db.Bookings
            .Include(b => b.Court)
            .Where(b => b.GuestAccessToken == token && b.PaymentStatus == PaymentStatus.Unpaid)
            .ToListAsync();
        if (rows.Count == 0) return NotFound();

        var firstBooking = rows.First();
        var nameToUse = !string.IsNullOrWhiteSpace(fullName) ? fullName.Trim() : firstBooking.CustomerNameSnapshot;
        if (string.IsNullOrWhiteSpace(nameToUse))
        {
            TempData["Error"] = "Full name is required.";
            return RedirectToAction(nameof(GuestPay), new { token });
        }
        foreach (var row in rows)
            row.CustomerNameSnapshot = nameToUse;

        // Check if any of the bookings in this bundle have expired
        var expiredRows = rows.Where(b => b.ReservedUntil.HasValue && DateTime.UtcNow > b.ReservedUntil.Value).ToList();
        if (expiredRows.Count > 0)
        {
            foreach (var row in expiredRows)
            {
                row.Status = BookingStatus.Cancelled;
            }
            await _db.SaveChangesAsync();
            TempData["Error"] = "One or more slots in this bundle have expired (15-minute payment window elapsed). The bundle has been released. Please book again.";
            return RedirectToAction(nameof(GuestPay), new { token });
        }

        if (screenshot is null || screenshot.Length == 0)
        {
            TempData["Error"] = "Please upload a screenshot of your payment confirmation.";
            return RedirectToAction(nameof(GuestPay), new { token });
        }

        var ext = Path.GetExtension(screenshot.FileName).ToLower();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
        {
            TempData["Error"] = "Screenshot must be JPG, PNG, or WebP.";
            return RedirectToAction(nameof(GuestPay), new { token });
        }

        var uploadsDir = Path.Combine(UploadsRoot, "uploads", "proofs");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"bundle_{token:N}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsDir, fileName);
        using (var stream = System.IO.File.Create(fullPath))
            await screenshot.CopyToAsync(stream);
        var screenshotPath = $"/uploads/proofs/{fileName}";

        foreach (var booking in rows)
        {
            booking.PaymentMethod           = method;
            booking.PaymentReference        = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
            booking.PaymentProofPath        = screenshotPath;
            booking.PaymentProofSubmittedAt = DateTime.UtcNow;
            booking.Status                  = BookingStatus.Pending;
            booking.PaymentStatus           = PaymentStatus.Unpaid;
        }
        await _db.SaveChangesAsync();

        var first    = rows[0];
        var customer = await _userManager.FindByIdAsync(first.UserId);
        var owner    = first.Court?.OwnerId is { } ownerId ? await _userManager.FindByIdAsync(ownerId) : null;
        await SendBundleProofSubmittedNotificationAsync(rows, customer, owner);

        TempData["Success"] = "Payment submitted! Your bundle is reserved while the facility reviews your payment. "
                            + "You'll get a confirmation email once it's approved.";
        return RedirectToAction(nameof(GuestPay), new { token });
    }

    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> GuestCancel(Guid token)
    {
        var rows = await _db.Bookings.Where(b => b.GuestAccessToken == token).ToListAsync();
        if (rows.Count == 0) return NotFound();

        if (rows[0].BookingDate <= PhtClock.Today)
        {
            TempData["Error"] = "Cannot cancel a past or same-day booking.";
            return RedirectToAction(nameof(GuestPay), new { token });
        }

        foreach (var booking in rows)
            booking.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Bundle booking cancelled successfully.";
        return RedirectToAction(nameof(GuestPay), new { token });
    }

    // ── Email notifications ───────────────────────────────────────────────────

    private async Task SendNewBundleBookingNotificationAsync(
        List<Booking> bookings, CourtBundle bundle, List<Court> memberCourts,
        ApplicationUser? customer, ApplicationUser? owner)
    {
        try
        {
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email))
            {
                _logger.LogWarning("[BundleBookingsController] Skipped new-bundle-booking email: owner missing or has no email (BundleId={BundleId})", bundle.Id);
                return;
            }

            var first         = bookings[0];
            var baseUrl       = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var bookingsUrl   = $"{baseUrl}/Admin/Bookings";
            var customerName  = customer?.FullName ?? "A customer";
            var customerEmail = customer?.Email ?? "—";
            var dateLabel     = first.BookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel     = $"{first.StartTime:hh\\:mm tt} – {first.EndTime:hh\\:mm tt}";
            var courtNames    = string.Join(", ", memberCourts.Select(c => c.Name));
            var amount        = bookings.Sum(b => b.TotalPrice).ToString("N0");

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>📦 New Bundle Booking Received</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>A customer just booked your <strong>{bundle.Name}</strong> bundle:</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Courts</td>    <td style='font-weight:600;padding:5px 0;'>{courtNames}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Date</td>       <td style='font-weight:600;padding:5px 0;'>{dateLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Time</td>       <td style='padding:5px 0;'>{timeLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Amount</td>     <td style='padding:5px 0;font-weight:600;color:#198754;'>₱{amount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Customer</td>   <td style='padding:5px 0;'>{customerName}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Email</td>      <td style='padding:5px 0;'><a href='mailto:{customerEmail}' style='color:#0d6efd;'>{customerEmail}</a></td></tr>
      </table>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{bookingsUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>View All Bookings</a>
      </p>
    </div>
  </div>
</body></html>";

            var plain = $"New Bundle Booking — {bundle.Name}\n\nCourts: {courtNames}\nDate: {dateLabel}\nTime: {timeLabel}\nAmount: ₱{amount}\nCustomer: {customerName} ({customerEmail})\n\nView bookings: {bookingsUrl}";
            await _email.SendAsync(owner.Email, $"📦 New Bundle Booking — {bundle.Name} on {dateLabel}", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BundleBookingsController] Failed to send new bundle booking notification");
        }
    }

    /// <summary>Sent once, right after a guest (no account) books a bundle — their only way
    /// back to pay, check status, or cancel, since there's no login to fall back on.</summary>
    private async Task SendGuestAccessLinkEmailAsync(List<Booking> bookings, CourtBundle bundle, ApplicationUser? guest)
    {
        try
        {
            var first = bookings[0];
            if (guest is null || string.IsNullOrWhiteSpace(guest.Email) || !first.GuestAccessToken.HasValue) return;

            var baseUrl   = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var payUrl    = $"{baseUrl}/BundleBookings/GuestPay?token={first.GuestAccessToken}";
            var dateLabel = first.BookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel = $"{first.StartTime:hh\\:mm tt} – {first.EndTime:hh\\:mm tt}";
            var amount    = bookings.Sum(b => b.TotalPrice).ToString("N0");

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>📦 Your Bundle Booking — Complete Payment</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>Thanks for booking the <strong>{bundle.Name}</strong> bundle! No account needed — use the link below any time to pay, check status, or cancel.</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Date</td>  <td style='font-weight:600;padding:5px 0;'>{dateLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Time</td>  <td style='padding:5px 0;'>{timeLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Amount</td><td style='padding:5px 0;font-weight:600;color:#198754;'>₱{amount}</td></tr>
      </table>
      <p style='margin:20px 0 0;text-align:center;'>
        <a href='{payUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Manage My Booking</a>
      </p>
      <p style='margin:16px 0 0;font-size:12px;color:#6c757d;'>Keep this email — it's the only way to access your booking without creating an account.</p>
    </div>
  </div>
</body></html>";

            var plain = $"Your Bundle Booking — {bundle.Name}\n\nDate: {dateLabel}\nTime: {timeLabel}\nAmount: ₱{amount}\n\nManage your booking: {payUrl}\n\nKeep this email — it's the only way to access your booking without an account.";
            await _email.SendAsync(guest.Email, "📦 Your Bundle Booking — Complete Payment", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BundleBookingsController] Failed to send guest access link email");
        }
    }

    private async Task SendBundleProofSubmittedNotificationAsync(List<Booking> rows, ApplicationUser? customer, ApplicationUser? owner)
    {
        try
        {
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email)) return;

            var first       = rows[0];
            var baseUrl     = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var bookingsUrl = $"{baseUrl}/Admin/Bookings";
            var courtNames  = string.Join(", ", rows.Select(r => r.Court?.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
            var dateLabel   = first.BookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel   = $"{first.StartTime:hh\\:mm tt} – {first.EndTime:hh\\:mm tt}";
            var amount      = rows.Sum(r => r.TotalPrice).ToString("N0");
            var method      = first.PaymentMethod ?? "—";
            var reference   = first.PaymentReference ?? "—";

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>🔔 Bundle Payment Proof Submitted</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>A customer submitted payment proof for a bundle booking. Please <strong style='color:#0d6efd;'>review and confirm</strong> it:</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Courts</td>  <td style='padding:5px 0;'>{courtNames}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Date</td>       <td style='padding:5px 0;'>{dateLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Time</td>       <td style='padding:5px 0;'>{timeLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Amount</td>     <td style='font-weight:600;color:#198754;padding:5px 0;'>₱{amount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Method</td>     <td style='padding:5px 0;'>{method}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Reference #</td><td style='font-family:monospace;padding:5px 0;'>{reference}</td></tr>
      </table>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{bookingsUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Review &amp; Confirm</a>
      </p>
    </div>
  </div>
</body></html>";

            var plain = $"Bundle Payment Proof Submitted\n\nCourts: {courtNames}\nDate: {dateLabel}\nTime: {timeLabel}\nAmount: ₱{amount}\nMethod: {method}\nReference: {reference}\n\nReview and confirm: {bookingsUrl}";
            await _email.SendAsync(owner.Email, "🔔 Bundle booking — Payment proof submitted, please confirm", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BundleBookingsController] Failed to send bundle proof notification");
        }
    }

    /// <summary>
    /// Root folder for file uploads. On Railway, UPLOADS_ROOT points to the mounted
    /// persistent volume (e.g. /data) — the container's own wwwroot is ephemeral and
    /// wiped on every redeploy. Falls back to wwwroot locally so behaviour is unchanged.
    /// </summary>
    private static string UploadsRoot =>
        Environment.GetEnvironmentVariable("UPLOADS_ROOT")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
}
