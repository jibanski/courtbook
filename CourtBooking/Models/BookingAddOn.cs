using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

/// <summary>One rented add-on item attached to a <see cref="Booking"/>, with the quantity and unit
/// price snapshotted at booking time so a later catalog price change doesn't rewrite history.</summary>
public class BookingAddOn
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    public int AddOnItemId { get; set; }
    public AddOnItem AddOnItem { get; set; } = null!;

    public int Quantity { get; set; } = 1;

    [Column(TypeName = "numeric(10,2)")]
    public decimal UnitPrice { get; set; }
}
