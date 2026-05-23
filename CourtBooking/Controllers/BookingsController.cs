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
    private readonly ApplicationDbContext         _db;
    private readonly BookingService               _bookingService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PayMongoService              _payMongo;
    private readonly IConfiguration              _config;
    private readonly EmailService                _email;

    public BookingsController(
        ApplicationDbContext db,
        BookingService bookingService,
        UserManager<ApplicationUser> userManager,
        PayMongoService payMongo,
        IConfiguration config,
        EmailService email)
    {
        _db             = db;
        _bookingService = bookingService;
        _userManager    = userManager;
        _payMongo       = payMongo;
        _config         = config;
        _email          = email;
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

        // Past-date/time guard using Philippine Standard Time (UTC+8)
        var localNow  = DateTime.UtcNow.AddHours(8);
        var todayPht  = DateOnly.FromDateTime(localNow);
        if (vm.BookingDate < todayPht)
            ModelState.AddModelError("BookingDate", "Cannot book a date in the past.");
        else if (vm.BookingDate == todayPht && vm.StartHour <= localNow.Hour)
            ModelState.AddModelError("StartHour", "This time slot has already passed. Please choose a future slot.");

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

        // Reload with navigation properties for email
        var customer = await _userManager.FindByIdAsync(userId);
        var fullCourt = await _db.Courts.FindAsync(booking.CourtId);
        _ = Task.Run(() => SendNewBookingNotificationAsync(booking, fullCourt, customer));

        return RedirectToAction(nameof(Pay), new { id = booking.Id });
    }

    // Shows payment options: card (if facility has PayMongo key) + GCash/Maya
    public async Task<IActionResult> Pay(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (booking is null) return NotFound();

        var settings = (booking.Court?.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == booking.Court.OwnerId)
            : await _db.FacilitySettings.FirstOrDefaultAsync())
            ?? new FacilitySettings();

        ViewBag.Settings    = settings;
        ViewBag.HasCardPay  = settings.AcceptsCardPayment;
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

        // Notify the facility owner that proof was submitted
        var customer = await _userManager.FindByIdAsync(userId);
        _ = Task.Run(() => SendProofSubmittedNotificationAsync(booking, customer));

        TempData["Success"] = "Payment details submitted! Your booking will be confirmed once the admin verifies your payment.";
        return RedirectToAction(nameof(My));
    }

    // ── PayMongo card payment ─────────────────────────────────────────────────

    /// <summary>Creates a PayMongo checkout session using the facility's own secret key and redirects the customer.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PayWithCard(int bookingId)
    {
        var userId  = _userManager.GetUserId(User)!;
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId
                                      && b.PaymentStatus == PaymentStatus.Unpaid);

        if (booking is null) return NotFound();

        // Load the facility's PayMongo secret key
        var settings  = booking.Court?.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == booking.Court.OwnerId)
            : null;
        var secretKey = settings?.PayMongoSecretKey;

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            TempData["Error"] = "Card payment is not available for this facility.";
            return RedirectToAction(nameof(Pay), new { id = bookingId });
        }

        var baseUrl    = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        var successUrl = $"{baseUrl}/Bookings/PaymentSuccess?session_id={{CHECKOUT_SESSION_ID}}&bookingId={booking.Id}";
        var cancelUrl  = $"{baseUrl}/Bookings/PaymentCancelled?bookingId={booking.Id}";

        try
        {
            var (sessionId, checkoutUrl) = await _payMongo.CreateCheckoutSessionAsync(secretKey, booking, successUrl, cancelUrl);
            booking.CheckoutSessionId = sessionId;
            await _db.SaveChangesAsync();
            return Redirect(checkoutUrl);
        }
        catch
        {
            TempData["Error"] = "Could not start card payment. Please try again or use GCash/Maya.";
            return RedirectToAction(nameof(Pay), new { id = bookingId });
        }
    }

    /// <summary>
    /// PayMongo redirects here after payment. We verify the session status using the
    /// facility's own secret key before confirming the booking.
    /// </summary>
    public async Task<IActionResult> PaymentSuccess(string sessionId, int bookingId)
    {
        var userId  = _userManager.GetUserId(User)!;
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId);

        if (booking is null) return NotFound();

        if (!string.IsNullOrEmpty(sessionId))
        {
            var settings  = booking.Court?.OwnerId != null
                ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == booking.Court.OwnerId)
                : null;
            var secretKey = settings?.PayMongoSecretKey;

            if (!string.IsNullOrEmpty(secretKey))
            {
                var status = await _payMongo.GetSessionStatusAsync(secretKey, sessionId);
                if (status == "paid" && booking.PaymentStatus == PaymentStatus.Unpaid)
                {
                    booking.PaymentStatus    = PaymentStatus.Paid;
                    booking.Status           = BookingStatus.Confirmed;
                    booking.PaymentMethod    = "Card";
                    booking.PaymentReference = sessionId;
                    booking.PaidAt           = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }
        }

        return View(booking);
    }

    /// <summary>PayMongo redirects here when the customer cancels or closes the hosted page.</summary>
    public async Task<IActionResult> PaymentCancelled(int bookingId)
    {
        var userId  = _userManager.GetUserId(User)!;
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId);

        if (booking is null) return NotFound();
        return View(booking);
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

    // ── Email notifications ───────────────────────────────────────────────────

    /// <summary>
    /// Notifies the facility owner when a new booking is created.
    /// Runs fire-and-forget so it never delays the user's redirect.
    /// </summary>
    private async Task SendNewBookingNotificationAsync(Booking booking, Court? court, ApplicationUser? customer)
    {
        try
        {
            if (court?.OwnerId == null) return;
            var owner = await _userManager.FindByIdAsync(court.OwnerId);
            if (owner?.Email == null) return;

            var baseUrl    = _config["App:BaseUrl"]?.TrimEnd('/') ?? "https://courtbooksolutions.org";
            var bookingsUrl = $"{baseUrl}/Admin/Bookings";
            var bookedAt   = DateTime.UtcNow.AddHours(8).ToString("MMM d, yyyy h:mm tt") + " PHT";
            var customerName  = customer?.FullName ?? "A customer";
            var customerEmail = customer?.Email    ?? "—";
            var dateLabel  = booking.BookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel  = $"{booking.StartTime:hh\\:mm tt} – {booking.EndTime:hh\\:mm tt}";
            var courtName  = court.Name;
            var amount     = booking.TotalPrice.ToString("N0");

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>📅 New Booking Received</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>A customer just booked a court at your facility:</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Court</td>      <td style='font-weight:600;padding:5px 0;'>{courtName}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Date</td>       <td style='font-weight:600;padding:5px 0;'>{dateLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Time</td>       <td style='padding:5px 0;'>{timeLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Amount</td>     <td style='padding:5px 0;font-weight:600;color:#198754;'>₱{amount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Customer</td>   <td style='padding:5px 0;'>{customerName}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Email</td>      <td style='padding:5px 0;'><a href='mailto:{customerEmail}' style='color:#0d6efd;'>{customerEmail}</a></td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Booking #</td>  <td style='padding:5px 0;'>#{booking.Id}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Received</td>   <td style='padding:5px 0;'>{bookedAt}</td></tr>
      </table>
      <p style='margin:20px 0 0;font-size:13px;color:#6c757d;'>
        The customer will now submit their payment proof. You will receive another email when they do.
      </p>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{bookingsUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>View All Bookings</a>
      </p>
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      Automated notification from CourtBook · Booking #{booking.Id}
    </div>
  </div>
</body></html>";

            var plain = $"New Booking #{booking.Id}\n\nCourt: {courtName}\nDate: {dateLabel}\nTime: {timeLabel}\nAmount: ₱{amount}\nCustomer: {customerName} ({customerEmail})\nReceived: {bookedAt}\n\nView bookings: {bookingsUrl}";

            await _email.SendAsync(owner.Email, $"📅 New Booking — {courtName} on {dateLabel}", html, plain);
        }
        catch (Exception ex)
        {
            // Fire-and-forget — log only, never throw
            var logger = HttpContext?.RequestServices
                .GetService<ILogger<BookingsController>>();
            logger?.LogError(ex, "[BookingsController] Failed to send new booking notification for booking #{Id}", booking.Id);
        }
    }

    /// <summary>
    /// Notifies the facility owner when a customer submits payment proof.
    /// Runs fire-and-forget so it never delays the user's redirect.
    /// </summary>
    private async Task SendProofSubmittedNotificationAsync(Booking booking, ApplicationUser? customer)
    {
        try
        {
            if (booking.Court?.OwnerId == null)
            {
                // Reload court if not included
                var court2 = await _db.Courts.FindAsync(booking.CourtId);
                if (court2?.OwnerId == null) return;
                booking.Court = court2;
            }

            var owner = await _userManager.FindByIdAsync(booking.Court.OwnerId!);
            if (owner?.Email == null) return;

            var baseUrl     = _config["App:BaseUrl"]?.TrimEnd('/') ?? "https://courtbooksolutions.org";
            var bookingsUrl = $"{baseUrl}/Admin/Bookings";
            var submittedAt = DateTime.UtcNow.AddHours(8).ToString("MMM d, yyyy h:mm tt") + " PHT";
            var customerName  = customer?.FullName ?? "A customer";
            var dateLabel  = booking.BookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel  = $"{booking.StartTime:hh\\:mm tt} – {booking.EndTime:hh\\:mm tt}";
            var courtName  = booking.Court.Name;
            var amount     = booking.TotalPrice.ToString("N0");
            var method     = booking.PaymentMethod ?? "—";
            var reference  = booking.PaymentReference ?? "—";

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#198754;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>💳 Payment Proof Submitted</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>A customer has submitted payment proof and is waiting for confirmation:</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Booking #</td>  <td style='font-weight:600;padding:5px 0;'>#{booking.Id}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Court</td>      <td style='padding:5px 0;'>{courtName}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Date</td>       <td style='padding:5px 0;'>{dateLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Time</td>       <td style='padding:5px 0;'>{timeLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Amount</td>     <td style='font-weight:600;color:#198754;padding:5px 0;'>₱{amount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Customer</td>   <td style='padding:5px 0;'>{customerName}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Method</td>     <td style='padding:5px 0;'>{method}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Reference #</td><td style='font-family:monospace;padding:5px 0;'>{reference}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Submitted</td>  <td style='padding:5px 0;'>{submittedAt}</td></tr>
      </table>
      <div style='background:#fff3cd;border:1px solid #ffc107;border-radius:6px;padding:12px 16px;margin:20px 0 0;font-size:13px;'>
        ⚠️ <strong>Action required:</strong> Please verify the payment and confirm or reject this booking in your dashboard.
      </div>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{bookingsUrl}' style='display:inline-block;background:#198754;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Review &amp; Confirm Booking</a>
      </p>
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      Automated notification from CourtBook · Booking #{booking.Id}
    </div>
  </div>
</body></html>";

            var plain = $"Payment Proof Submitted — Booking #{booking.Id}\n\nCourt: {courtName}\nDate: {dateLabel}\nTime: {timeLabel}\nAmount: ₱{amount}\nCustomer: {customerName}\nMethod: {method}\nReference: {reference}\nSubmitted: {submittedAt}\n\nReview and confirm: {bookingsUrl}";

            await _email.SendAsync(owner.Email, $"💳 Payment Proof Submitted — Booking #{booking.Id} needs verification", html, plain);
        }
        catch (Exception ex)
        {
            var logger = HttpContext?.RequestServices
                .GetService<ILogger<BookingsController>>();
            logger?.LogError(ex, "[BookingsController] Failed to send proof notification for booking #{Id}", booking.Id);
        }
    }
}
