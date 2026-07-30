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

    public string? LoggedByStaffId { get; set; }
    public DateTime CreatedAt { get; set; }
}
