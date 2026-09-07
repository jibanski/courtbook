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

    /// <summary>
    /// Real duration-aware end hour for an overnight-spanning booking. <c>end &lt;= start</c> is
    /// otherwise impossible for a valid forward-time booking, so it unambiguously signals the
    /// booking continues into the next calendar day (e.g. Start=22:00, End=03:00 -&gt; 27,
    /// covering a court open past midnight). Also covers the pre-existing "ends exactly at
    /// midnight" convention (End=00:00 -&gt; 24).
    /// </summary>
    public static int WrapAwareEndHour(TimeOnly start, TimeOnly end) => end <= start ? end.Hour + 24 : end.Hour;

    /// <summary>
    /// The real calendar date a grid selection actually starts on, given the "view date" the
    /// customer was browsing and a possibly-virtual start hour (&gt;=24 meaning the slot is
    /// entirely after midnight, only reachable as the tail of an overnight court's operating
    /// window). Pair with <c>startHour % 24</c>/<c>endHour % 24</c> for the TimeOnly values.
    /// </summary>
    public static DateOnly ResolveBookingDate(DateOnly viewDate, int startHour) => viewDate.AddDays(startHour / 24);
}
