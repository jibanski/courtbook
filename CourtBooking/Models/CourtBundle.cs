using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Models;

/// <summary>
/// A facility owner's flat-price package of two or more of their own courts, sold as a
/// single "book everything together" reservation instead of per-court hourly rental.
/// </summary>
public class CourtBundle
{
    public int Id { get; set; }

    /// <summary>The facility owner this bundle belongs to (matches Court.OwnerId).</summary>
    [Required]
    public string OwnerId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<CourtBundleCourt> Courts { get; set; } = new List<CourtBundleCourt>();
}
