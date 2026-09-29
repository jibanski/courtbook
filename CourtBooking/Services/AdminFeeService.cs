using CourtBooking.Data;
using CourtBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Services;

public record AdminFeeMonthlyRow(int Year, int Month, int ChargeCount, decimal Generated, decimal Paid, decimal Outstanding)
{
    public string MonthLabel => new DateOnly(Year, Month, 1).ToString("MMMM yyyy");
}

public record AdminFeeOwnerSummary(
    decimal Outstanding,
    decimal PendingVerification,
    decimal TotalPaid,
    decimal ThisMonthGenerated,
    int ThisMonthChargeCount,
    List<AdminFeeMonthlyRow> MonthlyHistory,
    List<AdminFeeSettlement> RecentSettlements);

public record AdminFeePlatformRow(
    int FacilitySettingsId,
    string FacilityName,
    string OwnerEmail,
    decimal Generated,
    decimal Paid,
    decimal Outstanding,
    decimal PendingVerification);

/// <summary>
/// Owns the platform Admin Fee ledger: accruing a charge when a qualifying booking/sign-up is
/// confirmed, reversing it proportionally on refund, and settling/verifying payment against it.
/// All balances are computed from <see cref="AdminFeeCharge"/>/<see cref="AdminFeeSettlement"/>
/// rows rather than cached on <see cref="FacilitySettings"/>, so they're always derivable from
/// the underlying transaction history.
/// </summary>
public class AdminFeeService
{
    private readonly ApplicationDbContext _db;

    public AdminFeeService(ApplicationDbContext db) => _db = db;

    private static decimal ComputeFee(decimal totalPrice, FacilitySettings settings) =>
        settings.AdminFeeType == AdminFeeType.Fixed
            ? settings.AdminFeeFixedAmount
            : Math.Round(totalPrice * settings.AdminFeeRate / 100m, 2);

    /// <summary>Previews the admin fee for a customer-facing checkout breakdown (online
    /// self-checkout only) — computed the same way as the owner-absorbed ledger charge, off the
    /// amount the customer owes for the court/spots + add-ons after any voucher discount, before
    /// the fee itself is added on top. Returns 0 when the facility isn't admin-fee-enabled.</summary>
    public decimal PreviewFee(decimal amountBeforeFee, FacilitySettings? settings) =>
        settings?.IsAdminFeeEnabled == true && amountBeforeFee > 0 ? ComputeFee(amountBeforeFee, settings) : 0m;

    private static AdminFeeCharge BuildCharge(decimal amount, FacilitySettings settings) => new()
    {
        OwnerId = settings.OwnerId!,
        Amount = amount,
        OriginalAmount = amount,
        FeeTypeSnapshot = settings.AdminFeeType,
        FeeRateSnapshot = settings.AdminFeeType == AdminFeeType.Percentage ? settings.AdminFeeRate : null,
        FeeFixedAmountSnapshot = settings.AdminFeeType == AdminFeeType.Fixed ? settings.AdminFeeFixedAmount : null,
        AccruedAt = DateTime.UtcNow,
    };

    /// <summary>
    /// Call once, when a booking (or a group of bookings sharing one <see cref="Booking.BundleGroupId"/>
    /// — a bundle purchase or a multi-court cart checkout, all confirmed together) first becomes
    /// Confirmed+Paid. The fee is charged ONCE per transaction, not once per court/row: a Fixed fee
    /// is a flat amount for the whole group, and a Percentage fee is computed off the group's
    /// combined total, not summed per-row (which would overcharge a multi-court checkout under a
    /// Fixed fee, and needlessly risk rounding drift under a Percentage one).
    /// No-op if the facility isn't admin-fee-enabled, the group's total is free, or a charge
    /// already exists for this booking/group.
    /// </summary>
    public async Task AccrueForBookingsAsync(IReadOnlyList<Booking> bookings, FacilitySettings? settings)
    {
        if (settings?.IsAdminFeeEnabled != true || bookings.Count == 0) return;

        var primary = bookings[0];
        var groupId = primary.BundleGroupId;
        var alreadyCharged = groupId.HasValue
            ? await _db.AdminFeeCharges.AnyAsync(c => c.BundleGroupId == groupId)
            : await _db.AdminFeeCharges.AnyAsync(c => c.BookingId == primary.Id);
        if (alreadyCharged) return;

        // Online self-checkout already collected the fee from the customer at booking time
        // (Booking.AdminFeeAmount, baked into TotalPrice) — use that locked-in amount rather than
        // recomputing a percentage off TotalPrice, which now already includes the fee itself and
        // would double-dip. Walk-in/cash bookings never set AdminFeeAmount, so they fall through
        // to the original owner-absorbed calculation, computed fresh off TotalPrice.
        var collectedAtCheckout = bookings.Sum(b => b.AdminFeeAmount);
        decimal amount;
        if (collectedAtCheckout > 0)
        {
            amount = collectedAtCheckout;
        }
        else
        {
            var totalPrice = bookings.Sum(b => b.TotalPrice);
            if (totalPrice <= 0) return;
            amount = ComputeFee(totalPrice, settings);
        }
        if (amount <= 0) return;

        var charge = BuildCharge(amount, settings);
        charge.Booking = primary;
        charge.BundleGroupId = groupId;
        _db.AdminFeeCharges.Add(charge);
    }

    /// <summary>Sign-up equivalent of <see cref="AccrueForBookingsAsync"/> — sign-ups are never
    /// grouped into one transaction, so this always charges a single sign-up individually.</summary>
    public async Task AccrueForSignupAsync(OpenPlaySignup signup, FacilitySettings? settings)
    {
        if (settings?.IsAdminFeeEnabled != true || signup.TotalPrice <= 0) return;
        if (await _db.AdminFeeCharges.AnyAsync(c => c.OpenPlaySignupId == signup.Id)) return;

        var amount = signup.AdminFeeAmount > 0 ? signup.AdminFeeAmount : ComputeFee(signup.TotalPrice, settings);
        if (amount <= 0) return;

        var charge = BuildCharge(amount, settings);
        charge.OpenPlaySignup = signup;
        _db.AdminFeeCharges.Add(charge);
    }

    /// <summary>Proportionally reduces a booking/sign-up's admin-fee charge to match a refund. For
    /// a booking that's part of a bundle/cart group, the charge covers the whole group, so the
    /// share is computed against the group's aggregate remaining balance (every sibling booking's
    /// own remaining, since only <paramref name="booking"/> has had this specific refund applied
    /// yet), not just this one row — otherwise refunding one of several court bookings in the same
    /// checkout would wipe out (or barely touch) a fee that's shared across all of them. No-op if
    /// there's no charge (facility wasn't admin-fee-enabled, or the fee was 0).</summary>
    public async Task ReverseForRefundAsync(Booking? booking, OpenPlaySignup? signup, decimal refundedAmount, decimal remainingBeforeRefund)
    {
        if (remainingBeforeRefund <= 0) return;

        AdminFeeCharge? charge;
        var groupRemaining = remainingBeforeRefund;

        if (booking is not null)
        {
            charge = booking.BundleGroupId.HasValue
                ? await _db.AdminFeeCharges.FirstOrDefaultAsync(c => c.BundleGroupId == booking.BundleGroupId)
                : await _db.AdminFeeCharges.FirstOrDefaultAsync(c => c.BookingId == booking.Id);

            if (charge is not null && booking.BundleGroupId.HasValue)
            {
                var siblingsRemaining = await _db.Bookings
                    .Where(b => b.BundleGroupId == booking.BundleGroupId && b.Id != booking.Id)
                    .Select(b => b.TotalPrice - (b.RefundAmount ?? 0m))
                    .SumAsync();
                groupRemaining = remainingBeforeRefund + siblingsRemaining;
            }
        }
        else
        {
            charge = await _db.AdminFeeCharges.FirstOrDefaultAsync(c => c.OpenPlaySignupId == signup!.Id);
        }

        if (charge is null || charge.Amount <= 0 || groupRemaining <= 0) return;

        var share = charge.Amount * (refundedAmount / groupRemaining);
        charge.Amount = Math.Max(0, charge.Amount - share);
        charge.ReversedAt ??= DateTime.UtcNow;
    }

    public async Task<AdminFeeOwnerSummary> GetOwnerSummaryAsync(string ownerId)
    {
        var charges = await _db.AdminFeeCharges
            .Where(c => c.OwnerId == ownerId)
            .Include(c => c.Settlement)
            .AsNoTracking()
            .ToListAsync();
        var settlements = await _db.AdminFeeSettlements
            .Where(s => s.OwnerId == ownerId)
            .OrderByDescending(s => s.SubmittedAt)
            .AsNoTracking()
            .ToListAsync();

        var outstanding = charges.Where(c => c.SettlementId == null).Sum(c => c.Amount);
        var pendingVerification = charges.Where(c => c.Settlement?.Status == AdminFeeSettlementStatus.Submitted).Sum(c => c.Amount);
        // Paid is read off the settlement's own recorded Amount, not the charges it happens to
        // link to — a settlement is authoritative proof of what was actually paid, independent of
        // whether every linked charge survives (e.g. a historical backfill settlement with no
        // linked charges at all still represents a real payment that must count as paid).
        var totalPaid = settlements.Where(s => s.Status == AdminFeeSettlementStatus.Verified).Sum(s => s.Amount);

        var now = DateTime.UtcNow;
        var thisMonth = charges.Where(c => c.AccruedAt.Year == now.Year && c.AccruedAt.Month == now.Month).ToList();

        var generatedByMonth = charges.GroupBy(c => (c.AccruedAt.Year, c.AccruedAt.Month))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Generated: g.Sum(c => c.OriginalAmount), Outstanding: g.Where(c => c.SettlementId == null).Sum(c => c.Amount)));
        var paidByMonth = settlements.Where(s => s.Status == AdminFeeSettlementStatus.Verified)
            .GroupBy(s => (s.SubmittedAt.Year, s.SubmittedAt.Month))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));

        var monthly = generatedByMonth.Keys.Union(paidByMonth.Keys)
            .Select(key => new AdminFeeMonthlyRow(
                key.Year, key.Month,
                ChargeCount: generatedByMonth.TryGetValue(key, out var g) ? g.Count : 0,
                Generated: generatedByMonth.TryGetValue(key, out var g2) ? g2.Generated : 0,
                Paid: paidByMonth.GetValueOrDefault(key),
                Outstanding: generatedByMonth.TryGetValue(key, out var g3) ? g3.Outstanding : 0))
            .OrderByDescending(m => m.Year).ThenByDescending(m => m.Month)
            .ToList();

        return new AdminFeeOwnerSummary(
            outstanding, pendingVerification, totalPaid,
            ThisMonthGenerated: thisMonth.Sum(c => c.OriginalAmount),
            ThisMonthChargeCount: thisMonth.Count,
            MonthlyHistory: monthly,
            RecentSettlements: settlements.Take(20).ToList());
    }

    /// <summary>Submits the facility's full current outstanding balance for settlement. Throws
    /// <see cref="InvalidOperationException"/> if a settlement is already awaiting verification
    /// (prevents duplicate/overlapping settlement rows) or there's nothing owed.</summary>
    public async Task<AdminFeeSettlement> SubmitSettlementAsync(string ownerId, string? reference, string? proofPath)
    {
        var hasPending = await _db.AdminFeeSettlements
            .AnyAsync(s => s.OwnerId == ownerId && s.Status == AdminFeeSettlementStatus.Submitted);
        if (hasPending)
            throw new InvalidOperationException("You already have a payment submitted and awaiting verification.");

        var outstandingCharges = await _db.AdminFeeCharges
            .Where(c => c.OwnerId == ownerId && c.SettlementId == null && c.Amount > 0)
            .ToListAsync();
        var outstanding = outstandingCharges.Sum(c => c.Amount);
        if (outstanding <= 0)
            throw new InvalidOperationException("You have no outstanding admin fee balance to pay.");

        var settlement = new AdminFeeSettlement
        {
            OwnerId = ownerId,
            Amount = outstanding,
            PaymentReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            ProofPath = proofPath,
            SubmittedAt = DateTime.UtcNow,
            Status = AdminFeeSettlementStatus.Submitted,
        };
        _db.AdminFeeSettlements.Add(settlement);

        foreach (var charge in outstandingCharges)
            charge.Settlement = settlement;

        await _db.SaveChangesAsync();
        return settlement;
    }

    public async Task VerifySettlementAsync(int settlementId, string verifiedByName)
    {
        var settlement = await _db.AdminFeeSettlements.FindAsync(settlementId)
            ?? throw new InvalidOperationException("Settlement not found.");
        if (settlement.Status != AdminFeeSettlementStatus.Submitted)
            throw new InvalidOperationException("Only a submitted (pending) settlement can be verified.");

        settlement.Status = AdminFeeSettlementStatus.Verified;
        settlement.VerifiedAt = DateTime.UtcNow;
        settlement.VerifiedByName = verifiedByName;
        await _db.SaveChangesAsync();
    }

    /// <summary>Rejects a settlement and unlinks its charges so they become outstanding again —
    /// a failed/invalid payment must never reduce the owner's balance.</summary>
    public async Task RejectSettlementAsync(int settlementId, string verifiedByName, string? reason)
    {
        var settlement = await _db.AdminFeeSettlements
            .Include(s => s.Charges)
            .FirstOrDefaultAsync(s => s.Id == settlementId)
            ?? throw new InvalidOperationException("Settlement not found.");
        if (settlement.Status != AdminFeeSettlementStatus.Submitted)
            throw new InvalidOperationException("Only a submitted (pending) settlement can be rejected.");

        settlement.Status = AdminFeeSettlementStatus.Rejected;
        settlement.VerifiedAt = DateTime.UtcNow;
        settlement.VerifiedByName = verifiedByName;
        settlement.RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        foreach (var charge in settlement.Charges)
            charge.Settlement = null;

        await _db.SaveChangesAsync();
    }

    /// <summary>Cross-facility rows for the Dev Admin monitoring dashboard.</summary>
    public async Task<List<AdminFeePlatformRow>> GetPlatformOverviewAsync(int? facilityId = null, int? year = null, int? month = null)
    {
        var facilities = await _db.FacilitySettings
            .Where(f => f.BillingModel == "Commission" && (facilityId == null || f.Id == facilityId))
            .AsNoTracking()
            .ToListAsync();
        var ownerIds = facilities.Where(f => f.OwnerId != null).Select(f => f.OwnerId!).ToList();

        var chargesQuery = _db.AdminFeeCharges
            .Where(c => ownerIds.Contains(c.OwnerId))
            .Include(c => c.Settlement)
            .AsNoTracking();
        if (year.HasValue) chargesQuery = chargesQuery.Where(c => c.AccruedAt.Year == year);
        if (month.HasValue) chargesQuery = chargesQuery.Where(c => c.AccruedAt.Month == month);
        var charges = await chargesQuery.ToListAsync();

        var settlementsQuery = _db.AdminFeeSettlements
            .Where(s => ownerIds.Contains(s.OwnerId) && s.Status == AdminFeeSettlementStatus.Verified)
            .AsNoTracking();
        if (year.HasValue) settlementsQuery = settlementsQuery.Where(s => s.SubmittedAt.Year == year);
        if (month.HasValue) settlementsQuery = settlementsQuery.Where(s => s.SubmittedAt.Month == month);
        var verifiedSettlements = await settlementsQuery.ToListAsync();

        var owners = await _db.Users.Where(u => ownerIds.Contains(u.Id)).AsNoTracking().ToListAsync();

        return facilities.Select(f =>
        {
            var mine = charges.Where(c => c.OwnerId == f.OwnerId).ToList();
            return new AdminFeePlatformRow(
                FacilitySettingsId: f.Id,
                FacilityName: f.FacilityName,
                OwnerEmail: owners.FirstOrDefault(u => u.Id == f.OwnerId)?.Email ?? "(no owner)",
                Generated: mine.Sum(c => c.OriginalAmount),
                Paid: verifiedSettlements.Where(s => s.OwnerId == f.OwnerId).Sum(s => s.Amount),
                Outstanding: mine.Where(c => c.SettlementId == null).Sum(c => c.Amount),
                PendingVerification: mine.Where(c => c.Settlement?.Status == AdminFeeSettlementStatus.Submitted).Sum(c => c.Amount));
        }).ToList();
    }

    /// <summary>Settlements awaiting Dev Admin verification, across all facilities (or one).</summary>
    public async Task<List<AdminFeeSettlement>> GetPendingSettlementsAsync(string? ownerId = null)
    {
        var query = _db.AdminFeeSettlements
            .Where(s => s.Status == AdminFeeSettlementStatus.Submitted);
        if (ownerId != null) query = query.Where(s => s.OwnerId == ownerId);
        return await query.OrderBy(s => s.SubmittedAt).AsNoTracking().ToListAsync();
    }
}
