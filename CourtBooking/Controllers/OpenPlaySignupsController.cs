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
/// Customer-facing flow for reserving one or more spots in a court's Admin-Hosted Open
/// Play session. Mirrors <see cref="BundleBookingsController"/>'s manual GCash/Maya
/// payment flow, but each sign-up is a single <see cref="OpenPlaySignup"/> row (not a
/// group) since it doesn't reserve the court exclusively — many customers can join the
/// same session up to its configured capacity. PayMongo instant checkout is
/// intentionally not supported yet, matching the bundled-booking scope cut.
/// </summary>
[Authorize]
public class OpenPlaySignupsController : Controller
{
    private readonly ApplicationDbContext         _db;
    private readonly BookingService               _bookingService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration               _config;
    private readonly EmailService                 _email;
    private readonly GuestCheckoutService         _guestCheckout;
    private readonly ILogger<OpenPlaySignupsController> _logger;

    public OpenPlaySignupsController(
        ApplicationDbContext db,
        BookingService bookingService,
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        EmailService email,
        GuestCheckoutService guestCheckout,
        ILogger<OpenPlaySignupsController> logger)
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
    public async Task<IActionResult> Create(int courtId, DateOnly date, int startHour, int endHour)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == courtId && c.IsActive);
        if (court is null) return NotFound();

        var block = await _bookingService.ResolveScheduleBlockForHourAsync(court, date, startHour);
        if (block is null || !block.AllowPublicSignup || block.StartHour != startHour || block.EndHour != endHour)
            return NotFound();

        var spotsRemaining = await _bookingService.GetOpenPlaySpotsRemainingAsync(block, courtId, date);

        ViewBag.Court          = court;
        ViewBag.Block          = block;
        ViewBag.Date           = date;
        ViewBag.SpotsRemaining = spotsRemaining;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> Create(
        int courtId, DateOnly date, int startHour, int endHour, int spotCount, string? notes,
        string? guestName, string? guestEmail, string? guestPhone)
    {
        bool isGuest = User.Identity?.IsAuthenticated != true;
        if (isGuest && (string.IsNullOrWhiteSpace(guestName) || string.IsNullOrWhiteSpace(guestEmail) || string.IsNullOrWhiteSpace(guestPhone)))
        {
            TempData["Error"] = "Please enter your name, email, and phone number.";
            return RedirectToAction(nameof(Create), new { courtId, date, startHour, endHour });
        }

        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == courtId && c.IsActive);
        if (court is null) return NotFound();

        var block = await _bookingService.ResolveScheduleBlockForHourAsync(court, date, startHour);
        if (block is null || !block.AllowPublicSignup || block.StartHour != startHour || block.EndHour != endHour)
        {
            TempData["Error"] = "This Open Play session is no longer available.";
            return RedirectToAction(nameof(Create), new { courtId, date, startHour, endHour });
        }

        var localNow = DateTime.UtcNow.AddHours(8);
        var todayPht = DateOnly.FromDateTime(localNow);
        if (date < todayPht || (date == todayPht && startHour <= localNow.Hour))
        {
            TempData["Error"] = "This session has already passed. Please choose a future date.";
            return RedirectToAction(nameof(Create), new { courtId, date, startHour, endHour });
        }

        if (spotCount < 1)
        {
            TempData["Error"] = "Pick at least 1 spot.";
            return RedirectToAction(nameof(Create), new { courtId, date, startHour, endHour });
        }

        var spotsRemaining = await _bookingService.GetOpenPlaySpotsRemainingAsync(block, courtId, date);
        if (spotCount > spotsRemaining)
        {
            TempData["Error"] = $"Only {spotsRemaining} spot(s) left for this session.";
            return RedirectToAction(nameof(Create), new { courtId, date, startHour, endHour });
        }

        string userId;
        if (isGuest)
        {
            var guestUser = await _guestCheckout.GetOrCreateGuestUserAsync(guestName!, guestEmail!, guestPhone!);
            userId = guestUser.Id;
        }
        else
        {
            userId = _userManager.GetUserId(User)!;
        }

        var pricePerHead = block.PricePerHead ?? 0;
        var facilityName = court.OwnerId != null
            ? await _db.FacilitySettings.Where(s => s.OwnerId == court.OwnerId).Select(s => s.FacilityName).FirstOrDefaultAsync()
            : null;

        var signup = new OpenPlaySignup
        {
            CourtId              = courtId,
            FacilityName         = facilityName,
            UserId               = userId,
            BookingDate          = date,
            StartHour            = startHour,
            EndHour              = endHour,
            SpotCount            = spotCount,
            PricePerHeadSnapshot = pricePerHead,
            TotalPrice           = pricePerHead * spotCount,
            Notes                = notes,
            Status               = BookingStatus.Pending,
            PaymentStatus        = PaymentStatus.Unpaid,
            GuestAccessToken     = isGuest ? Guid.NewGuid() : null
        };
        _db.OpenPlaySignups.Add(signup);
        await _db.SaveChangesAsync();

        var customer = await _userManager.FindByIdAsync(userId);
        var owner    = court.OwnerId != null ? await _userManager.FindByIdAsync(court.OwnerId) : null;
        await SendNewSignupNotificationAsync(signup, court, customer, owner);

        if (isGuest)
        {
            await SendGuestAccessLinkEmailAsync(signup, court, customer);
            return RedirectToAction(nameof(GuestPay), new { token = signup.GuestAccessToken });
        }

        return RedirectToAction(nameof(Pay), new { id = signup.Id });
    }

    public async Task<IActionResult> Pay(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var signup = await _db.OpenPlaySignups
            .Include(s => s.Court)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (signup is null) return NotFound();

        var settings = (signup.Court?.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == signup.Court.OwnerId)
            : null) ?? new FacilitySettings();

        ViewBag.Settings = settings;
        return View(signup);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitProof(int id, string method, string? reference, IFormFile? screenshot)
    {
        var userId = _userManager.GetUserId(User)!;
        var signup = await _db.OpenPlaySignups
            .Include(s => s.Court)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && s.PaymentStatus == PaymentStatus.Unpaid);
        if (signup is null) return NotFound();

        if (screenshot is null || screenshot.Length == 0)
        {
            TempData["Error"] = "Please upload a screenshot of your payment confirmation.";
            return RedirectToAction(nameof(Pay), new { id });
        }

        var ext = Path.GetExtension(screenshot.FileName).ToLower();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
        {
            TempData["Error"] = "Screenshot must be JPG, PNG, or WebP.";
            return RedirectToAction(nameof(Pay), new { id });
        }

        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "proofs");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"openplay_{id}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsDir, fileName);
        using (var stream = System.IO.File.Create(fullPath))
            await screenshot.CopyToAsync(stream);

        signup.PaymentMethod           = method;
        signup.PaymentReference        = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        signup.PaymentProofPath        = $"/uploads/proofs/{fileName}";
        signup.PaymentProofSubmittedAt = DateTime.UtcNow;
        signup.Status                  = BookingStatus.Pending;
        signup.PaymentStatus           = PaymentStatus.Unpaid;
        await _db.SaveChangesAsync();

        var customer = await _userManager.FindByIdAsync(userId);
        var owner    = signup.Court?.OwnerId is { } ownerId ? await _userManager.FindByIdAsync(ownerId) : null;
        await SendSignupProofSubmittedNotificationAsync(signup, customer, owner);

        TempData["Success"] = "Payment submitted! Your spot is reserved while the facility reviews your payment. "
                            + "You'll get a confirmation email once it's approved.";
        return RedirectToAction("My", "Bookings");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var signup = await _db.OpenPlaySignups.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (signup is null) return NotFound();

        if (signup.BookingDate <= DateOnly.FromDateTime(DateTime.Today))
        {
            TempData["Error"] = "Cannot cancel a past or same-day sign-up.";
            return RedirectToAction("My", "Bookings");
        }

        signup.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Sign-up cancelled successfully.";
        return RedirectToAction("My", "Bookings");
    }

    // ── Guest checkout (no account) ───────────────────────────────────────────
    // Mirrors Pay/SubmitProof/Cancel above exactly, but scoped by the unguessable
    // GuestAccessToken emailed to the guest instead of a logged-in session.

    [AllowAnonymous]
    public async Task<IActionResult> GuestPay(Guid token)
    {
        var signup = await _db.OpenPlaySignups
            .Include(s => s.Court)
            .FirstOrDefaultAsync(s => s.GuestAccessToken == token);
        if (signup is null) return NotFound();

        var settings = (signup.Court?.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == signup.Court.OwnerId)
            : null) ?? new FacilitySettings();

        ViewBag.Settings   = settings;
        ViewBag.GuestToken = token;
        return View("Pay", signup);
    }

    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> GuestSubmitProof(Guid token, string method, string? reference, IFormFile? screenshot)
    {
        var signup = await _db.OpenPlaySignups
            .Include(s => s.Court)
            .FirstOrDefaultAsync(s => s.GuestAccessToken == token && s.PaymentStatus == PaymentStatus.Unpaid);
        if (signup is null) return NotFound();

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

        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "proofs");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"openplay_{signup.Id}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsDir, fileName);
        using (var stream = System.IO.File.Create(fullPath))
            await screenshot.CopyToAsync(stream);

        signup.PaymentMethod           = method;
        signup.PaymentReference        = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        signup.PaymentProofPath        = $"/uploads/proofs/{fileName}";
        signup.PaymentProofSubmittedAt = DateTime.UtcNow;
        signup.Status                  = BookingStatus.Pending;
        signup.PaymentStatus           = PaymentStatus.Unpaid;
        await _db.SaveChangesAsync();

        var customer = await _userManager.FindByIdAsync(signup.UserId);
        var owner    = signup.Court?.OwnerId is { } ownerId ? await _userManager.FindByIdAsync(ownerId) : null;
        await SendSignupProofSubmittedNotificationAsync(signup, customer, owner);

        TempData["Success"] = "Payment submitted! Your spot is reserved while the facility reviews your payment. "
                            + "You'll get a confirmation email once it's approved.";
        return RedirectToAction(nameof(GuestPay), new { token });
    }

    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> GuestCancel(Guid token)
    {
        var signup = await _db.OpenPlaySignups.FirstOrDefaultAsync(s => s.GuestAccessToken == token);
        if (signup is null) return NotFound();

        if (signup.BookingDate <= DateOnly.FromDateTime(DateTime.Today))
        {
            TempData["Error"] = "Cannot cancel a past or same-day sign-up.";
            return RedirectToAction(nameof(GuestPay), new { token });
        }

        signup.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Sign-up cancelled successfully.";
        return RedirectToAction(nameof(GuestPay), new { token });
    }

    // ── Email notifications ───────────────────────────────────────────────────

    private async Task SendNewSignupNotificationAsync(OpenPlaySignup signup, Court court, ApplicationUser? customer, ApplicationUser? owner)
    {
        try
        {
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email))
            {
                _logger.LogWarning("[OpenPlaySignupsController] Skipped new-signup email: owner missing or has no email (CourtId={CourtId})", court.Id);
                return;
            }

            var baseUrl       = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var bookingsUrl   = $"{baseUrl}/Admin/OpenPlaySignups";
            var customerName  = customer?.FullName ?? "A customer";
            var customerEmail = customer?.Email ?? "—";
            var dateLabel     = signup.BookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel     = TimeDisplay.HourRange(signup.StartHour, signup.EndHour);

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>🙋 New Open Play Sign-up</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>A customer just reserved a spot in your Open Play session:</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Court</td>    <td style='font-weight:600;padding:5px 0;'>{court.Name}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Date</td>       <td style='font-weight:600;padding:5px 0;'>{dateLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Time</td>       <td style='padding:5px 0;'>{timeLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Spots</td>      <td style='padding:5px 0;'>{signup.SpotCount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Amount</td>     <td style='padding:5px 0;font-weight:600;color:#198754;'>₱{signup.TotalPrice.ToString("N0")}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Customer</td>   <td style='padding:5px 0;'>{customerName}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Email</td>      <td style='padding:5px 0;'><a href='mailto:{customerEmail}' style='color:#0d6efd;'>{customerEmail}</a></td></tr>
      </table>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{bookingsUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>View Sign-ups</a>
      </p>
    </div>
  </div>
</body></html>";

            var plain = $"New Open Play Sign-up\n\nCourt: {court.Name}\nDate: {dateLabel}\nTime: {timeLabel}\nSpots: {signup.SpotCount}\nAmount: ₱{signup.TotalPrice:N0}\nCustomer: {customerName} ({customerEmail})\n\nView sign-ups: {bookingsUrl}";
            await _email.SendAsync(owner.Email, $"🙋 New Open Play Sign-up — {court.Name} on {dateLabel}", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenPlaySignupsController] Failed to send new signup notification");
        }
    }

    /// <summary>Sent once, right after a guest (no account) signs up — their only way back
    /// to pay, check status, or cancel, since there's no login to fall back on.</summary>
    private async Task SendGuestAccessLinkEmailAsync(OpenPlaySignup signup, Court court, ApplicationUser? guest)
    {
        try
        {
            if (guest is null || string.IsNullOrWhiteSpace(guest.Email) || !signup.GuestAccessToken.HasValue) return;

            var baseUrl   = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var payUrl    = $"{baseUrl}/OpenPlaySignups/GuestPay?token={signup.GuestAccessToken}";
            var dateLabel = signup.BookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel = TimeDisplay.HourRange(signup.StartHour, signup.EndHour);

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>🙋 Your Open Play Sign-up — Complete Payment</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>Thanks for signing up for Open Play at {court.Name}! No account needed — use the link below any time to pay, check status, or cancel.</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Court</td> <td style='font-weight:600;padding:5px 0;'>{court.Name}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Date</td>  <td style='font-weight:600;padding:5px 0;'>{dateLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Time</td>  <td style='padding:5px 0;'>{timeLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Spots</td> <td style='padding:5px 0;'>{signup.SpotCount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Amount</td><td style='padding:5px 0;font-weight:600;color:#198754;'>₱{signup.TotalPrice.ToString("N0")}</td></tr>
      </table>
      <p style='margin:20px 0 0;text-align:center;'>
        <a href='{payUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Manage My Sign-up</a>
      </p>
      <p style='margin:16px 0 0;font-size:12px;color:#6c757d;'>Keep this email — it's the only way to access your sign-up without creating an account.</p>
    </div>
  </div>
</body></html>";

            var plain = $"Your Open Play Sign-up — {court.Name}\n\nDate: {dateLabel}\nTime: {timeLabel}\nSpots: {signup.SpotCount}\nAmount: ₱{signup.TotalPrice:N0}\n\nManage your sign-up: {payUrl}\n\nKeep this email — it's the only way to access your sign-up without an account.";
            await _email.SendAsync(guest.Email, "🙋 Your Open Play Sign-up — Complete Payment", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenPlaySignupsController] Failed to send guest access link email");
        }
    }

    private async Task SendSignupProofSubmittedNotificationAsync(OpenPlaySignup signup, ApplicationUser? customer, ApplicationUser? owner)
    {
        try
        {
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email)) return;

            var baseUrl     = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var bookingsUrl = $"{baseUrl}/Admin/OpenPlaySignups";
            var dateLabel   = signup.BookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel   = TimeDisplay.HourRange(signup.StartHour, signup.EndHour);
            var method      = signup.PaymentMethod ?? "—";
            var reference   = signup.PaymentReference ?? "—";

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>🔔 Open Play Payment Proof Submitted</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>A customer submitted payment proof for their Open Play sign-up. Please <strong style='color:#0d6efd;'>review and confirm</strong> it:</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Date</td>       <td style='padding:5px 0;'>{dateLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Time</td>       <td style='padding:5px 0;'>{timeLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Spots</td>      <td style='padding:5px 0;'>{signup.SpotCount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Amount</td>     <td style='font-weight:600;color:#198754;padding:5px 0;'>₱{signup.TotalPrice.ToString("N0")}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Method</td>     <td style='padding:5px 0;'>{method}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Reference #</td><td style='font-family:monospace;padding:5px 0;'>{reference}</td></tr>
      </table>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{bookingsUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Review &amp; Confirm</a>
      </p>
    </div>
  </div>
</body></html>";

            var plain = $"Open Play Payment Proof Submitted\n\nDate: {dateLabel}\nTime: {timeLabel}\nSpots: {signup.SpotCount}\nAmount: ₱{signup.TotalPrice:N0}\nMethod: {method}\nReference: {reference}\n\nReview and confirm: {bookingsUrl}";
            await _email.SendAsync(owner.Email, "🔔 Open Play sign-up — Payment proof submitted, please confirm", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenPlaySignupsController] Failed to send proof notification");
        }
    }
}
