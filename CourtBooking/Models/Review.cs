using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

/// <summary>
/// A testimonial submitted by a facility owner about CourtBook itself.
/// Approved + featured reviews are rendered on the public landing page.
/// </summary>
public class Review
{
    public int Id { get; set; }

    /// <summary>The facility-owner user who submitted this review.</summary>
    [Required, MaxLength(450)]
    public string OwnerId { get; set; } = string.Empty;

    public ApplicationUser? Owner { get; set; }

    /// <summary>Snapshot of the owner's display name at submit time.</summary>
    [Required, MaxLength(120)]
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>Snapshot of the facility name at submit time, e.g. "Inayawan Sports Complex - Cebu".</summary>
    [Required, MaxLength(160)]
    public string FacilityName { get; set; } = string.Empty;

    /// <summary>Star rating, 1–5.</summary>
    [Range(1, 5)]
    public int Rating { get; set; }

    /// <summary>Optional short headline shown above the testimonial body.</summary>
    [MaxLength(120)]
    public string? Title { get; set; }

    /// <summary>Main testimonial text.</summary>
    [Required, MaxLength(600)]
    public string Body { get; set; } = string.Empty;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Platform admin has reviewed this and marked it OK to keep.</summary>
    public bool IsApproved { get; set; }

    public DateTime? ApprovedAt { get; set; }

    /// <summary>Subset of approved — actually shown on the public landing page.</summary>
    public bool IsFeatured { get; set; }

    /// <summary>Lower = appears earlier on the landing page. Ties broken by SubmittedAt.</summary>
    public int DisplayOrder { get; set; }
}
