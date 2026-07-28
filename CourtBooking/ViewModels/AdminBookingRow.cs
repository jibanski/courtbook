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
    public decimal TotalPrice { get; set; }
    public BookingStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public bool HasPaymentProof { get; set; }
}
