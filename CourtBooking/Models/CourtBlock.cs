using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Models;

/// <summary>
/// A date/time range during which a court is unavailable for booking.
/// Covers all hours between StartDate/StartHour and EndDate/EndHour (exclusive).
/// </summary>
public class CourtBlock
{
    public int Id { get; set; }
    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public DateOnly StartDate { get; set; }
    public int StartHour { get; set; }          // 0–23

    public DateOnly EndDate { get; set; }
    public int EndHour { get; set; }            // 1–24

    [MaxLength(300)]
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Computed helpers ──────────────────────────────────────────────────────
    public string StartLabel => $"{StartDate:MMM d, yyyy} {StartHour:D2}:00";
    public string EndLabel   => $"{EndDate:MMM d, yyyy} {EndHour:D2}:00";

    /// <summary>True when the block fully covers a single calendar date.</summary>
    public bool IsSingleDay => StartDate == EndDate;

    /// <summary>Hours blocked on a specific date by this range block.</summary>
    public (int From, int To) HoursOn(DateOnly date)
    {
        if (date < StartDate || date > EndDate) return (0, 0);
        int from = date == StartDate ? StartHour : 0;
        int to   = date == EndDate   ? EndHour   : 24;
        return (from, to);
    }
}
