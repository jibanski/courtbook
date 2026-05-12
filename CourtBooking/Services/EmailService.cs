using System.Net;
using System.Net.Mail;

namespace CourtBooking.Services;

/// <summary>
/// Lightweight SMTP wrapper. Reads configuration from the "Email" section of
/// appsettings.json (overridable via env vars like Email__SmtpHost). If SMTP
/// is unconfigured (e.g. local dev), <see cref="SendAsync"/> simply logs the
/// message instead of throwing, so the rest of the app keeps working.
/// </summary>
public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>True when SMTP is configured well enough to send.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Email:SmtpHost"]) &&
        !string.IsNullOrWhiteSpace(_config["Email:FromAddress"]);

    public async Task SendAsync(string toEmail, string subject, string htmlBody, string? plainBody = null)
    {
        var fromAddress = _config["Email:FromAddress"];
        var fromName    = _config["Email:FromName"] ?? "CourtBook";
        var host        = _config["Email:SmtpHost"];
        var port        = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
        var user        = _config["Email:SmtpUser"];
        var pass        = _config["Email:SmtpPass"];
        var enableSsl   = !bool.TryParse(_config["Email:EnableSsl"], out var ssl) || ssl; // default true

        if (!IsConfigured)
        {
            _logger.LogWarning(
                "[EmailService] SMTP not configured — would have sent to {To}\nSubject: {Subject}\n{Body}",
                toEmail, subject, plainBody ?? StripHtml(htmlBody));
            return;
        }

        using var msg = new MailMessage
        {
            From       = new MailAddress(fromAddress!, fromName),
            Subject    = subject,
            Body       = htmlBody,
            IsBodyHtml = true,
        };
        msg.To.Add(toEmail);

        // Plaintext fallback for clients that prefer it
        if (!string.IsNullOrWhiteSpace(plainBody))
        {
            var plainView = AlternateView.CreateAlternateViewFromString(plainBody, null, "text/plain");
            var htmlView  = AlternateView.CreateAlternateViewFromString(htmlBody,  null, "text/html");
            msg.AlternateViews.Add(plainView);
            msg.AlternateViews.Add(htmlView);
        }

        using var client = new SmtpClient(host!, port)
        {
            EnableSsl   = enableSsl,
            Timeout     = 30000,   // ms — fail fast instead of hanging for 100s
            Credentials = string.IsNullOrWhiteSpace(user)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(user, pass),
        };

        try
        {
            await client.SendMailAsync(msg);
            _logger.LogInformation("[EmailService] Sent '{Subject}' to {To}", subject, toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmailService] Failed to send '{Subject}' to {To}", subject, toEmail);
            throw;
        }
    }

    private static string StripHtml(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
}
