using CourtBooking.Data;
using CourtBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Services;

/// <summary>
/// Validates and computes discounts for owner-created <see cref="Voucher"/> codes at checkout.
/// Does not persist anything itself — callers apply the returned discount to their own
/// booking/signup rows and save alongside them in the same SaveChangesAsync.
/// </summary>
public class VoucherService
{
    private readonly ApplicationDbContext _db;

    public VoucherService(ApplicationDbContext db)
    {
        _db = db;
    }

    public record VoucherResult(bool Success, string? Error, Voucher? Voucher, decimal DiscountAmount);

    /// <summary>
    /// Looks up <paramref name="code"/> for the given facility owner and validates it against
    /// <paramref name="subtotal"/> (the order total before any discount). Returns a failure
    /// result with a user-facing message if the code is missing/invalid/expired/exhausted/
    /// below its minimum spend. The returned <see cref="Voucher"/> is still tracked by the
    /// context — callers should increment <c>TimesRedeemed</c> on it once the order is
    /// actually persisted, not before.
    /// </summary>
    public async Task<VoucherResult> ValidateAsync(string code, string ownerId, decimal subtotal)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.OwnerId == ownerId && v.Code == normalized);

        if (voucher is null)
            return new VoucherResult(false, "Invalid voucher code.", null, 0m);
        if (!voucher.IsActive)
            return new VoucherResult(false, "This voucher code is no longer active.", null, 0m);
        if (voucher.ExpiresAt < DateTime.UtcNow)
            return new VoucherResult(false, "This voucher code has expired.", null, 0m);
        if (voucher.MaxRedemptions.HasValue && voucher.TimesRedeemed >= voucher.MaxRedemptions.Value)
            return new VoucherResult(false, "This voucher code has reached its usage limit.", null, 0m);
        if (voucher.MinSpend.HasValue && subtotal < voucher.MinSpend.Value)
            return new VoucherResult(false, $"This voucher requires a minimum spend of \u20b1{voucher.MinSpend.Value:N0}.", null, 0m);

        var discount = ComputeDiscount(voucher, subtotal);

        return new VoucherResult(true, null, voucher, discount);
    }

    /// <summary>
    /// Applies <paramref name="voucher"/>'s discount rules to <paramref name="subtotal"/> in
    /// isolation — used both for the single-order case above and for callers (e.g. a multi-court
    /// cart) that need the SAME voucher applied independently, in full, to each row's own price
    /// rather than proportionally splitting one combined discount across rows.
    /// </summary>
    public static decimal ComputeDiscount(Voucher voucher, decimal subtotal)
    {
        var discount = voucher.DiscountType == VoucherDiscountType.Percentage
            ? Math.Round(subtotal * (voucher.DiscountValue / 100m), 2)
            : voucher.DiscountValue;

        if (voucher.MaxDiscountAmount.HasValue)
            discount = Math.Min(discount, voucher.MaxDiscountAmount.Value);

        return Math.Clamp(discount, 0m, subtotal);
    }
}
