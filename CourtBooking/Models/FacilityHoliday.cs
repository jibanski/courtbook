using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Models;

/// <summary>
/// A facility-wide, owner-marked holiday date. Used so a "weekend + holidays"
/// rate tier or schedule block can also apply on a manually-marked date that
/// doesn't fall on one of its configured days of week.
/// </summary>
public class FacilityHoliday
{
    public int Id { get; set; }

    /// <summary>The facility owner this holiday applies to (matches Court.OwnerId).</summary>
    [Required]
    public string OwnerId { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    [MaxLength(100)]
    public string? Label { get; set; }
}
