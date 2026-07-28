using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

/// <summary>
/// A recurring weekly window during which a <see cref="CourtBundle"/> is sellable as a
/// single flat-price package (not a per-hour rate) covering all its member courts at once.
/// </summary>
public class CourtBundleRateBlock
{
    public int Id { get; set; }

    public int CourtBundleId { get; set; }
    public CourtBundle CourtBundle { get; set; } = null!;

    /// <summary>Comma-separated 3-letter day abbreviations, e.g. "Fri,Sat,Sun".</summary>
    [Required, MaxLength(40)]
    public string DaysOfWeek { get; set; } = string.Empty;

    /// <summary>When true, this window also applies on facility holiday dates regardless of day-of-week.</summary>
    public bool IncludeHolidays { get; set; }

    public int StartHour { get; set; }
    public int EndHour { get; set; }

    /// <summary>Flat price for the whole window, covering every member court together.</summary>
    [Column(TypeName = "numeric(10,2)")]
    [Range(0, 1000000)]
    public decimal FlatPrice { get; set; }

    public bool IsActive { get; set; } = true;
}
