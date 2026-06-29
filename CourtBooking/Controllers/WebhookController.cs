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
/// AllowAnonymous — authenticity is verified either by HMAC signature
/// (when PayMongo:WebhookSecret is configured) or, as a fallback for
/// older deployments, by re-fetching the session via the facility's secret key.
/// </summary>
[AllowAnonymous]
[ApiController]
public class WebhookController : ControllerBase
{
    private readonly ApplicationDbContext      _db;
    private readonly PayMongoService           _payMongo;
    private readonly IConfiguration            _config;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        ApplicationDbContext db,
        PayMongoService payMongo,
        IConfiguration config,
        ILogger<WebhookController> logger)
    {
        _db       = db;
        _payMongo = payMongo;
        _config   = config;
        _logger   = logger;
    }

    // POST /webhook/paymongo
    [HttpPost("/webhook/paymongo")]
    public async Task<IActionResult> PayMongo()
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body))
            rawBody = await reader.ReadToEndAsync();

        // ── Authenticate ──────────────────────────────────────────────────
        // Preferred: PayMongo HMAC signature using a platform-wide webhook secret.
        // Fallback (when no secret is configured): re-fetch the session via the
        // facility's own key further down before mutating any booking.
        var webhookSecret = _config["PayMongo:WebhookSecret"];
        var signature     = Request.Headers["Paymongo-Signature"].FirstOrDefault();
        var signatureOk   = !string.IsNullOrEmpty(webhookSecret)
                            && PayMongoService.VerifyWebhookSignature(rawBody, signature, webhookSecret);

        if (!string.IsNullOrEmpty(webhookSecret) && !signatureOk)
        {
            _logger.LogWarning("Rejected PayMongo webhook with bad/missing signature.");
            return Unauthorized();
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var attrs     = doc.RootElement.GetProperty("data").GetProperty("attributes");
            var eventType = attrs.GetProperty("type").GetString();

            _logger.LogInformation("PayMongo webhook: {EventType} (signed={Signed})", eventType, signatureOk);

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
                        var facilitySettings = booking.Court?.OwnerId != null
                            ? await _db.FacilitySettings
                                .FirstOrDefaultAsync(s => s.OwnerId == booking.Court.OwnerId)
                            : null;
                        var secretKey = facilitySettings?.PayMongoSecretKey;

                        var    confirmed   = false;
                        string? methodUsed = null;

                        if (signatureOk)
                        {
                            // Signature already proves the payload is authentic — trust it.
                            confirmed = true;
                            methodUsed = TryReadPaymentMethod(sessionAttrs);
                        }
                        else if (!string.IsNullOrEmpty(secretKey) && !string.IsNullOrEmpty(sessionId))
                        {
                            // Legacy verification path: re-fetch with the facility's own key.
                            var (status, method) = await _payMongo.GetSessionDetailsAsync(secretKey, sessionId);
                            confirmed  = status == "paid";
                            methodUsed = method;
                        }

                        if (confirmed)
                        {
                            booking.PaymentStatus    = PaymentStatus.Paid;
                            booking.Status           = BookingStatus.Confirmed;
                            booking.PaymentMethod    = FormatMethod(methodUsed);
                            booking.PaymentReference = sessionId;
                            booking.PaidAt           = DateTime.UtcNow;
                            await _db.SaveChangesAsync();
                            _logger.LogInformation(
                                "Booking #{BookingId} confirmed via PayMongo webhook ({Method}).",
                                bookingId, booking.PaymentMethod);
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

    private static string? TryReadPaymentMethod(JsonElement sessionAttrs)
    {
        if (sessionAttrs.TryGetProperty("payments", out var payments) &&
            payments.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in payments.EnumerateArray())
            {
                if (!p.TryGetProperty("attributes", out var pa)) continue;
                if (pa.TryGetProperty("source", out var src) &&
                    src.TryGetProperty("type", out var st))
                    return st.GetString();
                if (pa.TryGetProperty("payment_method_used", out var pmu))
                    return pmu.GetString();
            }
        }
        return null;
    }

    private static string FormatMethod(string? m) => (m ?? "").ToLowerInvariant() switch
    {
        "card"     => "Card",
        "gcash"    => "GCash",
        "paymaya"  => "Maya",
        "grab_pay" => "GrabPay",
        "qrph"     => "QRPh",
        "dob"      => "Online Banking",
        "billease" => "BillEase",
        ""         => "Card",
        _          => char.ToUpperInvariant(m![0]) + m[1..]
    };
}
