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
    [Range(1, 16, ErrorMessage = "Duration must be between 1 and 16 hours.")]
    public int DurationHours { get; set; } = 1;

    // When set, duration is fixed by the slot and the dropdown is hidden
    public int? FixedEndHour { get; set; }
    public bool IsSlotBooking => FixedEndHour.HasValue;

    public string? Notes { get; set; }

    // Contact info — required for guests; pre-filled from account for authenticated users.
    public string? GuestName { get; set; }
    public string? GuestEmail { get; set; }
    public string? GuestPhone { get; set; }

    // When set (from the GET action), this is the tier-aware total for a fixed-slot booking
    // and takes precedence over the flat Court.PricePerHour * duration fallback below.
    public decimal? ResolvedSlotTotal { get; set; }

    // FixedEndHour can be 24 (midnight/end-of-day, e.g. an "8pm-12am" slot) — TimeOnly only
    // accepts 0-23, so wrap with % 24 the same way TimeDisplay.Hour does.
    public TimeOnly StartTime => new TimeOnly(StartHour % 24, 0);
    public TimeOnly EndTime   => FixedEndHour.HasValue
        ? new TimeOnly(FixedEndHour.Value % 24, 0)
        : StartTime.AddHours(DurationHours);
    public decimal TotalPrice => ResolvedSlotTotal
        ?? ((Court?.PricePerHour ?? 0) *
            (FixedEndHour.HasValue ? FixedEndHour.Value - StartHour : DurationHours));
}

public class CourtAvailabilityViewModel
{
    public Court Court { get; set; } = null!;
    public DateOnly Date { get; set; }
    public List<int> AvailableHours { get; set; } = new();
    public List<int> BookedHours { get; set; } = new();
    public List<int> PendingHours { get; set; } = new();

    // Pending bundle purchases grouped by start hour so the grid can render each one as a
    // single blocked window instead of separate hourly pending tiles.
    public Dictionary<int, Booking> PendingBundleWindows { get; set; } = new();

    // Fallback-mode blocked hours (admin-marked unavailable, no booking)
    public List<int> BlockedHours { get; set; } = new();

    // Reason text per blocked hour (only populated for CourtBlock range blocks that have a reason)
    public Dictionary<int, string> BlockReasons { get; set; } = new();

    // Slot-based availability (used when court has defined time slots)
    public List<CourtBooking.Models.CourtTimeSlot> TimeSlots { get; set; } = new();
    public List<int> UnavailableSlotIds { get; set; } = new();
    public bool HasSlots => TimeSlots.Any();

    // Hours the recurring weekly schedule reserves for Admin-Hosted Open Play
    // (fallback hourly-grid mode only — not directly bookable by a customer).
    public List<int> OpenPlayHours { get; set; } = new();

    // Resolved per-hour rate (tiered if a CourtRateTier matches, else Court.PricePerHour) —
    // fallback hourly-grid mode only.
    public Dictionary<int, decimal> HourlyRates { get; set; } = new();

    // Tier-aware total price per pre-defined CourtTimeSlot on this date, keyed by slot Id.
    // Populated only when TimeSlots is non-empty (slot mode).
    public Dictionary<int, decimal> SlotPrices { get; set; } = new();

    // Hours sellable only as part of a flat-price multi-court bundle — not directly bookable.
    // Keyed by hour; value is the covering bundle + rate block (for the "Book This Bundle" link).
    public Dictionary<int, (CourtBundle Bundle, CourtBundleRateBlock Block)> BundleOnlyHours { get; set; } = new();

    // Open Play hours where the owner has enabled public sign-up. Keyed by hour; value is
    // the covering schedule block + live spots-remaining count (for the "Join Open Play" link).
    public Dictionary<int, (CourtScheduleBlock Block, int SpotsRemaining)> OpenPlaySignupInfo { get; set; } = new();

    // Display-only rate range spanning the court's base rate and any rate tiers (e.g. ₱250–350/hr).
    // Equal when there are no tiers — the view falls back to showing a single price.
    public decimal RateRangeMin { get; set; }
    public decimal RateRangeMax { get; set; }
    public bool HasRateRange => RateRangeMax > RateRangeMin;
}
