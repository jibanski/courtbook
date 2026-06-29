using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace CourtBooking.Services;

/// <summary>
/// Email sender that supports two providers, selected by the
/// <c>Email:Provider</c> config key:
///
///   - <c>"BrevoHttp"</c>: POSTs to https://api.brevo.com/v3/smtp/email.
///     Required because Railway (and several other PaaS hosts) block
///     outbound SMTP entirely. Uses port 443 so it works anywhere.
///
///   - anything else (default): falls back to <see cref="SmtpClient"/>
///     using <c>Email:SmtpHost</c> / <c>SmtpPort</c> / <c>SmtpUser</c> /
///     <c>SmtpPass</c>. Useful for local dev or self-hosted deployments
///     where SMTP isn't blocked.
///
/// When the configured provider is unreachable (e.g. no creds in local
/// dev), <see cref="SendAsync"/> logs a warning instead of throwing so
/// the rest of the app keeps working.
/// </summary>
public class EmailService
{
    private const string BrevoEndpoint = "https://api.brevo.com/v3/smtp/email";

    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public EmailService(
        IConfiguration config,
        ILogger<EmailService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config            = config;
        _logger            = logger;
        _httpClientFactory = httpClientFactory;
    }

    private string Provider => _config["Email:Provider"] ?? "Smtp";

    /// <summary>True when the active provider has all the config it needs.</summary>
    public bool IsConfigured
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_config["Email:FromAddress"])) return false;
            return IsBrevoHttp
                ? !string.IsNullOrWhiteSpace(_config["Email:ApiKey"])
                : !string.IsNullOrWhiteSpace(_config["Email:SmtpHost"]);
        }
    }

    private bool IsBrevoHttp =>
        string.Equals(Provider, "BrevoHttp", StringComparison.OrdinalIgnoreCase);

    public async Task SendAsync(string toEmail, string subject, string htmlBody, string? plainBody = null)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning(
                "[EmailService] {Provider} not configured — would have sent to {To}\nSubject: {Subject}",
                Provider, toEmail, subject);
            return;
        }

        if (IsBrevoHttp)
            await SendViaBrevoHttpAsync(toEmail, subject, htmlBody, plainBody);
        else
            await SendViaSmtpAsync(toEmail, subject, htmlBody, plainBody);
    }

    // ── Brevo HTTP API ────────────────────────────────────────────────────────

    private async Task SendViaBrevoHttpAsync(string toEmail, string subject, string htmlBody, string? plainBody)
    {
        var apiKey   = _config["Email:ApiKey"]!;
        var fromAddr = _config["Email:FromAddress"]!;
        var fromName = _config["Email:FromName"] ?? "CourtBook";

        var payload = new
        {
            sender      = new { name = fromName, email = fromAddr },
            to          = new[] { new { email = toEmail } },
            subject     = subject,
            htmlContent = htmlBody,
            textContent = plainBody ?? StripHtml(htmlBody)
        };

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        using var req = new HttpRequestMessage(HttpMethod.Post, BrevoEndpoint);
        req.Headers.Add("api-key", apiKey);
        req.Headers.Add("Accept",  "application/json");
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage res;
        string responseBody;
        try
        {
            res = await http.SendAsync(req);
            responseBody = await res.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmailService] Brevo HTTP send failed for '{Subject}' to {To}", subject, toEmail);
            throw;
        }

        if (res.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "[EmailService] Sent '{Subject}' to {To} via Brevo HTTP. Response: {Body}",
                subject, toEmail, responseBody);
        }
        else
        {
            _logger.LogError(
                "[EmailService] Brevo HTTP rejected '{Subject}' to {To}. Status: {Status}. Body: {Body}",
                subject, toEmail, (int)res.StatusCode, responseBody);
            throw new InvalidOperationException(
                $"Brevo API returned {(int)res.StatusCode}: {responseBody}");
        }
    }

    // ── SMTP ──────────────────────────────────────────────────────────────────

    private async Task SendViaSmtpAsync(string toEmail, string subject, string htmlBody, string? plainBody)
    {
        var fromAddress = _config["Email:FromAddress"]!;
        var fromName    = _config["Email:FromName"] ?? "CourtBook";
        var host        = _config["Email:SmtpHost"]!;
        var port        = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
        var user        = _config["Email:SmtpUser"];
        var pass        = _config["Email:SmtpPass"];
        var enableSsl   = !bool.TryParse(_config["Email:EnableSsl"], out var ssl) || ssl;

        using var msg = new MailMessage
        {
            From       = new MailAddress(fromAddress, fromName),
            Subject    = subject,
            Body       = htmlBody,
            IsBodyHtml = true,
        };
        msg.To.Add(toEmail);

        if (!string.IsNullOrWhiteSpace(plainBody))
        {
            var plainView = AlternateView.CreateAlternateViewFromString(plainBody, null, "text/plain");
            var htmlView  = AlternateView.CreateAlternateViewFromString(htmlBody,  null, "text/html");
            msg.AlternateViews.Add(plainView);
            msg.AlternateViews.Add(htmlView);
        }

        using var client = new SmtpClient(host, port)
        {
            EnableSsl   = enableSsl,
            Timeout     = 30000,
            Credentials = string.IsNullOrWhiteSpace(user)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(user, pass),
        };

        try
        {
            await client.SendMailAsync(msg);
            _logger.LogInformation("[EmailService] Sent '{Subject}' to {To} via SMTP", subject, toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmailService] Failed to send '{Subject}' to {To} via SMTP", subject, toEmail);
            throw;
        }
    }

    private static string StripHtml(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);

    /// <summary>
    /// Sends a "payment received — booking confirmed" email to the customer.
    /// Safe to fire-and-forget; never throws.
    /// </summary>
    public async Task SendBookingConfirmedToCustomerAsync(
        string toEmail,
        string? customerFirstName,
        int bookingId,
        string courtName,
        DateOnly bookingDate,
        TimeOnly startTime,
        TimeOnly endTime,
        decimal totalPrice,
        string? paymentMethod,
        string? paymentReference,
        string baseUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(toEmail)) return;

            var greeting    = string.IsNullOrWhiteSpace(customerFirstName) ? "Hi there" : $"Hi {customerFirstName}";
            var dateLabel   = bookingDate.ToString("dddd, MMMM d, yyyy");
            var timeLabel   = $"{startTime:hh\\:mm tt} – {endTime:hh\\:mm tt}";
            var amount      = totalPrice.ToString("N0");
            var method      = string.IsNullOrWhiteSpace(paymentMethod) ? "Online payment" : paymentMethod;
            var refLine     = string.IsNullOrWhiteSpace(paymentReference) ? "" :
                              $"<tr><td style='color:#6c757d;padding:5px 0;'>Reference</td><td style='padding:5px 0;font-family:monospace;font-size:13px;'>{paymentReference}</td></tr>";
            var myBookings  = $"{baseUrl.TrimEnd('/')}/Bookings/My";

            var html = $@"<!doctype html>
<html><body style='font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;'>
  <div style='max-width:540px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;'>
    <div style='background:#198754;color:#fff;padding:18px 24px;'>
      <div style='font-size:13px;opacity:.9;letter-spacing:.5px;text-transform:uppercase;'>Booking Confirmed</div>
      <div style='font-size:20px;font-weight:700;margin-top:4px;'>✅ Payment Received</div>
    </div>
    <div style='padding:24px;font-size:15px;line-height:1.6;'>
      <p style='margin:0 0 16px;'>{greeting}, your payment has been received and your booking is now <strong style='color:#198754;'>confirmed</strong>.</p>
      <table style='width:100%;border-collapse:collapse;font-size:14px;'>
        <tr><td style='color:#6c757d;padding:5px 0;width:120px;'>Court</td>     <td style='font-weight:600;padding:5px 0;'>{courtName}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Date</td>      <td style='font-weight:600;padding:5px 0;'>{dateLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Time</td>      <td style='padding:5px 0;'>{timeLabel}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Amount</td>    <td style='padding:5px 0;font-weight:600;color:#198754;'>₱{amount}</td></tr>
        <tr><td style='color:#6c757d;padding:5px 0;'>Method</td>    <td style='padding:5px 0;'>{method}</td></tr>
        {refLine}
        <tr><td style='color:#6c757d;padding:5px 0;'>Booking #</td> <td style='padding:5px 0;'>#{bookingId}</td></tr>
      </table>
      <p style='margin:20px 0 0;text-align:center;'>
        <a href='{myBookings}' style='display:inline-block;background:#198754;color:#fff;text-decoration:none;font-weight:600;padding:11px 24px;border-radius:6px;font-size:14px;'>View My Bookings</a>
      </p>
    </div>
    <div style='background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;'>
      Automated confirmation · Booking #{bookingId}
    </div>
  </div>
</body></html>";

            var plain = $"Payment Received — Booking #{bookingId} Confirmed\n\n{greeting},\n\nYour payment for {courtName} on {dateLabel} ({timeLabel}) of ₱{amount} via {method} has been received. Your booking is now confirmed.\n\nView your bookings: {myBookings}";

            await SendAsync(toEmail, $"✅ Booking Confirmed — {courtName} on {dateLabel}", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmailService] Failed to send confirmation email for booking #{Id}", bookingId);
        }
    }
}
