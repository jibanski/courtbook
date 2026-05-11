using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CourtBooking.Services;

public class PayMongoSource
{
    public string Id { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class PaymentService
{
    private const string BaseUrl = "https://api.paymongo.com/v1";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<PaymentService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_config["PayMongo:SecretKey"]);

    /// <summary>Creates a GCash or Maya e-wallet source and returns the redirect URL.</summary>
    /// <param name="type">"gcash" or "paymaya"</param>
    public async Task<PayMongoSource> CreateSourceAsync(
        string type, decimal amount, string description, string successUrl, string failedUrl)
    {
        var client = CreateClient();
        var payload = new
        {
            data = new
            {
                attributes = new
                {
                    amount = (int)(amount * 100),
                    redirect = new { success = successUrl, failed = failedUrl },
                    type = type,
                    currency = "PHP",
                    description = description
                }
            }
        };

        var response = await client.PostAsync($"{BaseUrl}/sources", Serialize(payload));

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("PayMongo source creation failed ({Type}): {Error}", type, err);
            throw new Exception($"PayMongo error: {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        var attrs = data.GetProperty("attributes");

        return new PayMongoSource
        {
            Id = data.GetProperty("id").GetString()!,
            CheckoutUrl = attrs.GetProperty("redirect").GetProperty("checkout_url").GetString()!,
            Status = attrs.GetProperty("status").GetString()!
        };
    }

    /// <summary>Fetches current source status: "pending", "chargeable", "cancelled", "consumed", "expired".</summary>
    public async Task<string> GetSourceStatusAsync(string sourceId)
    {
        var client = CreateClient();
        var response = await client.GetAsync($"{BaseUrl}/sources/{sourceId}");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("attributes")
                  .GetProperty("status").GetString() ?? "pending";
    }

    /// <summary>Charges a chargeable source — must be called after source.chargeable webhook or poll.</summary>
    public async Task<string> CreatePaymentAsync(string sourceId, decimal amount, string description)
    {
        var client = CreateClient();
        var payload = new
        {
            data = new
            {
                attributes = new
                {
                    amount = (int)(amount * 100),
                    source = new { id = sourceId, type = "source" },
                    currency = "PHP",
                    description = description
                }
            }
        };

        var response = await client.PostAsync($"{BaseUrl}/payments", Serialize(payload));

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("PayMongo payment creation failed: {Error}", err);
            throw new Exception($"PayMongo payment error: {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("attributes")
                  .GetProperty("status").GetString() ?? "unknown";
    }

    public bool VerifyWebhookSignature(string rawBody, string signatureHeader)
    {
        var webhookSecret = _config["PayMongo:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret)) return true;

        // Header format: t=<timestamp>,te=<hmac_test>,li=<hmac_live>
        var parts = signatureHeader.Split(',').ToDictionary(
            p => p.Split('=')[0],
            p => p.Substring(p.IndexOf('=') + 1));

        if (!parts.TryGetValue("t", out var timestamp)) return false;

        var toSign = $"{timestamp}.{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign))).ToLower();

        var provided = parts.TryGetValue("te", out var te) ? te : (parts.TryGetValue("li", out var li) ? li : null);
        return provided == computed;
    }

    private HttpClient CreateClient()
    {
        var secretKey = _config["PayMongo:SecretKey"]
            ?? throw new InvalidOperationException("PayMongo:SecretKey not configured.");
        var client = _httpClientFactory.CreateClient("PayMongo");
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secretKey}:"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static StringContent Serialize(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
