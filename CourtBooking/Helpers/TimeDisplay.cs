namespace CourtBooking.Helpers;

/// <summary>Formats hour-of-day integers (0-23, or 24 for end-of-day) as 12-hour clock strings.</summary>
public static class TimeDisplay
{
    public static string Hour(int hour) => new TimeOnly(hour % 24, 0).ToString("h:mm tt");

    public static string HourRange(int startHour, int endHour) => $"{Hour(startHour)} – {Hour(endHour)}";
}
