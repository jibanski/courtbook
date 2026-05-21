using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CourtBooking.Controllers;

/// <summary>
/// Receives async webhook events from PayMongo.
/// AllowAnonymous — authenticity is verified by looking up the payment status
/// directly via the facility's own secret key (no shared webhook secret needed).
/// </summary>
[AllowAnonymous]
[ApiController]
public class WebhookController : ControllerBase
{
    private readonly ApplicationDbContext      _db;
    private readonly PayMongoService           _payMongo;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(ApplicationDbContext db, PayMongoService payMongo, ILogger<WebhookController> logger)
    {
        _db       = db;
        _payMongo = payMongo;
        _logger   = logger;
    }

    // POST /webhook/paymongo
    [HttpPost("/webhook/paymongo")]
    public async Task<IActionResult> PayMongo()
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body))
            rawBody = await reader.ReadToEndAsync();

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var attrs     = doc.RootElement.GetProperty("data").GetProperty("attributes");
            var eventType = attrs.GetProperty("type").GetString();

            _logger.LogInformation("PayMongo webhook: {EventType}", eventType);

            if (eventType == "checkout_session.payment.paid")
            {
                var sessionData  = attrs.GetProperty("data");
                var sessionId    = sessionData.GetProperty("id").GetString();
                var sessionAttrs = sessionData.GetProperty("attributes");
                var refNumber    = sessionAttrs.TryGetProperty("reference_number", out var refEl)
                    ? refEl.GetString() : null;

                if (refNumber?.StartsWith("BOOKING-") == true
                    && int.TryParse(refNumber[8..], out var bookingId))
                {
                    var booking = await _db.Bookings
                        .Include(b => b.Court)
                        .FirstOrDefaultAsync(b => b.Id == bookingId);

                    if (booking is not null && booking.PaymentStatus == PaymentStatus.Unpaid)
                    {
                        // Verify the payment is real using the facility's own secret key
                        var facilitySettings = booking.Court?.OwnerId != null
                            ? await _db.FacilitySettings
                                .FirstOrDefaultAsync(s => s.OwnerId == booking.Court.OwnerId)
                            : null;
                        var secretKey = facilitySettings?.PayMongoSecretKey;

                        var confirmed = false;
                        if (!string.IsNullOrEmpty(secretKey) && !string.IsNullOrEmpty(sessionId))
                        {
                            var status = await _payMongo.GetSessionStatusAsync(secretKey, sessionId);
                            confirmed  = status == "paid";
                        }

                        if (confirmed)
                        {
                            booking.PaymentStatus    = PaymentStatus.Paid;
                            booking.Status           = BookingStatus.Confirmed;
                            booking.PaymentMethod    = "Card";
                            booking.PaymentReference = sessionId;
                            booking.PaidAt           = DateTime.UtcNow;
                            await _db.SaveChangesAsync();
                            _logger.LogInformation("Booking #{BookingId} confirmed via PayMongo webhook.", bookingId);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PayMongo webhook.");
        }

        return Ok(); // always 200 — PayMongo retries on non-2xx
    }
}
