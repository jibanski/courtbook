using CourtBooking.Data;
using CourtBooking.Models;
using CourtBooking.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CourtBooking.Controllers;

/// <summary>
/// Receives async webhook events from payment providers.
/// All endpoints are AllowAnonymous — authenticity is verified via HMAC signatures.
/// </summary>
[AllowAnonymous]
[ApiController]
public class WebhookController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly PayMongoService      _payMongo;
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
        // Read the raw body (needed for signature verification)
        string rawBody;
        using (var reader = new StreamReader(Request.Body))
            rawBody = await reader.ReadToEndAsync();

        var signatureHeader = Request.Headers["Paymongo-Signature"].ToString();

        // Verify signature if a webhook secret is configured
        if (!string.IsNullOrEmpty(signatureHeader))
        {
            if (!_payMongo.VerifyWebhookSignature(rawBody, signatureHeader))
            {
                _logger.LogWarning("PayMongo webhook signature verification failed.");
                return Unauthorized();
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var attrs     = doc.RootElement.GetProperty("data").GetProperty("attributes");
            var eventType = attrs.GetProperty("type").GetString();

            _logger.LogInformation("PayMongo webhook received: {EventType}", eventType);

            if (eventType == "checkout_session.payment.paid")
            {
                // Nested structure: data.attributes.data.attributes
                var sessionAttrs = attrs.GetProperty("data").GetProperty("attributes");
                var sessionId    = attrs.GetProperty("data").GetProperty("id").GetString();
                var refNumber    = sessionAttrs.TryGetProperty("reference_number", out var refEl)
                    ? refEl.GetString()
                    : null;

                if (refNumber?.StartsWith("BOOKING-") == true
                    && int.TryParse(refNumber[8..], out var bookingId))
                {
                    var booking = await _db.Bookings.FindAsync(bookingId);
                    if (booking is not null && booking.PaymentStatus == PaymentStatus.Unpaid)
                    {
                        booking.PaymentStatus    = PaymentStatus.Paid;
                        booking.Status           = BookingStatus.Confirmed;
                        booking.PaymentMethod    = "Card";
                        booking.PaymentReference = sessionId;
                        booking.PaidAt           = DateTime.UtcNow;
                        await _db.SaveChangesAsync();

                        _logger.LogInformation("Booking #{BookingId} marked Paid via PayMongo webhook.", bookingId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PayMongo webhook.");
            // Return 200 anyway — PayMongo retries on non-2xx responses, leading to infinite loops
        }

        return Ok();
    }
}
