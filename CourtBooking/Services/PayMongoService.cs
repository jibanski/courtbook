using CourtBooking.Models;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CourtBooking.Services;

/// <summary>
/// Wraps the PayMongo v1 REST API for creating and verifying checkout sessions.
/// Configure via PayMongo:SecretKey and PayMongo:WebhookSecret in appsettings / env vars.
/// </summary>
public class PayMongoService
{
    private readonly HttpClient     _http;
    private readonly string         _secretKey;
    private readonly string         _webhookSecret;
    private readonly ILogger<PayMongoService> _logger;

    private const string BaseUrl = "https://api.paymongo.com/v1";

    public PayMongoService(IHttpClientFactory factory, IConfiguration config, ILogger<PayMongoService> logger)
    {
        _secretKey     = config["PayMongo:SecretKey"]     ?? string.Empty;
        _webhookSecret = config["PayMongo:WebhookSecret"] ?? string.Empty;
        _logger        = logger;

        _http = factory.CreateClient("paymongo");
        if (!string.IsNullOrEmpty(_secretKey))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(_secretKey + ":"));
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", token);
        }
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>True when a secret key has been configured.</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(_secretKey);

    // ── Checkout Session ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a PayMongo hosted Checkout Session for a booking.
    /// Returns the session ID and the URL to redirect the customer to.
    /// </summary>
    public async Task<(string SessionId, string CheckoutUrl)> CreateCheckoutSessionAsync(
        Booking booking, string successUrl, string cancelUrl)
    {
        var amountCentavos = (long)Math.Round(booking.TotalPrice * 100);
        var courtName      = booking.Court?.Name ?? "Court";
        var userName       = booking.User?.FullName;
        var userEmail      = booking.User?.Email ?? "";
        var userPhone      = booking.User?.PhoneNumber;

        var durationLabel = booking.DurationHours == 1 ? "1 hr" : $"{booking.DurationHours} hrs";

        var payload = new
        {
            data = new
            {
                attributes = new
                {
                    send_email_receipt = true,
                    show_description   = true,
                    show_line_items    = true,
                    billing = new
                    {
                        name  = string.IsNullOrWhiteSpace(userName) ? "Customer" : userName,
                        email = userEmail,
                        phone = string.IsNullOrWhiteSpace(userPhone) ? (string?)null : userPhone
                    },
                    description = $"Booking #{booking.Id} – {courtName} on " +
                                  $"{booking.BookingDate:MMMM d, yyyy} " +
                                  $"{booking.StartTime:HH:mm}–{booking.EndTime:HH:mm}",
                    line_items = new[]
                    {
                        new
                        {
                            currency = "PHP",
                            amount   = amountCentavos,
                            name     = $"{courtName} ({durationLabel})",
                            quantity = 1
                        }
                    },
                    payment_method_types = new[] { "card" },
                    success_url          = successUrl,
                    cancel_url           = cancelUrl,
                    reference_number     = $"BOOKING-{booking.Id}"
                }
            }
        };

        var json    = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response     = await _http.PostAsync($"{BaseUrl}/checkout_sessions", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PayMongo session creation failed ({Status}): {Body}",
                             response.StatusCode, responseBody);
            throw new InvalidOperationException(
                $"PayMongo returned {(int)response.StatusCode}. Check your secret key.");
        }

        using var doc        = JsonDocument.Parse(responseBody);
        var       dataEl     = doc.RootElement.GetProperty("data");
        var       sessionId  = dataEl.GetProperty("id").GetString()!;
        var       checkoutUrl = dataEl.GetProperty("attributes")
                                     .GetProperty("checkout_url").GetString()!;

        return (sessionId, checkoutUrl);
    }

    /// <summary>
    /// Returns the current status of a checkout session.
    /// Possible values: "active", "unpaid", "paid", "expired".
    /// </summary>
    public async Task<string> GetSessionStatusAsync(string sessionId)
    {
        var response     = await _http.GetAsync($"{BaseUrl}/checkout_sessions/{sessionId}");
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PayMongo session retrieve failed ({Status}): {Body}",
                             response.StatusCode, responseBody);
            return "error";
        }

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement
                  .GetProperty("data")
                  .GetProperty("attributes")
                  .GetProperty("status")
                  .GetString() ?? "unknown";
    }

    // ── Webhook ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the HMAC-SHA256 signature on an incoming PayMongo webhook.
    /// Header format: t={timestamp},te={test_hash},li={live_hash}
    /// </summary>
    public bool VerifyWebhookSignature(string rawBody, string signatureHeader)
    {
        if (string.IsNullOrEmpty(_webhookSecret)) return false;

        var parts     = signatureHeader.Split(',');
        var timestamp = parts.FirstOrDefault(p => p.StartsWith("t="))?.Substring(2);
        var isLive    = !_secretKey.StartsWith("sk_test_");
        var sigPart   = isLive
            ? parts.FirstOrDefault(p => p.StartsWith("li="))?.Substring(3)
            : parts.FirstOrDefault(p => p.StartsWith("te="))?.Substring(3);

        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(sigPart)) return false;

        var toSign  = $"{timestamp}.{rawBody}";
        var key     = Encoding.UTF8.GetBytes(_webhookSecret);
        var message = Encoding.UTF8.GetBytes(toSign);

        using var hmac        = new HMACSHA256(key);
        var       hash        = hmac.ComputeHash(message);
        var       expectedSig = Convert.ToHexString(hash).ToLower();

        return string.Equals(sigPart, expectedSig, StringComparison.OrdinalIgnoreCase);
    }
}
