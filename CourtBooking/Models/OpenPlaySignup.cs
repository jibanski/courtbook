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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

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
}
