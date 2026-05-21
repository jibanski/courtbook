using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Models;

public enum BookingStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Completed
}

public enum PaymentStatus
{
    Unpaid,
    Paid,
    Refunded
}

public class Booking
{
    public int Id { get; set; }

    [Required]
    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    [Required]
    public DateOnly BookingDate { get; set; }

    [Required]
    public TimeOnly StartTime { get; set; }

    [Required]
    public TimeOnly EndTime { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    public decimal TotalPrice { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public string? PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public string? PaymentProofPath { get; set; }
    public DateTime? PaymentProofSubmittedAt { get; set; }
    public DateTime? PaidAt { get; set; }

    /// <summary>PayMongo checkout session ID when the customer chose to pay by card.</summary>
    public string? CheckoutSessionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Platform commission charged when this booking is confirmed (commission-model facilities only).</summary>
    public decimal? CommissionAmount { get; set; }

    /// <summary>True once the owner has paid off this booking's commission.</summary>
    public bool CommissionPaid { get; set; } = false;

    public bool HasPaymentProof => !string.IsNullOrEmpty(PaymentReference);
    public double DurationHours => (EndTime - StartTime).TotalHours;
}
