using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

/// <summary>
/// A standalone add-on rental (e.g. paddles) a staff member logs for a customer who isn't
/// booking a court or joining Open Play — just renting equipment at the counter. Mirrors the
/// snapshot/payment-lifecycle patterns of <see cref="Booking"/> and <see cref="OpenPlaySignup"/>,
/// minus anything slot/time-related since no court resource is being held.
/// </summary>
public class AddOnRental
{
    public int Id { get; set; }

    /// <summary>The Admin (facility owner) this sale belongs to — same idiom as <c>Court.OwnerId</c>.</summary>
    [Required, MaxLength(450)]
    public string OwnerId { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    /// <summary>Snapshot of the customer's typed name at sale time — same reasoning as
    /// <see cref="Booking.CustomerNameSnapshot"/>.</summary>
    [MaxLength(200)]
    public string? CustomerNameSnapshot { get; set; }

    [Column(TypeName = "numeric(10,2)")]
    public decimal TotalPrice { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    public string? PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public string? PaymentProofPath { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? PaidAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The Staff account that logged this sale — every sale is staff-logged, there's no
    /// customer-facing self-service flow for a standalone add-on rental.</summary>
    public string? LoggedByStaffId { get; set; }

    /// <summary>Snapshot of the logging staff account's name at sale time, mirroring
    /// <see cref="Booking.LoggedByStaffName"/>.</summary>
    [MaxLength(200)]
    public string? LoggedByStaffName { get; set; }

    public bool HasPaymentProof => !string.IsNullOrEmpty(PaymentProofPath);

    public ICollection<AddOnRentalItem> Items { get; set; } = new List<AddOnRentalItem>();
}
