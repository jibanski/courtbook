using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

/// <summary>
/// A payment the platform admin recorded by hand for a facility — e.g. an initial payment received
/// outside the app (GCash/bank transfer/cash). It acts as credit against the facility's admin fee:
/// outstanding = unsettled fees − available credit, and any excess carries forward to future fees.
/// Never edited or deleted; a mistake is corrected by voiding it, which keeps the history intact.
/// </summary>
public class AdminFeePayment
{
    public int Id { get; set; }

    [Required]
    public string OwnerId { get; set; } = string.Empty;

    [Column(TypeName = "numeric(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>When the money was actually received (may be earlier than <see cref="CreatedAt"/>).</summary>
    public DateTime PaidOn { get; set; } = DateTime.UtcNow;

    [MaxLength(300)]
    public string? Note { get; set; }

    /// <summary>Free-text name of whoever recorded it — the platform-admin tool is password-gated,
    /// not tied to a logged-in account (same reasoning as <see cref="AdminFeeSettlement.VerifiedByName"/>).</summary>
    [MaxLength(200)]
    public string RecordedByName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsVoided { get; set; }
    public DateTime? VoidedAt { get; set; }

    [MaxLength(200)]
    public string? VoidedByName { get; set; }

    [MaxLength(300)]
    public string? VoidReason { get; set; }
}
