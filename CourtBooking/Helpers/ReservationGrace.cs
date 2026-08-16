namespace CourtBooking.Helpers;

/// <summary>
/// A customer who already paid can still lose their slot if a proof-of-payment upload
/// attempt fails part-way (corrupt/blank screenshot, flaky mobile connection, etc.) while
/// the original 15-minute <c>ReservedUntil</c> hold keeps ticking down in the background —
/// <see cref="Services.ReservationExpiryCleanupService"/> then cancels the booking and frees
/// the slot for someone else before the customer ever gets a working attempt in. Call
/// <see cref="ExtendOnAttempt"/> whenever a customer reaches the proof-submission action
/// (whether that attempt ultimately succeeds or fails) so a slow/flaky retry always has a
/// fresh window, bounded by an absolute cap so a hold can't be extended forever.
/// </summary>
public static class ReservationGrace
{
    private static readonly TimeSpan ExtensionPerAttempt = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaxHoldFromCreation = TimeSpan.FromMinutes(60);

    /// <summary>Returns the new ReservedUntil to persist, or the original value if there's
    /// nothing to extend (already cleared, or the absolute cap has already been reached).</summary>
    public static DateTime? ExtendOnAttempt(DateTime createdAt, DateTime? currentReservedUntil)
    {
        if (currentReservedUntil is null) return null;

        var cap = createdAt.Add(MaxHoldFromCreation);
        var extended = DateTime.UtcNow.Add(ExtensionPerAttempt);
        return extended < cap ? extended : cap;
    }
}
