using System.ComponentModel.DataAnnotations;
using CourtBooking.Models;

namespace CourtBooking.ViewModels;

public class BookingViewModel
{
    public int CourtId { get; set; }
    public Court? Court { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateOnly BookingDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required]
    public int StartHour { get; set; }

    [Required]
    [Range(1, 12, ErrorMessage = "Duration must be at least 1 hour.")]
    public int DurationHours { get; set; } = 1;

    // When set, duration is fixed by the slot and the dropdown is hidden
    public int? FixedEndHour { get; set; }
    public bool IsSlotBooking => FixedEndHour.HasValue;

    public string? Notes { get; set; }

    public TimeOnly StartTime => new TimeOnly(StartHour, 0);
    public TimeOnly EndTime   => FixedEndHour.HasValue
        ? new TimeOnly(FixedEndHour.Value, 0)
        : StartTime.AddHours(DurationHours);
    public decimal TotalPrice => (Court?.PricePerHour ?? 0) *
        (FixedEndHour.HasValue ? FixedEndHour.Value - StartHour : DurationHours);
}

public class CourtAvailabilityViewModel
{
    public Court Court { get; set; } = null!;
    public DateOnly Date { get; set; }
    public List<int> AvailableHours { get; set; } = new();
    public List<int> BookedHours { get; set; } = new();

    // Fallback-mode blocked hours (admin-marked unavailable, no booking)
    public List<int> BlockedHours { get; set; } = new();

    // Slot-based availability (used when court has defined time slots)
    public List<CourtBooking.Models.CourtTimeSlot> TimeSlots { get; set; } = new();
    public List<int> UnavailableSlotIds { get; set; } = new();
    public bool HasSlots => TimeSlots.Any();
}
