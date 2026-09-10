using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

public enum AddOnPricingType { PerUnit = 0, PerHour = 1 }

/// <summary>A rentable extra a facility offers alongside a court booking (e.g. paddles, shuttlecocks).</summary>
public class AddOnItem
{
    public int Id { get; set; }

    /// <summary>The Admin (facility owner) this item belongs to — same idiom as <c>Court.OwnerId</c>.</summary>
    [Required, MaxLength(450)]
    public string OwnerId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "numeric(10,2)")]
    [Range(0, 100000)]
    public decimal Price { get; set; }

    public AddOnPricingType PricingType { get; set; } = AddOnPricingType.PerUnit;

    /// <summary>How many physical units the facility owns (e.g. 1 paddle). 0 means unlimited —
    /// no stock cap is enforced. Only checked for <see cref="AddOnPricingType.PerUnit"/> items,
    /// since a PerHour item's <see cref="BookingAddOn.Quantity"/> stores billed hours, not a
    /// concurrent physical count.</summary>
    [Range(0, 100000)]
    public int StockQuantity { get; set; } = 0;

    public bool IsActive { get; set; } = true;
}
