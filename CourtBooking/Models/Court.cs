using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Models;

public class Court
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required, MaxLength(50)]
    public string SportType { get; set; } = string.Empty;

    [Range(0, 100000)]
    public decimal PricePerHour { get; set; }

    public bool IsIndoor { get; set; }
    public bool IsActive { get; set; } = true;

    public string? ImageUrl { get; set; }

    public int OpeningHour { get; set; } = 0;
    public int ClosingHour { get; set; } = 24;

    /// <summary>The admin user who created/owns this court.</summary>
    public string? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    /// <summary>
    /// Snapshot of the facility (owner) name. Denormalized onto the court so it
    /// can be attributed to a facility directly in the database, without joining
    /// through Owner → FacilitySettings.
    /// </summary>
    [MaxLength(100)]
    public string? FacilityName { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<CourtTimeSlot> TimeSlots { get; set; } = new List<CourtTimeSlot>();
}
