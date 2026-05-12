using CourtBooking.Data;
using CourtBooking.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Services;

/// <summary>
/// Daily background job that emails admins as their subscription approaches
/// (or passes) its expiry date. Fires at thresholds 14, 7, 3, 1, and 0 days
/// (the last meaning "subscription has expired"). Each threshold is sent at
/// most once per cycle thanks to <see cref="FacilitySettings.LastExpiryReminderThreshold"/>,
/// which is cleared back to null whenever the subscription is renewed.
/// </summary>
public class SubscriptionReminderHostedService : BackgroundService
{
    private static readonly TimeSpan Interval     = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    // Ordered largest → smallest so we always fire the closest unsent threshold.
    private static readonly int[] Thresholds = { 14, 7, 3, 1, 0 };

    private readonly IServiceProvider _services;
    private readonly ILogger<SubscriptionReminderHostedService> _logger;
    private readonly IConfiguration _config;

    public SubscriptionReminderHostedService(
        IServiceProvider services,
        ILogger<SubscriptionReminderHostedService> logger,
        IConfiguration config)
    {
        _services = services;
        _logger   = logger;
        _config   = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[SubReminders] starting; first check in {Delay}", StartupDelay);
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SubReminders] unhandled exception during sweep");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email       = scope.ServiceProvider.GetRequiredService<EmailService>();

        // Load subscribed facilities — we evaluate the rest in memory because
        // the expiry/threshold logic is computed (not stored).
        var subs = await db.FacilitySettings
            .Where(s => s.IsSubscribed
                     && s.OwnerId != null
                     && (s.SubscriptionExpiresAt.HasValue || s.SubscriptionActivatedAt.HasValue))
            .ToListAsync(ct);

        var checkedCount = 0;
        var sentCount    = 0;

        foreach (var s in subs)
        {
            checkedCount++;
            var threshold = ChooseThreshold(s);
            if (threshold is null) continue;                     // not in any reminder window
            if (s.LastExpiryReminderThreshold.HasValue
                && s.LastExpiryReminderThreshold.Value <= threshold.Value)
                continue;                                        // already sent this (or a closer) one

            var owner = await userManager.FindByIdAsync(s.OwnerId!);
            if (owner?.Email is null)
            {
                _logger.LogWarning("[SubReminders] facility #{Id} has no owner email — skipping", s.Id);
                continue;
            }

            try
            {
                var (subject, html, plain) = BuildEmail(owner, s, threshold.Value);
                await email.SendAsync(owner.Email, subject, html, plain);
                s.LastExpiryReminderThreshold = threshold.Value;
                await db.SaveChangesAsync(ct);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SubReminders] failed to send reminder for facility #{Id}", s.Id);
                // intentionally NOT updating LastExpiryReminderThreshold — try again tomorrow
            }
        }

        _logger.LogInformation("[SubReminders] sweep complete — checked {Checked}, sent {Sent}",
            checkedCount, sentCount);
    }

    /// <summary>Returns the threshold this subscription is currently in, or null if none.</summary>
    private static int? ChooseThreshold(FacilitySettings s)
    {
        var days = s.SubscriptionDaysRemaining;
        if (s.IsSubscriptionExpired) return 0;
        foreach (var t in Thresholds.Where(x => x > 0))
            if (days <= t) return t;
        return null;
    }

    private (string subject, string htmlBody, string plainBody) BuildEmail(
        ApplicationUser owner, FacilitySettings s, int threshold)
    {
        var contactEmail = _config["Subscription:ContactEmail"] ?? "sales@courtbook.com";
        var contactPhone = _config["Subscription:ContactPhone"] ?? "";
        var firstName    = string.IsNullOrWhiteSpace(owner.FirstName) ? "there" : owner.FirstName;
        var expiryStr    = s.EffectiveSubscriptionExpiry?.ToLocalTime().ToString("MMMM d, yyyy") ?? "—";
        var planLabel    = string.Equals(s.SubscriptionPlan, "annual", StringComparison.OrdinalIgnoreCase)
                            ? "Annual" : "Monthly";

        string subject;
        string headline;
        string lede;
        string ctaColor;

        if (threshold == 0)
        {
            subject  = $"Your CourtBook Pro subscription has expired";
            headline = "Subscription Expired";
            lede     = $"Your CourtBook Pro subscription for <strong>{s.FacilityName}</strong> expired on {expiryStr}. Pro features may now be restricted. Renew today to restore full access.";
            ctaColor = "#dc3545";
        }
        else
        {
            var dayLabel = threshold == 1 ? "tomorrow" : $"in {threshold} days";
            subject  = $"Reminder: CourtBook Pro expires {dayLabel}";
            headline = $"Renewal Reminder — {threshold} day{(threshold == 1 ? "" : "s")} left";
            lede     = $"Your CourtBook Pro subscription for <strong>{s.FacilityName}</strong> expires {dayLabel} ({expiryStr}). Renew early and we'll add the remaining days to your new period.";
            ctaColor = threshold <= 3 ? "#dc3545" : (threshold <= 7 ? "#f59f00" : "#0d6efd");
        }

        var renewUrl = $"{(_config["App:BaseUrl"] ?? "https://courtbook-solutions.up.railway.app")}/Subscription/Upgrade";

        var html = $@"<!doctype html>
<html><body style=""font-family:Arial,Helvetica,sans-serif;background:#f5f5f7;padding:24px;color:#212529;"">
  <div style=""max-width:560px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e9ecef;"">
    <div style=""background:{ctaColor};color:#fff;padding:18px 24px;"">
      <div style=""font-size:13px;opacity:.85;letter-spacing:.5px;text-transform:uppercase;"">CourtBook Pro</div>
      <div style=""font-size:22px;font-weight:700;margin-top:4px;"">{headline}</div>
    </div>
    <div style=""padding:24px;line-height:1.55;font-size:15px;"">
      <p style=""margin:0 0 14px;"">Hi {firstName},</p>
      <p style=""margin:0 0 18px;"">{lede}</p>
      <table cellpadding=""0"" cellspacing=""0"" style=""width:100%;margin:0 0 18px;border:1px solid #e9ecef;border-radius:6px;"">
        <tr><td style=""padding:10px 14px;color:#6c757d;width:42%;"">Facility</td><td style=""padding:10px 14px;font-weight:600;"">{s.FacilityName}</td></tr>
        <tr><td style=""padding:10px 14px;color:#6c757d;border-top:1px solid #e9ecef;"">Plan</td><td style=""padding:10px 14px;font-weight:600;border-top:1px solid #e9ecef;"">{planLabel}</td></tr>
        <tr><td style=""padding:10px 14px;color:#6c757d;border-top:1px solid #e9ecef;"">Expires on</td><td style=""padding:10px 14px;font-weight:600;border-top:1px solid #e9ecef;"">{expiryStr}</td></tr>
      </table>
      <p style=""margin:0 0 18px;text-align:center;"">
        <a href=""{renewUrl}"" style=""display:inline-block;background:{ctaColor};color:#fff;text-decoration:none;font-weight:600;padding:12px 22px;border-radius:6px;"">Renew Subscription</a>
      </p>
      <p style=""margin:0 0 6px;color:#6c757d;font-size:13px;"">Need help? Reach our team at <a href=""mailto:{contactEmail}"" style=""color:#0d6efd;"">{contactEmail}</a>{(string.IsNullOrWhiteSpace(contactPhone) ? "" : $" or {contactPhone}")}.</p>
    </div>
    <div style=""background:#f8f9fa;color:#6c757d;font-size:12px;padding:14px 24px;border-top:1px solid #e9ecef;"">
      You're receiving this because you're the registered admin for <strong>{s.FacilityName}</strong> on CourtBook.
    </div>
  </div>
</body></html>";

        var plain = $@"{headline}

Hi {firstName},

Your CourtBook Pro subscription for {s.FacilityName} {(threshold == 0 ? $"expired on {expiryStr}" : $"expires on {expiryStr}")}.

Plan: {planLabel}

Renew here: {renewUrl}

Need help? {contactEmail}{(string.IsNullOrWhiteSpace(contactPhone) ? "" : $" or {contactPhone}")}
";

        return (subject, html, plain);
    }
}
