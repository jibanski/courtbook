using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

public enum AdminFeeSettlementStatus
{
    Submitted,
    Verified,
    Rejected
}

/// <summary>
/// One row per payment a Facility Owner submits to settle their outstanding admin-fee balance.
/// Doubles as the verification audit record — each settlement is verified/rejected exactly once,
/// so <see cref="VerifiedByName"/>/<see cref="VerifiedAt"/> already capture who/when/what changed.
/// </summary>
public class AdminFeeSettlement
{
    public int Id { get; set; }

    [Required]
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The full outstanding balance at the moment this settlement was submitted.</summary>
    [Column(TypeName = "numeric(18,2)")]
    public decimal Amount { get; set; }

    [MaxLength(100)]
    public string? PaymentReference { get; set; }

    [MaxLength(500)]
    public string? ProofPath { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public AdminFeeSettlementStatus Status { get; set; } = AdminFeeSettlementStatus.Submitted;

    public DateTime? VerifiedAt { get; set; }

    /// <summary>Free-text name of whoever verified/rejected this settlement. The platform-admin
    /// tool is password-gated rather than tied to a logged-in account, so there's no user id to
    /// reference — same reasoning as <see cref="Booking.RescheduledByName"/>.</summary>
    [MaxLength(200)]
    public string? VerifiedByName { get; set; }

    [MaxLength(300)]
    public string? RejectionReason { get; set; }

    public ICollection<AdminFeeCharge> Charges { get; set; } = new List<AdminFeeCharge>();
}
