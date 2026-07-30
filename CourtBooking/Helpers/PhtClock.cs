namespace CourtBooking.Helpers;

/// <summary>
/// The current date/time in Philippine Standard Time (UTC+8) — use this instead of
/// <c>DateTime.Now</c>/<c>DateTime.Today</c> everywhere "today" or "now" means "today/now
/// for a court in the Philippines". The server's own OS timezone (e.g. UTC on most cloud
/// hosts) doesn't match PHT, so <c>DateTime.Today</c> silently resolves to the wrong
/// calendar date during PHT midnight–8am — this centralizes the one correct calculation.
/// </summary>
public static class PhtClock
{
    public static DateTime Now => DateTime.UtcNow.AddHours(8);
    public static DateOnly Today => DateOnly.FromDateTime(Now);
}
