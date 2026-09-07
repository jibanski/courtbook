namespace CourtBooking.Helpers;

/// <summary>Formats hour-of-day integers (0-23, or 24 for end-of-day) as 12-hour clock strings.</summary>
public static class TimeDisplay
{
    public static string Hour(int hour) => new TimeOnly(hour % 24, 0).ToString("h:mm tt");

    public static string HourRange(int startHour, int endHour) => $"{Hour(startHour)} – {Hour(endHour)}";

    /// <summary>
    /// Safe replacement for <c>Enumerable.Range(startHour, endHour - startHour)</c>. Misconfigured
    /// court/schedule data (e.g. a closing hour entered before the opening hour) makes that count
    /// negative, which throws <see cref="ArgumentOutOfRangeException"/> and 500s the page. Here we
    /// just treat an invalid/empty range as "no hours" instead of crashing.
    /// </summary>
    public static IEnumerable<int> HourSequence(int startHour, int endHour) =>
        endHour > startHour ? Enumerable.Range(startHour, endHour - startHour) : Enumerable.Empty<int>();
}
