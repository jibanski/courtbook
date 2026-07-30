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

    /// <summary>
    /// Snapshot of the facility (court owner) name at booking time. Denormalized
    /// onto the booking so each row can be attributed to a facility directly in
    /// the database, without joining through Court → Owner → FacilitySettings.
    /// </summary>
    [MaxLength(100)]
    public string? FacilityName { get; set; }

    /// <summary>
    /// Snapshot of the customer's typed name at booking time (guest checkout / staff walk-in only).
    /// A guest/walk-in shadow <see cref="ApplicationUser"/> is reused across visits by matching email
    /// or phone, and its FirstName/LastName get overwritten on every reuse — without this snapshot,
    /// an old booking would silently start displaying whatever name a later, unrelated visit typed
    /// under the same contact info. Null for a real logged-in customer's own booking, where the
    /// live <see cref="User"/>.FullName is accurate and safe to show.
    /// </summary>
    [MaxLength(200)]
    public string? CustomerNameSnapshot { get; set; }

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

    /// <summary>The Staff account that logged this as a cash walk-in booking (<see cref="PaymentMethod"/>
    /// = "Cash"). Null for every online/guest-checkout booking.</summary>
    public string? LoggedByStaffId { get; set; }

    /// <summary>Rented add-on items (e.g. paddles) attached to this booking. <see cref="TotalPrice"/>
    /// already includes their cost — this collection is for itemized display only.</summary>
    public ICollection<BookingAddOn> AddOns { get; set; } = new List<BookingAddOn>();

    /// <summary>Platform commission charged when this booking is confirmed (commission-model facilities only).</summary>
    public decimal? CommissionAmount { get; set; }

    /// <summary>True once the owner has paid off this booking's commission.</summary>
    public bool CommissionPaid { get; set; } = false;

    /// <summary>
    /// Set when this row is one court's share of a bundled multi-court booking. <see cref="TotalPrice"/>
    /// is this row's even split of the bundle's flat price, not this court's normal rate.
    /// </summary>
    public int? CourtBundleId { get; set; }
    public CourtBundle? CourtBundle { get; set; }

    /// <summary>Shared by every row created together as one bundle purchase (one per member court).</summary>
    public Guid? BundleGroupId { get; set; }

    /// <summary>
    /// Set only for a guest checkout (no login) — the unguessable capability token emailed to the
    /// guest so they can reach this booking (or, for a bundle, every row sharing the same
    /// <see cref="BundleGroupId"/>) without an account.
    /// </summary>
    public Guid? GuestAccessToken { get; set; }

    /// <summary>
    /// True once the customer has uploaded a payment screenshot. Checks
    /// <see cref="PaymentProofPath"/>, not <see cref="PaymentReference"/> — the reference
    /// number is optional on the submit-proof form, so a customer who leaves it blank must
    /// still be treated as "submitted, awaiting confirmation".
    /// </summary>
    public bool HasPaymentProof => !string.IsNullOrEmpty(PaymentProofPath);
    public double DurationHours => (EndTime - StartTime).TotalHours;
}
