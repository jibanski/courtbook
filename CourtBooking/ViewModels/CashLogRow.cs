namespace CourtBooking.ViewModels;

/// <summary>
/// Unified row for the Cash Log reconciliation reports (staff's own log + the owner's all-staff
/// view) so a regular court <c>Booking</c> and an Open Play <c>OpenPlaySignup</c> — two separate
/// entities — can be listed and totalled together. Mirrors <see cref="AdminBookingRow"/>'s
/// merge-two-entities pattern used on the "All Bookings" page.
/// </summary>
public class CashLogRow
{
    public int Id { get; set; }
    public bool IsOpenPlay { get; set; }

    /// <summary>True for a standalone <c>AddOnRental</c> — an add-on-only counter sale with no
    /// court/Open Play attached. <see cref="StartTime"/>/<see cref="EndTime"/> aren't meaningful
    /// for these rows (no time slot), views should hide the Time column for them instead.</summary>
    public bool IsAddOnOnly { get; set; }
    public DateOnly BookingDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string CourtName { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public int? SpotCount { get; set; }

    /// <summary>Open Play only: free-text names of the other players when SpotCount &gt; 1.</summary>
    public string? PlayerNames { get; set; }

    /// <summary>Court/session rate only — excludes add-ons (Open Play rows never have any).</summary>
    public decimal CourtRental { get; set; }
    public decimal AddOnsTotal { get; set; }
    public string? AddOnsSummary { get; set; }
    public decimal TotalPrice { get; set; }

    /// <summary>Cash, GCash, Maya, or GoTyme — lets the reconciliation views split totals by method
    /// instead of assuming everything logged here was handed over as physical cash.</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>Confirmed (cash, or a digital payment the owner has verified) vs. Pending (a digital
    /// payment claim that's still awaiting owner confirmation) — views must surface this so an
    /// unconfirmed sale is never mistaken for money already collected.</summary>
    public CourtBooking.Models.BookingStatus Status { get; set; }

    public string? LoggedByStaffId { get; set; }
    public DateTime CreatedAt { get; set; }
}
