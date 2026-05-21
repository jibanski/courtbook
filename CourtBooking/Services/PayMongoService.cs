using CourtBooking.Models;
using System.Net.Http.Headers;
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

    public PayMongoService(IHttpClientFactory factory, ILogger<PayMongoService> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    // ── Checkout Session ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a PayMongo hosted Checkout Session for a booking using the
    /// facility owner's secret key. Returns the session ID and the URL to
    /// redirect the customer to.
    /// </summary>
    public async Task<(string SessionId, string CheckoutUrl)> CreateCheckoutSessionAsync(
        string secretKey, Booking booking, string successUrl, string cancelUrl)
    {
        var http = BuildClient(secretKey);

        var amountCentavos = (long)Math.Round(booking.TotalPrice * 100);
        var courtName      = booking.Court?.Name ?? "Court";
        var userName       = booking.User?.FullName;
        var userEmail      = booking.User?.Email ?? "";
        var userPhone      = booking.User?.PhoneNumber;
        var durationLabel  = booking.DurationHours == 1 ? "1 hr" : $"{booking.DurationHours} hrs";

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
