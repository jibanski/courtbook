using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

/// <summary>
/// A recurring weekly rate rule for a court: on the days in <see cref="DaysOfWeek"/>
/// (optionally also on facility holidays), hours in [StartHour, EndHour) are priced
/// at <see cref="PricePerHour"/> instead of the court's flat <c>PricePerHour</c>.
/// </summary>
public class CourtRateTier
{
    public int Id { get; set; }

    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;

    /// <summary>Comma-separated 3-letter day abbreviations, e.g. "Mon,Tue,Wed,Thu".</summary>
    [Required, MaxLength(40)]
    public string DaysOfWeek { get; set; } = string.Empty;

    /// <summary>When true, this tier also applies on facility holiday dates regardless of day-of-week.</summary>
    public bool IncludeHolidays { get; set; }

    public int StartHour { get; set; }
    public int EndHour { get; set; }

    [Column(TypeName = "numeric(10,2)")]
    [Range(0, 100000)]
    public decimal PricePerHour { get; set; }
}
