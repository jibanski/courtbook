using CourtBooking.Models;

namespace CourtBooking.ViewModels;

/// <summary>
/// Unified row for the Admin "All Bookings" table so regular/bundle bookings and
/// Open Play sign-ups — two separate entities — can be listed and filtered together.
/// </summary>
public class AdminBookingRow
{
    public int Id { get; set; }
    public bool IsOpenPlay { get; set; }
    public string CustomerName { get; set; } = "";
    public string? CustomerPhone { get; set; }
    public bool IsGuest { get; set; }
    public string CourtName { get; set; } = "";
    public string? BundleName { get; set; }
    public int? SpotCount { get; set; }
    public DateOnly BookingDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>When this booking/sign-up was submitted (UTC) — distinct from <see cref="BookingDate"/>,
    /// which is the court's reserved date, not when the reservation itself was made.</summary>
    public DateTime CreatedAt { get; set; }
    public decimal TotalPrice { get; set; }
    public BookingStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public bool HasPaymentProof { get; set; }

    /// <summary>Name of the Staff account that logged this as a walk-in booking, if any — null for
    /// bookings a customer made themselves online/as a guest.</summary>
    public string? BookedByStaffName { get; set; }

    /// <summary>Combined cost of any rented add-ons (already included in <see cref="TotalPrice"/>) —
    /// zero for Open Play sign-ups, which don't support add-ons.</summary>
    public decimal AddOnsTotal { get; set; }

    /// <summary>Human-readable "2x Paddle Rental, 1x Shuttlecock" summary for a tooltip — null when there are none.</summary>
    public string? AddOnsSummary { get; set; }
}
