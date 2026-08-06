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

[Authorize]
public class BookingsController : Controller
{
    private readonly ApplicationDbContext         _db;
    private readonly BookingService               _bookingService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PayMongoService              _payMongo;
    private readonly IConfiguration              _config;
    private readonly EmailService                _email;
    private readonly GuestCheckoutService         _guestCheckout;
    private readonly ILogger<BookingsController> _logger;
    private readonly ImageCompressionService      _imageCompression;

    public BookingsController(
        ApplicationDbContext db,
        BookingService bookingService,
        UserManager<ApplicationUser> userManager,
        PayMongoService payMongo,
        IConfiguration config,
        EmailService email,
        GuestCheckoutService guestCheckout,
        ILogger<BookingsController> logger,
        ImageCompressionService imageCompression)
    {
        _db             = db;
        _bookingService = bookingService;
        _userManager    = userManager;
        _payMongo       = payMongo;
        _config         = config;
        _email          = email;
        _guestCheckout  = guestCheckout;
        _logger         = logger;
        _imageCompression = imageCompression;
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

        ViewBag.OpenPlaySignups = await _db.OpenPlaySignups
            .Include(s => s.Court)
            .Where(s => s.UserId == userId && s.Status != BookingStatus.Cancelled)
            .OrderByDescending(s => s.BookingDate).ThenByDescending(s => s.StartHour)
            .ToListAsync();

        return View(bookings);
    }

    [AllowAnonymous]
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
        ViewBag.AddOns = court.OwnerId != null ? await _bookingService.GetActiveAddOnsAsync(court.OwnerId) : new List<AddOnItem>();

        var vm = new BookingViewModel
        {
            CourtId = courtId,
            Court = court,
            BookingDate = date ?? PhtClock.Today,
            StartHour = startHour ?? court.OpeningHour,
            DurationHours = (endHour.HasValue && startHour.HasValue) ? endHour.Value - startHour.Value : 1,
            FixedEndHour = endHour
        };

        // Pre-fill contact info from the user's account if logged in
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                vm.GuestName  = user.FullName;
                vm.GuestEmail = user.Email;
                vm.GuestPhone = user.PhoneNumber;
            }
        }

        // Resolve the tier-aware total up front (for both a fixed owner-defined slot and the
        // regular hourly-grid default) so the confirmation page shows the same rate/price we'll
        // charge on POST — the court's flat PricePerHour alone can be wrong for a tiered hour.
        vm.ResolvedSlotTotal = await _bookingService.GetTotalPriceAsync(
            court, vm.BookingDate, vm.StartTime, vm.EndTime);

        return View(vm);
    }

    /// <summary>
    /// Tier-aware price preview for the hourly-grid booking form — re-fetched client-side whenever
    /// the customer changes the date/start-hour/duration, so the displayed total never falls back
    /// to the court's flat rate for an hour a <see cref="CourtRateTier"/> overrides.
    /// </summary>
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> GetSlotPrice(int courtId, DateOnly date, int startHour, int durationHours)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == courtId && c.IsActive);
        if (court is null) return NotFound();
        if (durationHours < 1) durationHours = 1;

        var start = new TimeOnly(startHour % 24, 0);
        var end   = new TimeOnly((startHour + durationHours) % 24, 0);
        var total = await _bookingService.GetTotalPriceAsync(court, date, start, end);
        return Json(new { total, perHour = Math.Round(total / durationHours, 2) });
    }

    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> Create(BookingViewModel vm)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == vm.CourtId && c.IsActive);
        if (court is null) return NotFound();
        vm.Court = court;

        bool isGuest = User.Identity?.IsAuthenticated != true;
        if (isGuest)
        {
            if (string.IsNullOrWhiteSpace(vm.GuestName)) ModelState.AddModelError("GuestName", "Name is required.");
            if (string.IsNullOrWhiteSpace(vm.GuestEmail)) ModelState.AddModelError("GuestEmail", "Email is required.");
            if (string.IsNullOrWhiteSpace(vm.GuestPhone)) ModelState.AddModelError("GuestPhone", "Phone number is required.");
        }

        // Past-date/time guard using Philippine Standard Time (UTC+8)
        var localNow  = PhtClock.Now;
        var todayPht  = DateOnly.FromDateTime(localNow);
        if (vm.BookingDate < todayPht)
            ModelState.AddModelError("BookingDate", "Cannot book a date in the past.");
        else if (vm.BookingDate == todayPht && (vm.StartHour * 60 + 20) < (localNow.Hour * 60 + localNow.Minute))
            ModelState.AddModelError("StartHour", "This time slot is too soon. Please book at least 20 minutes in advance.");

        if (vm.StartHour < court.OpeningHour || vm.StartHour >= court.ClosingHour)
            ModelState.AddModelError("StartHour", $"Start hour must be between {TimeDisplay.Hour(court.OpeningHour)} and {TimeDisplay.Hour(court.ClosingHour - 1)}.");

        if (vm.StartHour + vm.DurationHours > court.ClosingHour)
            ModelState.AddModelError("DurationHours", "Booking extends beyond closing time.");

        if (!ModelState.IsValid) return View(vm);

        var available = await _bookingService.IsSlotAvailableAsync(vm.CourtId, vm.BookingDate, vm.StartTime, vm.EndTime);
        if (!available)
        {
            ModelState.AddModelError("", "This time slot is no longer available. Please choose another time.");
            return View(vm);
        }

        // Grid-based bookings (not a pre-defined CourtTimeSlot window) must respect the
        // recurring weekly schedule: hours reserved for Admin-Hosted Open Play aren't
        // directly bookable, and price is the resolved tiered rate rather than the flat one.
        decimal totalPrice;
        if (!vm.IsSlotBooking)
        {
            if (await _bookingService.HasOpenPlayHoursAsync(court, vm.BookingDate, vm.StartTime, vm.EndTime))
            {
                ModelState.AddModelError("", "This time is reserved for Admin-Hosted Open Play and isn't available for direct booking.");
                return View(vm);
            }
            if (await _bookingService.HasBundleOnlyHoursAsync(court, vm.BookingDate, vm.StartTime, vm.EndTime))
            {
                ModelState.AddModelError("", "This time is only available as part of a bundled booking. Please use the bundle booking option instead.");
                return View(vm);
            }
            totalPrice = await _bookingService.GetTotalPriceAsync(court, vm.BookingDate, vm.StartTime, vm.EndTime);
        }
        else
        {
            // Owner-defined slot windows are exempt from the open-play / bundle-only guards,
            // but still respect the court's tiered rate rules — don't trust vm.TotalPrice here.
            totalPrice = await _bookingService.GetTotalPriceAsync(court, vm.BookingDate, vm.StartTime, vm.EndTime);
        }

        string userId;
        if (isGuest)
        {
            try
            {
                var guestUser = await _guestCheckout.GetOrCreateGuestUserAsync(vm.GuestName!, vm.GuestEmail!, vm.GuestPhone!);
                userId = guestUser.Id;
            }
            catch (GuestEmailConflictException ex)
            {
                ModelState.AddModelError("GuestEmail", ex.Message);
                return View(vm);
            }
        }
        else
        {
            userId = _userManager.GetUserId(User)!;
        }

        // Snapshot the facility name (court owner's facility) onto the booking so
        // it can be attributed to a facility directly in the database.
        var facilityName = court.OwnerId is { } courtOwnerId
            ? await _db.FacilitySettings
                .Where(s => s.OwnerId == courtOwnerId)
                .Select(s => s.FacilityName)
                .FirstOrDefaultAsync()
            : null;

        var (addOns, addOnsTotal) = court.OwnerId != null
            ? await _bookingService.ResolveSelectedAddOnsAsync(court.OwnerId, Request.Form, vm.DurationHours)
            : (new List<BookingAddOn>(), 0m);

        var booking = new Booking
        {
            CourtId = vm.CourtId,
            UserId = userId,
            FacilityName = facilityName,
            BookingDate = vm.BookingDate,
            StartTime = vm.StartTime,
            EndTime = vm.EndTime,
            TotalPrice = totalPrice + addOnsTotal,
            Notes = vm.Notes,
            Status = BookingStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            GuestAccessToken = isGuest ? Guid.NewGuid() : null,
            CustomerNameSnapshot = isGuest ? vm.GuestName : null,
            ReservedUntil = DateTime.UtcNow.AddMinutes(15),
            AddOns = addOns
        };

        await _bookingService.CreateBookingAsync(booking);

        // Reload with navigation properties for email
        var customer  = await _userManager.FindByIdAsync(userId);
        var fullCourt = await _db.Courts.FindAsync(booking.CourtId);
        var owner     = fullCourt?.OwnerId is { } ownerId ? await _userManager.FindByIdAsync(ownerId) : null;
        await SendNewBookingNotificationAsync(booking, fullCourt, customer, owner);

        if (isGuest)
        {
            await SendGuestAccessLinkEmailAsync(booking, fullCourt, customer);
            return RedirectToAction(nameof(GuestPay), new { token = booking.GuestAccessToken });
        }

        return RedirectToAction(nameof(Pay), new { id = booking.Id });
    }

    // Shows payment options: card (if facility has PayMongo key) + GCash/Maya
    public async Task<IActionResult> Pay(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.AddOns).ThenInclude(a => a.AddOnItem)
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
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId && b.PaymentStatus == PaymentStatus.Unpaid);

        if (booking is null) return NotFound();

        // Verify full name is present (required for payment records)
        var fullName = booking.CustomerNameSnapshot ?? booking.User?.FullName;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            TempData["Error"] = "Full name is required to submit payment. Please update your profile.";
            return RedirectToAction(nameof(Pay), new { id = bookingId });
        }

        // Check if the 15-minute reservation window has expired
        if (booking.ReservedUntil.HasValue && DateTime.UtcNow > booking.ReservedUntil.Value)
        {
            booking.Status = BookingStatus.Cancelled;
            await _db.SaveChangesAsync();
            TempData["Error"] = "Your reservation has expired (15-minute payment window elapsed). The slot has been released. Please select another time.";
            return RedirectToAction(nameof(Index));
        }

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

        var uploadsDir = Path.Combine(UploadsRoot, "uploads", "proofs");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"{bookingId}_{Guid.NewGuid():N}.jpg";
        var fullPath = Path.Combine(uploadsDir, fileName);
        byte[] compressed;
        try
        {
            await using var source = screenshot.OpenReadStream();
            compressed = await _imageCompression.CompressAsync(source);
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            TempData["Error"] = "That file doesn't look like a valid image. Please upload a JPG, PNG, or WebP screenshot.";
            return RedirectToAction(nameof(Pay), new { id = bookingId });
        }
        await System.IO.File.WriteAllBytesAsync(fullPath, compressed);
        screenshotPath = $"/uploads/proofs/{fileName}";

        booking.PaymentMethod = method;
        booking.PaymentReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        booking.PaymentProofPath = screenshotPath;
        booking.PaymentProofSubmittedAt = DateTime.UtcNow;

        // Keep the booking Pending until the facility owner reviews the proof and
        // confirms the payment. The slot is still reserved (availability excludes
        // any non-cancelled booking), so the customer won't lose it while waiting.
        // Confirmation — status flip, commission accrual, and the customer's
        // "Booking Confirmed" email — all happen in AdminController.ConfirmPayment.
        booking.Status        = BookingStatus.Pending;
        booking.PaymentStatus = PaymentStatus.Unpaid;

        // Clear the 15-minute hold now that proof is in — ReservationExpiryCleanupService
        // and ConfirmPayment's own expiry check only look at ReservedUntil, so leaving it
        // set meant a submitted-but-not-yet-reviewed booking could get auto-cancelled out
        // from under the customer before an admin ever saw it.
        booking.ReservedUntil = null;

        await _db.SaveChangesAsync();

        // Notify the facility owner that proof was submitted and needs confirming.
        if (booking.Court is null)
            booking.Court = await _db.Courts.FindAsync(booking.CourtId);
        var customer = await _userManager.FindByIdAsync(userId);
        var owner = booking.Court?.OwnerId is { } proofOwnerId
            ? await _userManager.FindByIdAsync(proofOwnerId) : null;
        await SendProofSubmittedNotificationAsync(booking, customer, owner);

        TempData["Success"] = "Payment submitted! Your slot is reserved while the facility reviews your payment. "
                            + "You'll get a confirmation email once it's approved.";
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

        if (booking.BookingDate <= PhtClock.Today)
        {
            TempData["Error"] = "Cannot cancel a past or same-day booking.";
            return RedirectToAction(nameof(My));
        }

        booking.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Booking cancelled successfully.";
        return RedirectToAction(nameof(My));
    }

    // ── Guest checkout (no account) ───────────────────────────────────────────
    // Mirrors Pay/SubmitProof/Cancel above exactly, but scoped by the unguessable
    // GuestAccessToken emailed to the guest instead of a logged-in session.

    [AllowAnonymous]
    public async Task<IActionResult> GuestPay(Guid token)
    {
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .Include(b => b.AddOns).ThenInclude(a => a.AddOnItem)
            .FirstOrDefaultAsync(b => b.GuestAccessToken == token);
        if (booking is null) return NotFound();

        var settings = (booking.Court?.OwnerId != null
            ? await _db.FacilitySettings.FirstOrDefaultAsync(s => s.OwnerId == booking.Court.OwnerId)
            : await _db.FacilitySettings.FirstOrDefaultAsync())
            ?? new FacilitySettings();

        ViewBag.Settings   = settings;
        ViewBag.HasCardPay = false; // PayMongo instant checkout isn't offered on the guest flow yet
        ViewBag.GuestToken = token;
        return View("Pay", booking);
    }

    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> GuestSubmitProof(Guid token, string method, string? reference, IFormFile? screenshot)
    {
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.GuestAccessToken == token && b.PaymentStatus == PaymentStatus.Unpaid);
        if (booking is null) return NotFound();

        // Verify full name is present (required for payment records)
        if (string.IsNullOrWhiteSpace(booking.CustomerNameSnapshot))
        {
            TempData["Error"] = "Full name is required to submit payment. Please provide your name when completing the booking.";
            return RedirectToAction(nameof(GuestPay), new { token });
        }

        // Check if the 15-minute reservation window has expired
        if (booking.ReservedUntil.HasValue && DateTime.UtcNow > booking.ReservedUntil.Value)
        {
            booking.Status = BookingStatus.Cancelled;
            await _db.SaveChangesAsync();
            TempData["Error"] = "Your reservation has expired (15-minute payment window elapsed). The slot has been released. Please book another time.";
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
        var fileName = $"{booking.Id}_{Guid.NewGuid():N}.jpg";
        var fullPath = Path.Combine(uploadsDir, fileName);
        byte[] compressed;
        try
        {
            await using var source = screenshot.OpenReadStream();
            compressed = await _imageCompression.CompressAsync(source);
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            TempData["Error"] = "That file doesn't look like a valid image. Please upload a JPG, PNG, or WebP screenshot.";
            return RedirectToAction(nameof(GuestPay), new { token });
        }
        await System.IO.File.WriteAllBytesAsync(fullPath, compressed);

        booking.PaymentMethod           = method;
        booking.PaymentReference        = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        booking.PaymentProofPath        = $"/uploads/proofs/{fileName}";
        booking.PaymentProofSubmittedAt = DateTime.UtcNow;
        booking.Status                  = BookingStatus.Pending;
        booking.PaymentStatus           = PaymentStatus.Unpaid;
        booking.ReservedUntil           = null;
        await _db.SaveChangesAsync();

        if (booking.Court is null)
            booking.Court = await _db.Courts.FindAsync(booking.CourtId);
        var customer = await _userManager.FindByIdAsync(booking.UserId);
        var owner = booking.Court?.OwnerId is { } proofOwnerId
            ? await _userManager.FindByIdAsync(proofOwnerId) : null;
        await SendProofSubmittedNotificationAsync(booking, customer, owner);

        TempData["Success"] = "Payment submitted! Your slot is reserved while the facility reviews your payment. "
                            + "You'll get a confirmation email once it's approved.";
        return RedirectToAction(nameof(GuestPay), new { token });
    }

    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> GuestCancel(Guid token)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.GuestAccessToken == token);
        if (booking is null) return NotFound();

        if (booking.BookingDate <= PhtClock.Today)
        {
            TempData["Error"] = "Cannot cancel a past or same-day booking.";
            return RedirectToAction(nameof(GuestPay), new { token });
        }

        booking.Status = BookingStatus.Cancelled;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Booking cancelled successfully.";
        return RedirectToAction(nameof(GuestPay), new { token });
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
            var bookedAt   = PhtClock.Now.ToString("MMM d, yyyy h:mm tt") + " PHT";
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
    /// Notifies the facility owner when a customer submits payment proof and asks
    /// them to review and confirm it. Awaited so DI-scoped services don't get
    /// disposed mid-send.
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
            var submittedAt = PhtClock.Now.ToString("MMM d, yyyy h:mm tt") + " PHT";
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
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>🔔 Payment Proof Submitted</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>A customer submitted payment proof for a booking. Please <strong style='color:#0d6efd;'>review and confirm</strong> it so the customer receives their confirmation:</p>
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
      <div style='background:#cfe2ff;border:1px solid #0d6efd;border-radius:6px;padding:12px 16px;margin:20px 0 0;font-size:13px;'>
        🔔 <strong>Action needed</strong> — the customer's slot is reserved and awaiting your confirmation. Review the proof in your dashboard, then <strong>Confirm</strong> the payment (the customer is emailed automatically) or <strong>Reject</strong> it if the payment was not received.
      </div>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{bookingsUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Review &amp; Confirm</a>
      </p>
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      Automated notification from CourtBook · Booking #{booking.Id}
    </div>
  </div>
</body></html>";

            var plain = $"Payment Proof Submitted — Booking #{booking.Id}\n\nCourt: {courtName}\nDate: {dateLabel}\nTime: {timeLabel}\nAmount: ₱{amount}\nCustomer: {customerName}\nMethod: {method}\nReference: {reference}\nSubmitted: {submittedAt}\n\nReview and confirm: {bookingsUrl}";

            await _email.SendAsync(owner.Email, $"🔔 Booking #{booking.Id} — Payment proof submitted, please confirm", html, plain);
            _logger?.LogInformation("[BookingsController] Sent proof-submitted notification for booking #{Id} to {Email}", booking.Id, owner.Email);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BookingsController] Failed to send proof notification for booking #{Id}", booking.Id);
        }
    }

    /// <summary>
    /// Sent once, right after a guest (no account) creates a booking — this link is their
    /// only way back to pay, check status, or cancel, since there's no login to fall back on.
    /// </summary>
    private async Task SendGuestAccessLinkEmailAsync(Booking booking, Court? court, ApplicationUser? guest)
    {
        try
        {
            if (guest is null || string.IsNullOrWhiteSpace(guest.Email) || !booking.GuestAccessToken.HasValue) return;

            var baseUrl   = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var payUrl    = $"{baseUrl}/Bookings/GuestPay?token={booking.GuestAccessToken}";
            var dateLabel = booking.BookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel = $"{booking.StartTime:hh\\:mm tt} – {booking.EndTime:hh\\:mm tt}";
            var courtName = court?.Name ?? "your court";
            var amount    = booking.TotalPrice.ToString("N0");

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>🎾 Your Booking — Complete Payment</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>Thanks for booking with CourtBook! No account needed — use the link below any time to pay, check status, or cancel this booking.</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Court</td> <td style='font-weight:600;padding:5px 0;'>{courtName}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Date</td>  <td style='font-weight:600;padding:5px 0;'>{dateLabel}</td></tr>
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

            var plain = $"Your CourtBook Booking\n\nCourt: {courtName}\nDate: {dateLabel}\nTime: {timeLabel}\nAmount: ₱{amount}\n\nManage your booking: {payUrl}\n\nKeep this email — it's the only way to access your booking without an account.";

            await _email.SendAsync(guest.Email, "🎾 Your CourtBook Booking — Complete Payment", html, plain);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BookingsController] Failed to send guest access link email for booking #{Id}", booking.Id);
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
            baseUrl,
            booking.User.IsGuest);
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
