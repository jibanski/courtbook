using CourtBooking.Models;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CourtBooking.Services;

/// <summary>
/// Wraps the PayMongo v1 REST API for creating and verifying checkout sessions.
/// Each method accepts the facility owner's secret key directly — money flows
/// to that facility's PayMongo account, not to the CourtBook platform.
/// </summary>
public class PayMongoService
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<PayMongoService> _logger;

    private const string BaseUrl = "https://api.paymongo.com/v1";

    /// <summary>All PayMongo Checkout payment methods supported in the Philippines.</summary>
    public static readonly string[] AllPhilippinesMethods =
    {
        "card", "gcash", "paymaya", "grab_pay", "qrph", "dob", "billease"
    };

    public PayMongoService(IHttpClientFactory factory, ILogger<PayMongoService> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    // ── Checkout Session ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a PayMongo hosted Checkout Session for a booking using the
    /// facility owner's secret key. Returns the session ID and the URL to
    /// redirect the customer to. Pass the list of payment methods the facility
    /// has activated on its PayMongo dashboard — defaults to card-only when null.
    /// </summary>
    public async Task<(string SessionId, string CheckoutUrl)> CreateCheckoutSessionAsync(
        string secretKey, Booking booking, string successUrl, string cancelUrl,
        IEnumerable<string>? paymentMethods = null)
    {
        var http = BuildClient(secretKey);

        var amountCentavos = (long)Math.Round(booking.TotalPrice * 100);
        var courtName      = booking.Court?.Name ?? "Court";
        var userName       = booking.User?.FullName;
        var userEmail      = booking.User?.Email ?? "";
        var userPhone      = booking.User?.PhoneNumber;
        var durationLabel  = booking.DurationHours == 1 ? "1 hr" : $"{booking.DurationHours} hrs";

        // Sanitise and dedupe requested methods against the supported set.
        var methods = (paymentMethods ?? new[] { "card" })
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToLowerInvariant())
            .Where(AllPhilippinesMethods.Contains)
            .Distinct()
            .ToArray();
        if (methods.Length == 0) methods = new[] { "card" };

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
                    payment_method_types = methods,
                    success_url          = successUrl,
                    cancel_url           = cancelUrl,
                    reference_number     = $"BOOKING-{booking.Id}"
                }
            }
        };

        var json     = JsonSerializer.Serialize(payload);
        var content  = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"{BaseUrl}/checkout_sessions", content);
        var body     = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PayMongo session creation failed ({Status}): {Body}", response.StatusCode, body);
            throw new InvalidOperationException(
                $"PayMongo returned {(int)response.StatusCode}. Check the facility's secret key.");
        }

        using var doc         = JsonDocument.Parse(body);
        var       dataEl      = doc.RootElement.GetProperty("data");
        var       sessionId   = dataEl.GetProperty("id").GetString()!;
        var       checkoutUrl = dataEl.GetProperty("attributes")
                                      .GetProperty("checkout_url").GetString()!;
        return (sessionId, checkoutUrl);
    }

    /// <summary>
    /// Returns the current status of a checkout session using the facility's key.
    /// Possible values: "active", "unpaid", "paid", "expired".
    /// </summary>
    public async Task<string> GetSessionStatusAsync(string secretKey, string sessionId)
    {
        var http     = BuildClient(secretKey);
        var response = await http.GetAsync($"{BaseUrl}/checkout_sessions/{sessionId}");
        var body     = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PayMongo session retrieve failed ({Status}): {Body}", response.StatusCode, body);
            return "error";
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
                  .GetProperty("data")
                  .GetProperty("attributes")
                  .GetProperty("status")
                  .GetString() ?? "unknown";
    }

    /// <summary>
    /// Returns (status, paymentMethodUsed). paymentMethodUsed is one of
    /// "card", "gcash", "paymaya", "grab_pay", "qrph", "dob", "billease"
    /// or null when not yet determined.
    /// </summary>
    public async Task<(string Status, string? PaymentMethod)> GetSessionDetailsAsync(string secretKey, string sessionId)
    {
        var http     = BuildClient(secretKey);
        var response = await http.GetAsync($"{BaseUrl}/checkout_sessions/{sessionId}");
        var body     = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PayMongo session retrieve failed ({Status}): {Body}", response.StatusCode, body);
            return ("error", null);
        }

        using var doc   = JsonDocument.Parse(body);
        var       attrs = doc.RootElement.GetProperty("data").GetProperty("attributes");
        var       status = attrs.GetProperty("status").GetString() ?? "unknown";

        string? method = null;
        // Look at the latest succeeded payment on the session for the method used.
        if (attrs.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in payments.EnumerateArray())
            {
                if (!p.TryGetProperty("attributes", out var pa)) continue;
                if (!pa.TryGetProperty("status", out var ps) || ps.GetString() != "paid") continue;
                if (pa.TryGetProperty("source", out var src) &&
                    src.TryGetProperty("type", out var st))
                {
                    method = st.GetString();
                    break;
                }
                if (pa.TryGetProperty("payment_method_used", out var pmu))
                {
                    method = pmu.GetString();
                    break;
                }
            }
        }
        return (status, method);
    }

    /// <summary>
    /// Verifies an incoming PayMongo webhook by recomputing the HMAC-SHA256
    /// signature over "{timestamp}.{rawBody}" with the platform webhook secret.
    /// The header looks like: t=1700000000,te=HEX,li=HEX (te = test, li = live).
    /// Returns true only when one of the two MACs matches in constant time.
    /// </summary>
    public static bool VerifyWebhookSignature(string rawBody, string? signatureHeader, string? webhookSecret)
    {
        if (string.IsNullOrEmpty(webhookSecret) || string.IsNullOrEmpty(signatureHeader))
            return false;

        string? timestamp = null, testSig = null, liveSig = null;
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            switch (kv[0].Trim())
            {
                case "t":  timestamp = kv[1].Trim(); break;
                case "te": testSig   = kv[1].Trim(); break;
                case "li": liveSig   = kv[1].Trim(); break;
            }
        }
        if (string.IsNullOrEmpty(timestamp)) return false;

        var data = Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var computed = Convert.ToHexString(hmac.ComputeHash(data)).ToLowerInvariant();

        bool Match(string? candidate)
            => !string.IsNullOrEmpty(candidate) &&
               CryptographicOperations.FixedTimeEquals(
                   Encoding.UTF8.GetBytes(computed),
                   Encoding.UTF8.GetBytes(candidate.ToLowerInvariant()));

        return Match(testSig) || Match(liveSig);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private HttpClient BuildClient(string secretKey)
    {
        var http  = _factory.CreateClient("paymongo");
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(secretKey + ":"));
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", token);
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }
}
