using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

/// <summary>One line item in a standalone <see cref="AddOnRental"/>, with quantity and unit price
/// snapshotted at sale time — same pattern as <see cref="BookingAddOn"/>.</summary>
public class AddOnRentalItem
{
    public int Id { get; set; }

    public int AddOnRentalId { get; set; }
    public AddOnRental AddOnRental { get; set; } = null!;

    public int AddOnItemId { get; set; }
    public AddOnItem AddOnItem { get; set; } = null!;

    public int Quantity { get; set; } = 1;

    [Column(TypeName = "numeric(10,2)")]
    public decimal UnitPrice { get; set; }

    /// <summary>Snapshotted at sale time so display stays correct if the catalog changes later.</summary>
    public AddOnPricingType PricingType { get; set; } = AddOnPricingType.PerUnit;
}
