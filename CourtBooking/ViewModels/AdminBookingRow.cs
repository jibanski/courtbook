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
    public int CourtId { get; set; }
    public string CourtName { get; set; } = "";
    public string? BundleName { get; set; }

    /// <summary>Shared by every court row purchased together as one bundle package — lets the UI
    /// offer a group-aware reschedule that moves every court in the package together instead of
    /// desyncing them. Null for Open Play sign-ups and non-bundle bookings.</summary>
    public Guid? BundleGroupId { get; set; }
    public int? SpotCount { get; set; }

    /// <summary>Open Play only: free-text names of the other players when SpotCount &gt; 1 — e.g.
    /// "Juan, Maria, Pedro". Null for a single-spot sign-up or a regular court booking.</summary>
    public string? PlayerNames { get; set; }
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
    public string? PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }

    /// <summary>When this row was actually marked Paid (UTC), null while still unpaid — this is the
    /// date Admin/Staff Analytics buckets revenue/status/staff/court breakdowns by (falling back to
    /// <see cref="BookingDate"/> only when null), which can differ from both <see cref="BookingDate"/>
    /// and <see cref="CreatedAt"/> (e.g. a booking paid in advance for a future slot, or one created
    /// Pending and confirmed by staff on a later day).</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Name of the Staff account that logged this as a walk-in booking, if any — null for
    /// bookings a customer made themselves online/as a guest.</summary>
    public string? BookedByStaffName { get; set; }

    /// <summary>Combined cost of any rented add-ons (already included in <see cref="TotalPrice"/>) —
    /// zero for Open Play sign-ups, which don't support add-ons.</summary>
    public decimal AddOnsTotal { get; set; }

    /// <summary>Human-readable "2x Paddle Rental, 1x Shuttlecock" summary for a tooltip — null when there are none.</summary>
    public string? AddOnsSummary { get; set; }

    /// <summary>Path to the uploaded payment proof screenshot, if any.</summary>
    public string? PaymentProofPath { get; set; }
}
