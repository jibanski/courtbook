using System.Text.Json;
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
/// Lets a customer check out several arbitrary court+time selections (within one facility) in
/// one payment instead of running the single-booking flow once per slot. The cart itself lives
/// client-side (localStorage, see wwwroot/js/cart.js) — this controller only re-validates and
/// persists it at checkout time.
///
/// The created <see cref="Booking"/> rows share a fresh <see cref="Booking.BundleGroupId"/> with
/// <see cref="Booking.CourtBundleId"/> left null, so payment/proof/cancel all ride the existing
/// <see cref="BundleBookingsController"/> Pay/SubmitProof/Cancel flow unchanged — that flow only
/// ever grouped by BundleGroupId, never required a real CourtBundle.
/// </summary>
[AllowAnonymous]
public class CartController : Controller
{
    private const int MaxCartItems = 20;

    private readonly ApplicationDbContext         _db;
    private readonly BookingService                _bookingService;
    private readonly UserManager<ApplicationUser>  _userManager;
    private readonly IConfiguration                _config;
    private readonly EmailService                  _email;
    private readonly GuestCheckoutService          _guestCheckout;
    private readonly ILogger<CartController>       _logger;

    public CartController(
        ApplicationDbContext db,
        BookingService bookingService,
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        EmailService email,
        GuestCheckoutService guestCheckout,
        ILogger<CartController> logger)
    {
        _db             = db;
        _bookingService = bookingService;
        _userManager    = userManager;
        _config         = config;
        _email          = email;
        _guestCheckout  = guestCheckout;
        _logger         = logger;
    }

    // GET /Cart/Checkout?slug={facilitySlug}
    // The cart's actual items live in the browser's localStorage — this just renders the shell
    // (facility branding + guest-info form) that wwwroot/js/cart.js populates on load.
    public async Task<IActionResult> Checkout(string slug)
    {
        var settings = await _db.FacilitySettings.FirstOrDefaultAsync(s => s.Slug == slug);
        if (settings is null) return NotFound();

        ViewBag.Settings = settings;
        ViewBag.Slug     = slug;
        ViewBag.AddOns   = settings.OwnerId != null
            ? await _bookingService.GetActiveAddOnsAsync(settings.OwnerId)
            : new List<AddOnItem>();

        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                ViewBag.GuestName  = user.FullName;
                ViewBag.GuestEmail = user.Email;
                ViewBag.GuestPhone = user.PhoneNumber;
            }
        }

        return View();
    }

    // POST /Cart/Checkout
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(string slug, string cartJson, string? guestName, string? guestEmail, string? guestPhone)
    {
        var settings = await _db.FacilitySettings.FirstOrDefaultAsync(s => s.Slug == slug);
        if (settings is null) return NotFound();

        List<CartItemRequest>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<CartItemRequest>>(cartJson ?? "[]",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            items = null;
        }

        if (items is null || items.Count == 0)
        {
            TempData["Error"] = "Your cart is empty.";
            return RedirectToAction(nameof(Checkout), new { slug });
        }
        if (items.Count > MaxCartItems)
        {
            TempData["Error"] = $"A cart can hold at most {MaxCartItems} slots. Please check out in smaller batches.";
            return RedirectToAction(nameof(Checkout), new { slug });
        }

        bool isGuest = User.Identity?.IsAuthenticated != true;
        if (isGuest && (string.IsNullOrWhiteSpace(guestName) || string.IsNullOrWhiteSpace(guestEmail) || string.IsNullOrWhiteSpace(guestPhone)))
        {
            TempData["Error"] = "Please enter your name, email, and phone number.";
            return RedirectToAction(nameof(Checkout), new { slug });
        }

        // Resolve every court up front and confirm they all belong to this facility — defense in
        // depth for the "one facility per cart" rule the client-side cart already enforces.
        var courtIds = items.Select(i => i.CourtId).Distinct().ToList();
        var courts = await _db.Courts.Where(c => courtIds.Contains(c.Id) && c.IsActive).ToListAsync();
        var courtsById = courts.ToDictionary(c => c.Id);

        var errors = new List<string>();
        foreach (var item in items)
        {
            if (!courtsById.TryGetValue(item.CourtId, out var court))
            {
                errors.Add($"Court #{item.CourtId} is no longer available.");
                continue;
            }
            if (court.OwnerId != settings.OwnerId)
            {
                errors.Add($"{court.Name} does not belong to this facility.");
            }
        }
        if (errors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", errors);
            return RedirectToAction(nameof(Checkout), new { slug });
        }

        // Re-validate availability & recompute price server-side for every item — never trust the
        // client-supplied price. Abort without creating anything if any single item fails, so the
        // customer can drop just that item (the localStorage cart is untouched) and resubmit.
        var resolved = new List<(CartItemRequest Item, Court Court, TimeOnly Start, TimeOnly End, decimal SlotPrice)>();
        foreach (var item in items)
        {
            var court = courtsById[item.CourtId];
            var start = new TimeOnly(item.StartHour % 24, 0);
            var end   = new TimeOnly(item.EndHour % 24, 0);

            if (item.EndHour <= item.StartHour ||
                item.StartHour < court.OpeningHour || item.EndHour > court.ClosingHour)
            {
                errors.Add($"{court.Name} on {item.Date:MMM d} falls outside operating hours.");
                continue;
            }

            if (!await _bookingService.IsSlotAvailableAsync(court.Id, item.Date, start, end))
            {
                errors.Add($"{court.Name} on {item.Date:MMM d} at {TimeDisplay.Hour(item.StartHour)} is no longer available.");
                continue;
            }

            var price = await _bookingService.GetTotalPriceAsync(court, item.Date, start, end);
            resolved.Add((item, court, start, end, price));
        }

        if (errors.Count > 0)
        {
            TempData["Error"] = "Some slots in your cart are no longer available — please remove them and try again: "
                               + string.Join(" ", errors);
            return RedirectToAction(nameof(Checkout), new { slug });
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
                return RedirectToAction(nameof(Checkout), new { slug });
            }
        }
        else
        {
            userId = _userManager.GetUserId(User)!;
        }

        var groupId    = Guid.NewGuid();
        var guestToken = isGuest ? Guid.NewGuid() : (Guid?)null;
        var bookings   = new List<Booking>();

        foreach (var (item, court, start, end, slotPrice) in resolved)
        {
            var (addOns, addOnsTotal) = court.OwnerId != null
                ? await _bookingService.ResolveAddOnsAsync(
                    court.OwnerId,
                    (item.AddOns ?? new List<CartAddOnRequest>())
                        .Select(a => new BookingService.AddOnSelection(a.AddOnItemId, a.Quantity, a.Hours)),
                    item.EndHour - item.StartHour)
                : (new List<BookingAddOn>(), 0m);

            bookings.Add(new Booking
            {
                CourtId          = court.Id,
                FacilityName     = settings.FacilityName,
                UserId           = userId,
                BookingDate      = item.Date,
                StartTime        = start,
                EndTime          = end,
                TotalPrice       = slotPrice + addOnsTotal,
                Status           = BookingStatus.Pending,
                PaymentStatus    = PaymentStatus.Unpaid,
                BundleGroupId    = groupId,
                GuestAccessToken = guestToken,
                CustomerNameSnapshot = isGuest ? guestName!.Trim() : null,
                ReservedUntil    = DateTime.UtcNow.AddMinutes(15),
                AddOns           = addOns
            });
        }

        _db.Bookings.AddRange(bookings);
        await _db.SaveChangesAsync();

        var customer = await _userManager.FindByIdAsync(userId);
        var owner    = settings.OwnerId != null ? await _userManager.FindByIdAsync(settings.OwnerId) : null;
        await SendNewCartBookingNotificationAsync(bookings, courts, customer, owner);

        // The cart itself lives in the customer's browser (localStorage) — now that its contents
        // are safely persisted as Bookings, tell the destination page to clear it so leftover
        // items don't linger into their next visit.
        TempData["ClearCart"] = true;

        if (isGuest)
        {
            await SendGuestAccessLinkEmailAsync(bookings, customer);
            return RedirectToAction("GuestPay", "BundleBookings", new { token = guestToken });
        }

        return RedirectToAction("Pay", "BundleBookings", new { groupId });
    }

    // ── Request shapes (bound from the cartJson field the client-side cart serializes) ──────────

    public class CartItemRequest
    {
        public int CourtId { get; set; }
        public DateOnly Date { get; set; }
        public int StartHour { get; set; }
        public int EndHour { get; set; }
        public List<CartAddOnRequest>? AddOns { get; set; }
    }

    public class CartAddOnRequest
    {
        public int AddOnItemId { get; set; }
        public int Quantity { get; set; }
        public int? Hours { get; set; }
    }

    // ── Email notifications ───────────────────────────────────────────────────

    private async Task SendNewCartBookingNotificationAsync(
        List<Booking> bookings, List<Court> courts, ApplicationUser? customer, ApplicationUser? owner)
    {
        try
        {
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email))
            {
                _logger.LogWarning("[CartController] Skipped new-cart-booking email: owner missing or has no email");
                return;
            }

            var courtsById    = courts.ToDictionary(c => c.Id);
            var baseUrl       = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var bookingsUrl   = $"{baseUrl}/Admin/Bookings";
            var customerName  = customer?.FullName ?? "A customer";
            var customerEmail = customer?.Email ?? "—";
            var amount        = bookings.Sum(b => b.TotalPrice).ToString("N0");
            var rowsHtml = string.Join("", bookings.Select(b =>
            {
                var courtName = courtsById.TryGetValue(b.CourtId, out var c) ? c.Name : "Court";
                var dateLabel = b.BookingDate.ToString("MMM d, yyyy");
                var timeLabel = $"{b.StartTime:hh\\:mm tt} – {b.EndTime:hh\\:mm tt}";
                return $"<tr><td style='padding:4px 0;color:#212529;'>{courtName}</td><td style='padding:4px 0;color:#6c757d;'>{dateLabel}, {timeLabel}</td><td style='padding:4px 0;text-align:right;font-weight:600;'>₱{b.TotalPrice:N0}</td></tr>";
            }));

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:560px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>🗂️ New Multi-Court Booking Received</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>A customer just booked {bookings.Count} slot{(bookings.Count == 1 ? "" : "s")} at your facility in one checkout:</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>{rowsHtml}</table>
      <table style='width:100%;border-collapse:collapse;font-size:14px;margin-top:12px;border-top:1px solid #e9ecef;padding-top:8px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Total</td> <td style='padding:5px 0;font-weight:600;color:#198754;'>₱{amount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Customer</td><td style='padding:5px 0;'>{customerName}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Email</td>   <td style='padding:5px 0;'><a href='mailto:{customerEmail}' style='color:#0d6efd;'>{customerEmail}</a></td></tr>
      </table>
      <p style='margin:16px 0 0;text-align:center;'>
        <a href='{bookingsUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>View All Bookings</a>
      </p>
    </div>
  </div>
</body></html>";

            var plain = $"New Multi-Court Booking — {bookings.Count} slot(s)\n\nTotal: ₱{amount}\nCustomer: {customerName} ({customerEmail})\n\nView bookings: {bookingsUrl}";
            await _email.SendAsync(owner.Email, $"🗂️ New Multi-Court Booking — {bookings.Count} slot(s)", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CartController] Failed to send new cart booking notification");
        }
    }

    /// <summary>Sent once, right after a guest (no account) checks out a cart — their only way
    /// back to pay, check status, or cancel, since there's no login to fall back on.</summary>
    private async Task SendGuestAccessLinkEmailAsync(List<Booking> bookings, ApplicationUser? guest)
    {
        try
        {
            var first = bookings[0];
            if (guest is null || string.IsNullOrWhiteSpace(guest.Email) || !first.GuestAccessToken.HasValue) return;

            var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var payUrl  = $"{baseUrl}/BundleBookings/GuestPay?token={first.GuestAccessToken}";
            var amount  = bookings.Sum(b => b.TotalPrice).ToString("N0");

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#0d6efd;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;'>CourtBook</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>🗂️ Your Booking — Complete Payment</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>Thanks for booking {bookings.Count} slot{(bookings.Count == 1 ? "" : "s")}! No account needed — use the link below any time to pay, check status, or cancel.</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Amount</td><td style='padding:5px 0;font-weight:600;color:#198754;'>₱{amount}</td></tr>
      </table>
      <p style='margin:20px 0 0;text-align:center;'>
        <a href='{payUrl}' style='display:inline-block;background:#0d6efd;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>Manage My Booking</a>
      </p>
      <p style='margin:16px 0 0;font-size:12px;color:#6c757d;'>Keep this email — it's the only way to access your booking without creating an account.</p>
    </div>
  </div>
</body></html>";

            var plain = $"Your Booking — {bookings.Count} slot(s)\n\nAmount: ₱{amount}\n\nManage your booking: {payUrl}\n\nKeep this email — it's the only way to access your booking without an account.";
            await _email.SendAsync(guest.Email, "🗂️ Your Booking — Complete Payment", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CartController] Failed to send guest access link email");
        }
    }
}
