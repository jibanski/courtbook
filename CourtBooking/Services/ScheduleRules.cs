using CourtBooking.Models;

namespace CourtBooking.Services;

/// <summary>
/// Pure resolution logic shared by <see cref="CourtRateTier"/> and
/// <see cref="CourtScheduleBlock"/> day-matching and hour lookups.
/// </summary>
public static class ScheduleRules
{
    /// <summary>True when <paramref name="date"/>'s day-of-week is listed in <paramref name="daysCsv"/>.</summary>
    public static bool DayOfWeekMatches(string daysCsv, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(daysCsv)) return false;

        var abbrev = date.DayOfWeek.ToString()[..3];
        return daysCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(d => string.Equals(d, abbrev, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the rule that applies to <paramref name="hour"/> on <paramref name="date"/>. On a holiday, a rule
    /// marked "include holidays" takes precedence over one that only matches by day-of-week — otherwise a
    /// holiday that happens to fall on, say, a Wednesday would keep using that Wednesday's own rule instead of
    /// the holiday override.
    /// </summary>
    private static T? ResolveMatch<T>(
        IEnumerable<T> rules, DateOnly date, bool isHoliday, int hour,
        Func<T, int> startHour, Func<T, int> endHour, Func<T, string> daysOfWeek, Func<T, bool> includeHolidays)
        where T : class
    {
        var inRange = rules.Where(r => hour >= startHour(r) && hour < endHour(r)).ToList();

        if (isHoliday)
        {
            var holidayMatch = inRange.FirstOrDefault(includeHolidays);
            if (holidayMatch is not null) return holidayMatch;
        }

        return inRange.FirstOrDefault(r => DayOfWeekMatches(daysOfWeek(r), date));
    }

    public static decimal ResolveHourlyRate(IEnumerable<CourtRateTier> tiers, decimal fallback, DateOnly date, bool isHoliday, int hour)
    {
        var match = ResolveMatch(tiers, date, isHoliday, hour,
            t => t.StartHour, t => t.EndHour, t => t.DaysOfWeek, t => t.IncludeHolidays);
        return match?.PricePerHour ?? fallback;
    }

    /// <summary>The schedule block (if any) covering this hour — lets callers read its Type as well as
    /// Open Play sign-up settings (AllowPublicSignup/MaxPlayers/PricePerHead).</summary>
    public static CourtScheduleBlock? ResolveScheduleBlock(IEnumerable<CourtScheduleBlock> blocks, DateOnly date, bool isHoliday, int hour) =>
        ResolveMatch(blocks, date, isHoliday, hour,
            b => b.StartHour, b => b.EndHour, b => b.DaysOfWeek, b => b.IncludeHolidays);

    public static BookingType ResolveBookingType(IEnumerable<CourtScheduleBlock> blocks, DateOnly date, bool isHoliday, int hour) =>
        ResolveScheduleBlock(blocks, date, isHoliday, hour)?.Type ?? BookingType.HourlyRental;

    /// <summary>Sums the resolved per-hour rate across [start, end) — handles a booking that spans a tier boundary.</summary>
    public static decimal ResolveTotalPrice(IEnumerable<CourtRateTier> tiers, decimal fallback, DateOnly date, bool isHoliday, TimeOnly start, TimeOnly end)
    {
        var tierList = tiers as IList<CourtRateTier> ?? tiers.ToList();
        decimal total = 0;
        // end.Hour is 0 when endHour==24 (midnight) due to TimeOnly wrapping — treat 0 as 24
        int endHour = end.Hour == 0 ? 24 : end.Hour;
        for (int h = start.Hour; h < endHour; h++)
            total += ResolveHourlyRate(tierList, fallback, date, isHoliday, h);
        return total;
    }

    /// <summary>The bundle rate block (if any) that makes an hour sellable only as part of a flat-price bundle.</summary>
    public static CourtBundleRateBlock? ResolveBundleRateBlock(IEnumerable<CourtBundleRateBlock> blocks, DateOnly date, bool isHoliday, int hour) =>
        ResolveMatch(blocks, date, isHoliday, hour,
            b => b.StartHour, b => b.EndHour, b => b.DaysOfWeek, b => b.IncludeHolidays);
}
