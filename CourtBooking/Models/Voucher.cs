using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

public enum VoucherDiscountType
{
    /// <summary>DiscountValue is a percentage (0-100) of the order subtotal.</summary>
    Percentage = 0,
    /// <summary>DiscountValue is a flat peso amount off the order subtotal.</summary>
    FixedAmount = 1
}

public enum VoucherDiscountScope
{
    /// <summary>Default. In a multi-court cart checkout, applied independently, in full, to
    /// EACH court's own subtotal (a fixed-amount voucher discounts every court by its full
    /// value). Single-item checkouts (Bookings/BundleBookings/OpenPlaySignups) are unaffected
    /// either way since there's only one row.</summary>
    PerCourt = 0,
    /// <summary>In a multi-court cart checkout, computed ONCE against the combined order
    /// subtotal, then distributed proportionally across each court's row.</summary>
    TotalOrder = 1
}

/// <summary>
/// An owner-created discount code (e.g. for loyal customers or a monthly promo) that a
/// customer can type in at checkout to reduce their booking total. Scoped per facility via
/// <see cref="OwnerId"/>, same multi-tenant idiom as <see cref="AddOnItem.OwnerId"/>.
/// </summary>
public class Voucher
{
    public int Id { get; set; }

    /// <summary>The Admin (facility owner) this voucher belongs to.</summary>
    [Required, MaxLength(450)]
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }

    /// <summary>Always stored/matched uppercase so "save10"/"SAVE10" are treated as the same code.</summary>
    [Required, MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }

    public VoucherDiscountType DiscountType { get; set; } = VoucherDiscountType.Percentage;

    /// <summary>How this voucher's discount is applied across a multi-court cart checkout order (see <see cref="VoucherDiscountScope"/>).</summary>
    public VoucherDiscountScope DiscountScope { get; set; } = VoucherDiscountScope.PerCourt;

    /// <summary>Percentage (0-100) or flat peso amount, depending on <see cref="DiscountType"/>.</summary>
    [Column(TypeName = "numeric(10,2)")]
    [Range(0, 1000000)]
    public decimal DiscountValue { get; set; }

    /// <summary>Optional cap on the peso amount a percentage voucher can discount. Ignored for FixedAmount.</summary>
    [Column(TypeName = "numeric(10,2)")]
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>Optional minimum order subtotal required for this voucher to apply.</summary>
    [Column(TypeName = "numeric(10,2)")]
    public decimal? MinSpend { get; set; }

    /// <summary>Null = unlimited uses. Otherwise the voucher stops working once <see cref="TimesRedeemed"/> reaches this.</summary>
    public int? MaxRedemptions { get; set; }

    public int TimesRedeemed { get; set; } = 0;

    /// <summary>Required so every voucher has a hard cutoff. Stored/compared in UTC.</summary>
    [Required]
    public DateTime ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
