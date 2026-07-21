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
    private readonly ILogger<BookingsController> _logger;

    public BookingsController(
        ApplicationDbContext db,
        BookingService bookingService,
        UserManager<ApplicationUser> userManager,
        PayMongoService payMongo,
        IConfiguration config,
        EmailService email,
        ILogger<BookingsController> logger)
    {
        _db             = db;
        _bookingService = bookingService;
        _userManager    = userManager;
        _payMongo       = payMongo;
        _config         = config;
        _email          = email;
        _logger         = logger;
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

        // Snapshot the facility name (court owner's facility) onto the booking so
        // it can be attributed to a facility directly in the database.
        var facilityName = court.OwnerId is { } courtOwnerId
            ? await _db.FacilitySettings
                .Where(s => s.OwnerId == courtOwnerId)
                .Select(s => s.FacilityName)
                .FirstOrDefaultAsync()
            : null;

        var booking = new Booking
        {
            CourtId = vm.CourtId,
            UserId = userId,
            FacilityName = facilityName,
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
        var customer  = await _userManager.FindByIdAsync(userId);
        var fullCourt = await _db.Courts.FindAsync(booking.CourtId);
        var owner     = fullCourt?.OwnerId is { } ownerId ? await _userManager.FindByIdAsync(ownerId) : null;
        await SendNewBookingNotificationAsync(booking, fullCourt, customer, owner);

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

    // User submits their GCash/Maya screenshot (required) + optional reference number
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitProof(int bookingId, string method, string? reference, IFormFile? screenshot)
    {
        var userId = _userManager.GetUserId(User)!;
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId && b.PaymentStatus == PaymentStatus.Unpaid);

        if (booking is null) return NotFound();

        if (screenshot is null || screenshot.Length == 0)
        {
            TempData["Error"] = "Please upload a screenshot of your payment confirmation.";
            return RedirectToAction(nameof(Pay), new { id = bookingId });
        }

        string? screenshotPath = null;
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
        using (var stream = System.IO.File.Create(fullPath))
            await screenshot.CopyToAsync(stream);
        screenshotPath = $"/uploads/proofs/{fileName}";

        booking.PaymentMethod = method;
        booking.PaymentReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        booking.PaymentProofPath = screenshotPath;
        booking.PaymentProofSubmittedAt = DateTime.UtcNow;

        // Auto-confirm the booking once the customer submits proof so they
        // don't have to wait for the owner to click a button. The owner is
        // still notified with the proof and can cancel later if fraudulent.
        booking.Status        = BookingStatus.Confirmed;
        booking.PaymentStatus = PaymentStatus.Paid;
        booking.PaidAt        = DateTime.UtcNow;

        // Accrue platform commission if the facility is on the commission model.
        if (booking.Court is null)
            booking.Court = await _db.Courts.FindAsync(booking.CourtId);
        var ownerSettings = booking.Court?.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == booking.Court.OwnerId)
            : null;
        if (ownerSettings?.IsCommissionModel == true && booking.TotalPrice > 0)
        {
            var commission = Math.Round(booking.TotalPrice * ownerSettings.CommissionRate / 100m, 2);
            booking.CommissionAmount        = commission;
            ownerSettings.CommissionBalanceOwed += commission;
        }

        await _db.SaveChangesAsync();

        // Notify the facility owner that proof was submitted
        var customer = await _userManager.FindByIdAsync(userId);
        var owner = booking.Court?.OwnerId is { } proofOwnerId
            ? await _userManager.FindByIdAsync(proofOwnerId) : null;
        await SendProofSubmittedNotificationAsync(booking, customer, owner);

        // Send the customer their "✅ Payment Received" confirmation email
        if (booking.Court is not null && !string.IsNullOrWhiteSpace(customer?.Email))
        {
            var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            await _email.SendBookingConfirmedToCustomerAsync(
                customer.Email!,
                customer.FirstName,
                booking.Id,
                booking.Court.Name,
                booking.BookingDate,
                booking.StartTime,
                booking.EndTime,
                booking.TotalPrice,
                booking.PaymentMethod,
                booking.PaymentReference,
                baseUrl);
        }

        TempData["Success"] = "Payment submitted — your booking is confirmed! A receipt has been emailed to you.";
        return RedirectToAction(nameof(My));
    }

    // ── PayMongo instant payment (card / GCash / Maya / GrabPay / QRPh / bank) ─

    /// <summary>Creates a PayMongo checkout session using the facility's own secret key and redirects the customer.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    [ActionName("PayWithCard")] // back-compat: existing form posts use action=PayWithCard
    public Task<IActionResult> PayWithCardLegacy(int bookingId) => PayWithGateway(bookingId);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PayWithGateway(int bookingId)
    {
        var userId  = _userManager.GetUserId(User)!;
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId
                                      && b.PaymentStatus == PaymentStatus.Unpaid);

        if (booking is null) return NotFound();

        // Load the facility's PayMongo secret key + enabled methods
        var settings  = booking.Court?.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == booking.Court.OwnerId)
            : null;
        var secretKey = settings?.PayMongoSecretKey;

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            TempData["Error"] = "Instant payment is not available for this facility.";
            return RedirectToAction(nameof(Pay), new { id = bookingId });
        }

        var baseUrl    = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        var successUrl = $"{baseUrl}/Bookings/PaymentSuccess?session_id={{CHECKOUT_SESSION_ID}}&bookingId={booking.Id}";
        var cancelUrl  = $"{baseUrl}/Bookings/PaymentCancelled?bookingId={booking.Id}";

        try
        {
            var methods = settings!.EnabledPayMongoMethods;
            var (sessionId, checkoutUrl) = await _payMongo.CreateCheckoutSessionAsync(
                secretKey, booking, successUrl, cancelUrl, methods);
            booking.CheckoutSessionId = sessionId;
            await _db.SaveChangesAsync();
            return Redirect(checkoutUrl);
        }
        catch
        {
            TempData["Error"] = "Could not start instant payment. Please try again later.";
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
            .Include(b => b.User)
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
                var (status, methodUsed) = await _payMongo.GetSessionDetailsAsync(secretKey, sessionId);
                if (status == "paid" && booking.PaymentStatus == PaymentStatus.Unpaid)
                {
                    booking.PaymentStatus    = PaymentStatus.Paid;
                    booking.Status           = BookingStatus.Confirmed;
                    booking.PaymentMethod    = FormatMethodLabel(methodUsed);
                    booking.PaymentReference = sessionId;
                    booking.PaidAt           = DateTime.UtcNow;
                    await _db.SaveChangesAsync();

                    _ = Task.Run(() => SendCustomerConfirmationAsync(booking));
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

    /// <summary>Human-readable label for a PayMongo payment_method_used value.</summary>
    private static string FormatMethodLabel(string? method) => (method ?? "").ToLowerInvariant() switch
    {
        "card"     => "Card",
        "gcash"    => "GCash",
        "paymaya"  => "Maya",
        "grab_pay" => "GrabPay",
        "qrph"     => "QRPh",
        "dob"      => "Online Banking",
        "billease" => "BillEase",
        ""         => "Card",
        _          => char.ToUpperInvariant(method![0]) + method[1..]
    };

    /// <summary>
    /// Notifies the facility owner when a new booking is created.
    /// Awaited so DI-scoped services don't get disposed mid-send.
    /// </summary>
    private async Task SendNewBookingNotificationAsync(Booking booking, Court? court, ApplicationUser? customer, ApplicationUser? owner)
    {
        try
        {
            if (court is null)
            {
                _logger.LogWarning("[BookingsController] Skipped new-booking email for #{Id}: court is null", booking.Id);
                return;
            }
            if (string.IsNullOrWhiteSpace(court.OwnerId))
            {
                _logger.LogWarning("[BookingsController] Skipped new-booking email for #{Id}: court '{Name}' has no OwnerId", booking.Id, court.Name);
                return;
            }
            if (owner is null)
            {
                _logger.LogWarning("[BookingsController] Skipped new-booking email for #{Id}: owner user (OwnerId={OwnerId}) not found", booking.Id, court.OwnerId);
                return;
            }
            if (string.IsNullOrWhiteSpace(owner.Email))
            {
                _logger.LogWarning("[BookingsController] Skipped new-booking email for #{Id}: owner {OwnerId} has no email", booking.Id, owner.Id);
                return;
            }

            var baseUrl    = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
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
            _logger?.LogInformation("[BookingsController] Sent new-booking notification for booking #{Id} to {Email}", booking.Id, owner.Email);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BookingsController] Failed to send new booking notification for booking #{Id}", booking.Id);
        }
    }

    /// <summary>
    /// Notifies the facility owner when a customer submits payment proof.
    /// Awaited so DI-scoped services don't get disposed mid-send.
    /// </summary>
    private async Task SendProofSubmittedNotificationAsync(Booking booking, ApplicationUser? customer, ApplicationUser? owner)
    {
        try
        {
            if (booking.Court is null)
            {
                _logger.LogWarning("[BookingsController] Skipped proof email for #{Id}: court not loaded", booking.Id);
                return;
            }
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email))
            {
                _logger.LogWarning("[BookingsController] Skipped proof email for #{Id}: owner missing or has no email (OwnerId={OwnerId})", booking.Id, booking.Court.OwnerId);
                return;
            }

            var baseUrl     = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
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
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>✅ Booking Auto-Confirmed</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>A customer submitted payment proof and the booking has been <strong style='color:#198754;'>automatically confirmed</strong>:</p>
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
      <div style='background:#d1e7dd;border:1px solid #198754;border-radius:6px;padding:12px 16px;margin:20px 0 0;font-size:13px;'>
        ✅ <strong>No action needed</strong> — the customer's slot is reserved. Please review the proof in your dashboard and cancel the booking if the payment was not received.
      </div>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{bookingsUrl}' style='display:inline-block;background:#198754;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Review Booking</a>
      </p>
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      Automated notification from CourtBook · Booking #{booking.Id}
    </div>
  </div>
</body></html>";

            var plain = $"Payment Proof Submitted — Booking #{booking.Id}\n\nCourt: {courtName}\nDate: {dateLabel}\nTime: {timeLabel}\nAmount: ₱{amount}\nCustomer: {customerName}\nMethod: {method}\nReference: {reference}\nSubmitted: {submittedAt}\n\nReview and confirm: {bookingsUrl}";

            await _email.SendAsync(owner.Email, $"✅ Booking #{booking.Id} Auto-Confirmed — review proof", html, plain);
            _logger?.LogInformation("[BookingsController] Sent proof-submitted notification for booking #{Id} to {Email}", booking.Id, owner.Email);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BookingsController] Failed to send proof notification for booking #{Id}", booking.Id);
        }
    }

    private async Task SendCustomerConfirmationAsync(Booking booking)
    {
        if (booking.Court is null || booking.User?.Email is null) return;
        var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        await _email.SendBookingConfirmedToCustomerAsync(
            booking.User.Email,
            booking.User.FirstName,
            booking.Id,
            booking.Court.Name,
            booking.BookingDate,
            booking.StartTime,
            booking.EndTime,
            booking.TotalPrice,
            booking.PaymentMethod,
            booking.PaymentReference,
            baseUrl);
    }
}
