using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

public enum AdminFeeType
{
    Fixed,
    Percentage
}

/// <summary>
/// One row per qualifying booking/sign-up that accrued a platform admin fee. This is the
/// ledger of record — <see cref="FacilitySettings"/> balances are computed from these rows
/// rather than cached, so the outstanding balance is always explainable transaction-by-transaction.
/// </summary>
public class AdminFeeCharge
{
    public int Id { get; set; }

    /// <summary>The facility owner this charge is payable by. Denormalized for fast per-owner queries.</summary>
    [Required]
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The representative/primary booking this charge was raised for. When
    /// <see cref="BundleGroupId"/> is set, this charge actually covers every booking sharing that
    /// group (one bundle purchase or multi-court cart checkout confirmed together) — this FK just
    /// points at the first of them, for display/reference.</summary>
    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    /// <summary>Set when this charge covers a whole group of bookings (<see cref="Booking.BundleGroupId"/>)
    /// confirmed together as one transaction, rather than a single standalone booking — so a Fixed
    /// fee is charged once per checkout, not once per court/row.</summary>
    public Guid? BundleGroupId { get; set; }

    public int? OpenPlaySignupId { get; set; }
    public OpenPlaySignup? OpenPlaySignup { get; set; }

    /// <summary>Current amount owed by this charge — shrinks if the underlying booking is
    /// (partially) refunded. Never goes below zero.</summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>Snapshot of the amount at accrual time — never changes, kept for audit clarity.</summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal OriginalAmount { get; set; }

    /// <summary>Snapshot of the facility's fee configuration at the moment this charge accrued —
    /// so a later change to <see cref="FacilitySettings.AdminFeeType"/>/rate/fixed-amount never
    /// retroactively alters historical charges.</summary>
    public AdminFeeType FeeTypeSnapshot { get; set; }
    [Column(TypeName = "numeric(5,2)")]
    public decimal? FeeRateSnapshot { get; set; }
    [Column(TypeName = "numeric(18,2)")]
    public decimal? FeeFixedAmountSnapshot { get; set; }

    public DateTime AccruedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set the first time a refund reduces this charge's <see cref="Amount"/>.</summary>
    public DateTime? ReversedAt { get; set; }

    /// <summary>Set once this charge has been included in a settlement submission — cleared again
    /// if that settlement is rejected, so the charge becomes outstanding again.</summary>
    public int? SettlementId { get; set; }
    public AdminFeeSettlement? Settlement { get; set; }
}
