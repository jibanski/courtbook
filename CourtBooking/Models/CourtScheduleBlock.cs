using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Models;

/// <summary>
/// A recurring weekly schedule rule for a court: on the days in <see cref="DaysOfWeek"/>
/// (optionally also on facility holidays), hours in [StartHour, EndHour) default to
/// <see cref="Type"/> instead of the implicit Hourly Rental default.
/// </summary>
public class CourtScheduleBlock
{
    public int Id { get; set; }

    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;

    /// <summary>Comma-separated 3-letter day abbreviations, e.g. "Mon,Tue,Wed,Thu".</summary>
    [Required, MaxLength(40)]
    public string DaysOfWeek { get; set; } = string.Empty;

    /// <summary>When true, this rule also applies on facility holiday dates regardless of day-of-week.</summary>
    public bool IncludeHolidays { get; set; }

    public int StartHour { get; set; }
    public int EndHour { get; set; }

    public BookingType Type { get; set; } = BookingType.HourlyRental;

    /// <summary>When false, this block is paused — resolution skips it as if it didn't exist (falls back to Hourly Rental).</summary>
    public bool IsActive { get; set; } = true;

    // ── Public sign-up (Admin-Hosted Open Play only) ────────────────────────────

    /// <summary>When true, customers can reserve a spot in this Open Play session directly through CourtBook.</summary>
    public bool AllowPublicSignup { get; set; }

    /// <summary>Capacity for the session — only meaningful when <see cref="AllowPublicSignup"/> is true.</summary>
    public int? MaxPlayers { get; set; }

    /// <summary>Price per reserved spot — only meaningful when <see cref="AllowPublicSignup"/> is true.</summary>
    [Range(0, 100000)]
    public decimal? PricePerHead { get; set; }
}
