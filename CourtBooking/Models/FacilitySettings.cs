using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

public class FacilitySettings
{
    public int Id { get; set; }

    /// <summary>The admin user who owns this facility's settings.</summary>
    public string? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    /// <summary>URL-friendly identifier, e.g. "greenfield-sports". Used for /f/{slug}.</summary>
    [MaxLength(100)]
    public string? Slug { get; set; }

    [MaxLength(100)]
    public string FacilityName { get; set; } = "CourtBook";

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(20)]
    public string? GCashNumber { get; set; }

    [MaxLength(100)]
    public string? GCashName { get; set; }

    [MaxLength(20)]
    public string? MayaNumber { get; set; }

    [MaxLength(100)]
    public string? MayaName { get; set; }

    [MaxLength(500)]
    public string? PaymentInstructions { get; set; }

    // ── Social Media ──────────────────────────────────────────────────────────
    [MaxLength(300)]
    [Url(ErrorMessage = "Please enter a valid Facebook URL (e.g. https://facebook.com/yourpage).")]
    public string? FacebookUrl { get; set; }

    [MaxLength(300)]
    [Url(ErrorMessage = "Please enter a valid Instagram URL (e.g. https://instagram.com/yourpage).")]
    public string? InstagramUrl { get; set; }

    // ── Trial ─────────────────────────────────────────────────────────────────
    public DateTime? TrialStartedAt { get; set; }
    public bool IsSubscribed { get; set; } = false;

    [NotMapped] public DateTime? TrialExpiresAt    => TrialStartedAt?.AddDays(7);
    [NotMapped] public bool      IsTrialActive     => TrialStartedAt.HasValue && DateTime.UtcNow < TrialExpiresAt && !IsSubscribed;
    [NotMapped] public bool      IsTrialExpired    => TrialStartedAt.HasValue && DateTime.UtcNow >= TrialExpiresAt && !IsSubscribed;
    [NotMapped] public int       TrialDaysRemaining => TrialExpiresAt.HasValue
        ? Math.Max(0, (int)Math.Ceiling((TrialExpiresAt.Value - DateTime.UtcNow).TotalDays))
        : 0;

    // ── Custom Branding (Pro only) ────────────────────────────────────────────
    [MaxLength(100)]
    public string? BrandName { get; set; }       // Replaces "CourtBook" site-wide

    [MaxLength(200)]
    public string? BrandTagline { get; set; }    // Short line shown under the name / in footer

    public string? BrandLogoUrl { get; set; }    // Path to uploaded logo image

    [NotMapped]
    public string DisplayName => IsSubscribed && !string.IsNullOrWhiteSpace(BrandName)
        ? BrandName : "CourtBook";

    [NotMapped]
    public string DisplayTagline => IsSubscribed && !string.IsNullOrWhiteSpace(BrandTagline)
        ? BrandTagline : "Book your court anytime, anywhere.";

    // ── Subscription Payment ──────────────────────────────────────────────────
    [MaxLength(20)]
    public string? SubscriptionPlan { get; set; }          // "monthly" | "annual"

    [MaxLength(100)]
    public string? SubscriptionPaymentRef { get; set; }

    [MaxLength(500)]
    public string? SubscriptionProofPath { get; set; }

    public DateTime? SubscriptionSubmittedAt { get; set; }
    public DateTime? SubscriptionActivatedAt { get; set; }
    public DateTime? SubscriptionExpiresAt   { get; set; }

    /// <summary>
    /// Closest expiry-reminder threshold (in days) that has already been
    /// emailed for the current subscription cycle. Cleared on renewal so the
    /// next cycle starts fresh. NULL means no reminder has been sent yet.
    /// </summary>
    public int? LastExpiryReminderThreshold { get; set; }

    [NotMapped] public bool IsSubscriptionPending => SubscriptionSubmittedAt.HasValue && !IsSubscribed;

    /// <summary>
    /// The effective expiry date — uses the stored <see cref="SubscriptionExpiresAt"/> when set,
    /// otherwise computes from <see cref="SubscriptionActivatedAt"/> + plan duration so existing
    /// subscribers from before the column existed still display correctly.
    /// </summary>
    [NotMapped]
    public DateTime? EffectiveSubscriptionExpiry
    {
        get
        {
            if (SubscriptionExpiresAt.HasValue) return SubscriptionExpiresAt;
            if (!SubscriptionActivatedAt.HasValue) return null;
            var days = string.Equals(SubscriptionPlan, "annual", StringComparison.OrdinalIgnoreCase) ? 365 : 30;
            return SubscriptionActivatedAt.Value.AddDays(days);
        }
    }

    [NotMapped]
    public int SubscriptionDaysRemaining => EffectiveSubscriptionExpiry.HasValue
        ? Math.Max(0, (int)Math.Ceiling((EffectiveSubscriptionExpiry.Value - DateTime.UtcNow).TotalDays))
        : 0;

    [NotMapped]
    public bool IsSubscriptionExpired => IsSubscribed
        && EffectiveSubscriptionExpiry.HasValue
        && DateTime.UtcNow >= EffectiveSubscriptionExpiry.Value;

    [NotMapped]
    public bool IsSubscriptionExpiringSoon => IsSubscribed
        && !IsSubscriptionExpired
        && SubscriptionDaysRemaining <= 14;
}
