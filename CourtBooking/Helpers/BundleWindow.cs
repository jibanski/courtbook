using CourtBooking.Models;

namespace CourtBooking.Helpers;

/// <summary>
/// Auto-shrinks a bundle-only rate block's start hour (and pro-rates its flat price) once part of
/// the configured window has already elapsed today — e.g. a 1pm-4pm window still sells as 2pm-4pm
/// at 2/3 price once it's already past 1pm, instead of the whole window becoming unsellable.
/// </summary>
public static class BundleWindow
{
    /// <summary>Returns the effective start hour and pro-rated price for <paramref name="block"/> on
    /// <paramref name="date"/>, or null if nothing bookable remains (the whole block has elapsed).
    /// Uses the same 20-minute advance-booking grace period as ordinary hourly slots.</summary>
    public static (int EffectiveStartHour, decimal EffectivePrice)? Resolve(CourtBundleRateBlock block, DateOnly date)
    {
        var today = PhtClock.Today;
        if (date < today) return null;

        int effectiveStart = block.StartHour;
        if (date == today)
        {
            var localNow = PhtClock.Now;
            while (effectiveStart < block.EndHour && (effectiveStart * 60 + 20) < (localNow.Hour * 60 + localNow.Minute))
                effectiveStart++;
        }
        if (effectiveStart >= block.EndHour) return null;

        var originalHours   = block.EndHour - block.StartHour;
        var remainingHours  = block.EndHour - effectiveStart;
        var price = originalHours > 0
            ? Math.Round(block.FlatPrice * remainingHours / originalHours, 2)
            : block.FlatPrice;
        return (effectiveStart, price);
    }
}
