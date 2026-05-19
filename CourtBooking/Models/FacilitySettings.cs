using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

public class FacilitySettings
{
    public int Id { get; set; }

    /// <summary>The admin user who owns this facility's settings.</summary>
    public string? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    /// <summary>URL-friendly identifier, e.g. "greenfield-sports". Used for /sportshub/{slug}.</summary>
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

    // ── Admin Suspension (platform-superadmin only) ───────────────────────────
    /// <summary>
    /// When true, the facility's public pages (/sportshub/{slug}, court listings) are
    /// hidden from customers and the owner sees a suspension banner. Existing
    /// bookings are preserved. Independent from owner-login lockout.
    /// </summary>
    public bool IsSuspended { get; set; }

    public DateTime? SuspendedAt { get; set; }

    [MaxLength(500)]
    public string? SuspendedReason { get; set; }

    // ── Billing Model ─────────────────────────────────────────────────────────
    /// <summary>"Subscription" (default) or "Commission" (2% per confirmed booking).</summary>
    [MaxLength(20)]
    public string BillingModel { get; set; } = "Subscription";

    /// <summary>Commission rate in percent, e.g. 2.0 means 2%. Set by platform admin.</summary>
    [Column(TypeName = "numeric(5,2)")]
    public decimal CommissionRate { get; set; } = 2.0m;

    /// <summary>Accumulated unpaid commission balance (increases on each confirmed booking).</summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal CommissionBalanceOwed { get; set; } = 0m;

    /// <summary>Total commission paid historically.</summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal CommissionTotalPaid { get; set; } = 0m;

    /// <summary>GCash/Maya reference submitted by owner to pay off commission balance.</summary>
    [MaxLength(100)]
    public string? CommissionPaymentRef { get; set; }

    [MaxLength(500)]
    public string? CommissionPaymentProofPath { get; set; }

    public DateTime? CommissionPaymentSubmittedAt { get; set; }

    [NotMapped] public bool IsCommissionModel =>
        string.Equals(BillingModel, "Commission", StringComparison.OrdinalIgnoreCase);

    [NotMapped] public bool IsCommissionPaymentPending =>
        CommissionPaymentSubmittedAt.HasValue && CommissionBalanceOwed > 0;

    // ── Trial ─────────────────────────────────────────────────────────────────
    /// <summary>Length of the free trial in days. Change here to retune.</summary>
    public const int TrialPeriodDays = 30;

    public DateTime? TrialStartedAt { get; set; }
    public bool IsSubscribed { get; set; } = false;

    [NotMapped] public DateTime? TrialExpiresAt    => TrialStartedAt?.AddDays(TrialPeriodDays);
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
    public string DisplayName => EffectiveIsSubscribed && !string.IsNullOrWhiteSpace(BrandName)
        ? BrandName : "CourtBook";

    [NotMapped]
    public string DisplayTagline => EffectiveIsSubscribed && !string.IsNullOrWhiteSpace(BrandTagline)
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

    /// <summary>
    /// True when there's a submitted payment that hasn't been activated yet.
    /// Covers both initial signup (never activated) and renewals (submitted after
    /// the most recent activation).
    /// </summary>
    [NotMapped]
    public bool IsSubscriptionPending => SubscriptionSubmittedAt.HasValue
        && (!SubscriptionActivatedAt.HasValue
            || SubscriptionSubmittedAt.Value > SubscriptionActivatedAt.Value);

    /// <summary>True only for renewal payments waiting to be verified.</summary>
    [NotMapped]
    public bool IsRenewalPending => IsSubscriptionPending && IsSubscribed;

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

    // ── Grace Period ──────────────────────────────────────────────────────────
    // After expiry we keep Pro features unlocked for a short grace window so
    // honest renewers don't lose anything if they pay a few days late. Once
    // grace ends the facility is "Downgraded" — stored IsSubscribed is still
    // true (so renewal preserves history) but Pro cosmetics revert to free.
    public const int GracePeriodDays = 7;

    [NotMapped]
    public DateTime? GraceEndsAt => EffectiveSubscriptionExpiry?.AddDays(GracePeriodDays);

    /// <summary>True between expiry and the end of the grace window. Pro stays on.</summary>
    [NotMapped]
    public bool IsInGracePeriod => IsSubscriptionExpired
        && GraceEndsAt.HasValue
        && DateTime.UtcNow < GraceEndsAt.Value;

    /// <summary>True once grace ends. Pro cosmetics revert (see <see cref="EffectiveIsSubscribed"/>).</summary>
    [NotMapped]
    public bool IsDowngraded => IsSubscriptionExpired
        && GraceEndsAt.HasValue
        && DateTime.UtcNow >= GraceEndsAt.Value;

    [NotMapped]
    public int GraceDaysRemaining => GraceEndsAt.HasValue
        ? Math.Max(0, (int)Math.Ceiling((GraceEndsAt.Value - DateTime.UtcNow).TotalDays))
        : 0;

    [NotMapped]
    public int DaysSinceExpiry => EffectiveSubscriptionExpiry.HasValue
        ? Math.Max(0, (int)Math.Floor((DateTime.UtcNow - EffectiveSubscriptionExpiry.Value).TotalDays))
        : 0;

    /// <summary>
    /// The flag every Pro-gated feature should use instead of <see cref="IsSubscribed"/>.
    /// True while paid (incl. grace); false once the grace window closes.
    /// </summary>
    [NotMapped]
    public bool EffectiveIsSubscribed => IsSubscribed && !IsDowngraded;
}
