using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Models;

/// <summary>
/// A customer's reservation of one or more spots in a court's Admin-Hosted Open Play
/// session on a specific date. Unlike <see cref="Booking"/>, this doesn't reserve the
/// court exclusively — many customers can sign up for the same session up to the
/// court's configured <see cref="CourtScheduleBlock.MaxPlayers"/>.
/// </summary>
public class OpenPlaySignup
{
    public int Id { get; set; }

    [Required]
    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;

    /// <summary>Snapshot of the facility name, same denormalization pattern as <see cref="Booking.FacilityName"/>.</summary>
    [MaxLength(100)]
    public string? FacilityName { get; set; }

    /// <summary>Snapshot of the customer's typed name at sign-up time — same reasoning and pattern as
    /// <see cref="Booking.CustomerNameSnapshot"/>.</summary>
    [MaxLength(200)]
    public string? CustomerNameSnapshot { get; set; }

    /// <summary>Always-populated customer name snapshot for direct DB lookup — same reasoning and
    /// pattern as <see cref="Booking.CustomerName"/>.</summary>
    [MaxLength(200)]
    public string? CustomerName { get; set; }

    /// <summary>Snapshot of the court's name at sign-up time, same denormalization pattern as <see cref="Booking.CourtName"/>.</summary>
    [MaxLength(100)]
    public string? CourtName { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    [Required]
    public DateOnly BookingDate { get; set; }

    /// <summary>Matches the covering CourtScheduleBlock's window — the whole block is one joinable session.</summary>
    public int StartHour { get; set; }
    public int EndHour { get; set; }

    [Range(1, 100)]
    public int SpotCount { get; set; } = 1;

    /// <summary>Price per spot at the time of sign-up — a later price change doesn't retroactively alter this.</summary>
    public decimal PricePerHeadSnapshot { get; set; }

    public decimal TotalPrice { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>Free-text names of the other players when SpotCount > 1 — e.g. "Juan, Maria, Pedro".
    /// Only the primary signer has an account/guest record; this just lets the facility know
    /// who's actually showing up for the extra spots.</summary>
    [MaxLength(500)]
    public string? PlayerNames { get; set; }

    public string? PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public string? PaymentProofPath { get; set; }
    public DateTime? PaymentProofSubmittedAt { get; set; }
    public DateTime? PaidAt { get; set; }

    /// <summary>When the facility admin marked this sign-up's payment as refunded (UTC). Null
    /// unless <see cref="PaymentStatus"/> is <see cref="PaymentStatus.Refunded"/> — same pattern
    /// as <see cref="Booking.RefundedAt"/>.</summary>
    public DateTime? RefundedAt { get; set; }

    /// <summary>Peso amount actually returned to the customer — may be less than <see cref="TotalPrice"/>
    /// for a partial refund. Null unless refunded.</summary>
    public decimal? RefundAmount { get; set; }

    /// <summary>Free-text note the admin entered when issuing the refund. Null unless refunded.</summary>
    [MaxLength(300)]
    public string? RefundReason { get; set; }

    /// <summary>The <see cref="Voucher"/> applied at checkout, if any — same pattern as <see cref="Booking.VoucherId"/>.</summary>
    public int? VoucherId { get; set; }

    [MaxLength(30)]
    public string? VoucherCode { get; set; }

    /// <summary>Peso amount deducted from <see cref="TotalPrice"/> by <see cref="VoucherCode"/> — display only, TotalPrice already nets it out.</summary>
    public decimal DiscountAmount { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// For Pending sign-ups: reservation expires at this time if payment is not confirmed.
    /// Allows a 15-minute window for customers to complete payment. Null for Confirmed/Cancelled/Completed sign-ups.
    /// Staff-logged cash sign-ups skip this timer (they're created as Confirmed immediately).
    /// </summary>
    public DateTime? ReservedUntil { get; set; }

    public decimal? CommissionAmount { get; set; }

    /// <summary>Set only for a guest checkout (no login) — the unguessable capability token emailed to
    /// the guest so they can reach this sign-up without an account.</summary>
    public Guid? GuestAccessToken { get; set; }

    /// <summary>
    /// True once the customer has uploaded a payment screenshot. Checks
    /// <see cref="PaymentProofPath"/>, not <see cref="PaymentReference"/> — the reference
    /// number is optional on the submit-proof form, so a customer who leaves it blank must
    /// still be treated as "submitted, awaiting confirmation".
    /// </summary>
    public bool HasPaymentProof => !string.IsNullOrEmpty(PaymentProofPath);

    /// <summary>The Staff account that logged this as a walk-in Open Play sign-up, if any —
    /// null for sign-ups a customer made themselves online/as a guest.</summary>
    public string? LoggedByStaffId { get; set; }

    /// <summary>Snapshot of the logging staff account's name at sign-up time, mirroring
    /// <see cref="Booking.LoggedByStaffName"/>. Null whenever <see cref="LoggedByStaffId"/> is null.</summary>
    [MaxLength(200)]
    public string? LoggedByStaffName { get; set; }
}
