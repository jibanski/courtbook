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
    List<AdminFeeSettlement> RecentSettlements,
    decimal CreditBalance = 0m,
    List<AdminFeePayment>? RecentPayments = null);

public record AdminFeePlatformRow(
    int FacilitySettingsId,
    string FacilityName,
    string OwnerEmail,
    decimal Generated,
    decimal Paid,
    decimal Outstanding,
    decimal PendingVerification,
    decimal CreditBalance = 0m);

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

    /// <summary>Convenience wrapper for call sites that flip a single booking to Paid (staff/admin confirm,
    /// status dropdown, checkout return page). If the booking belongs to a bundle/cart group, the whole
    /// group is passed through so the one-fee-per-transaction rule is computed off the group's combined
    /// total rather than just this row. Idempotent — a no-op if a charge already exists.</summary>
    public async Task AccrueForBookingAsync(Booking booking, FacilitySettings? settings)
    {
        if (settings?.IsAdminFeeEnabled != true) return;

        var rows = new List<Booking> { booking };
        if (booking.BundleGroupId.HasValue)
        {
            var siblings = await _db.Bookings
                .Where(b => b.BundleGroupId == booking.BundleGroupId && b.Id != booking.Id)
                .ToListAsync();
            rows.AddRange(siblings);
        }
        await AccrueForBookingsAsync(rows, settings);
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

    /// <summary>Credit a facility has left from manually recorded payments: active (non-voided)
    /// <see cref="AdminFeePayment"/> amounts minus whatever its non-rejected settlements already used.
    /// Summed client-side so it also works on the SQLite dev database (no server-side decimal SUM).</summary>
    private async Task<decimal> GetCreditAsync(string ownerId)
    {
        var paid = (await _db.AdminFeePayments.Where(p => p.OwnerId == ownerId && !p.IsVoided)
            .Select(p => p.Amount).ToListAsync()).Sum();
        var used = (await _db.AdminFeeSettlements
            .Where(s => s.OwnerId == ownerId && s.Status != AdminFeeSettlementStatus.Rejected)
            .Select(s => s.CreditApplied).ToListAsync()).Sum();
        return Math.Max(0m, paid - used);
    }

    /// <summary>Records a payment received outside the app. It becomes credit: outstanding drops by
    /// this amount, and any excess carries forward against future fees.</summary>
    public async Task<AdminFeePayment> RecordPaymentAsync(string ownerId, decimal amount, DateTime paidOn, string? note, string recordedByName)
    {
        if (amount <= 0) throw new InvalidOperationException("Payment amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(recordedByName)) throw new InvalidOperationException("Please enter your name.");

        var payment = new AdminFeePayment
        {
            OwnerId = ownerId,
            Amount = Math.Round(amount, 2),
            PaidOn = DateTime.SpecifyKind(paidOn, DateTimeKind.Utc),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            RecordedByName = recordedByName.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.AdminFeePayments.Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    /// <summary>Voids a recorded payment (kept in history, marked voided). Refused if part of it has
    /// already been used up by a settlement, since voiding would then leave the balance wrong.</summary>
    public async Task VoidPaymentAsync(int paymentId, string voidedByName, string? reason)
    {
        if (string.IsNullOrWhiteSpace(voidedByName)) throw new InvalidOperationException("Please enter your name.");

        var payment = await _db.AdminFeePayments.FirstOrDefaultAsync(p => p.Id == paymentId)
            ?? throw new InvalidOperationException("Payment not found.");
        if (payment.IsVoided) throw new InvalidOperationException("This payment is already voided.");

        var credit = await GetCreditAsync(payment.OwnerId);
        if (payment.Amount > credit + 0.01m)
            throw new InvalidOperationException(
                "This payment can't be voided because part of it was already applied to a settlement.");

        payment.IsVoided = true;
        payment.VoidedAt = DateTime.UtcNow;
        payment.VoidedByName = voidedByName.Trim();
        payment.VoidReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await _db.SaveChangesAsync();
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

        var payments = await _db.AdminFeePayments
            .Where(p => p.OwnerId == ownerId)
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.Id)
            .AsNoTracking()
            .ToListAsync();

        var credit = await GetCreditAsync(ownerId);
        var unsettled = charges.Where(c => c.SettlementId == null).Sum(c => c.Amount);
        var outstanding = Math.Max(0m, unsettled - credit);
        var creditBalance = Math.Max(0m, credit - unsettled);
        // Pending is what the owner actually submitted as cash (the settlement's own Amount), not the
        // linked charges' total, since part of those charges may have been covered by recorded credit.
        var pendingVerification = settlements.Where(s => s.Status == AdminFeeSettlementStatus.Submitted).Sum(s => s.Amount);
        // Paid is read off the settlement's own recorded Amount, not the charges it happens to
        // link to — a settlement is authoritative proof of what was actually paid, independent of
        // whether every linked charge survives (e.g. a historical backfill settlement with no
        // linked charges at all still represents a real payment that must count as paid). Manually
        // recorded (non-voided) payments count as paid too.
        var totalPaid = settlements.Where(s => s.Status == AdminFeeSettlementStatus.Verified).Sum(s => s.Amount)
                      + payments.Where(p => !p.IsVoided).Sum(p => p.Amount);

        var now = DateTime.UtcNow;
        var thisMonth = charges.Where(c => c.AccruedAt.Year == now.Year && c.AccruedAt.Month == now.Month).ToList();

        // Credit is applied to the oldest unsettled fees first, so each month's Outstanding adds up
        // to the headline balance.
        var remainingCredit = credit;
        var outstandingByMonth = new Dictionary<(int Year, int Month), decimal>();
        foreach (var c in charges.Where(c => c.SettlementId == null).OrderBy(c => c.AccruedAt))
        {
            var used = Math.Min(remainingCredit, c.Amount);
            remainingCredit -= used;
            var key = (c.AccruedAt.Year, c.AccruedAt.Month);
            outstandingByMonth[key] = outstandingByMonth.GetValueOrDefault(key) + (c.Amount - used);
        }

        var generatedByMonth = charges.GroupBy(c => (c.AccruedAt.Year, c.AccruedAt.Month))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Generated: g.Sum(c => c.OriginalAmount)));
        var paidByMonth = settlements.Where(s => s.Status == AdminFeeSettlementStatus.Verified)
            .Select(s => (Date: s.SubmittedAt, s.Amount))
            .Concat(payments.Where(p => !p.IsVoided).Select(p => (Date: p.PaidOn, p.Amount)))
            .GroupBy(x => (x.Date.Year, x.Date.Month))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var monthly = generatedByMonth.Keys.Union(paidByMonth.Keys)
            .Select(key => new AdminFeeMonthlyRow(
                key.Year, key.Month,
                ChargeCount: generatedByMonth.TryGetValue(key, out var g) ? g.Count : 0,
                Generated: generatedByMonth.TryGetValue(key, out var g2) ? g2.Generated : 0,
                Paid: paidByMonth.GetValueOrDefault(key),
                Outstanding: outstandingByMonth.GetValueOrDefault(key)))
            .OrderByDescending(m => m.Year).ThenByDescending(m => m.Month)
            .ToList();

        return new AdminFeeOwnerSummary(
            outstanding, pendingVerification, totalPaid,
            ThisMonthGenerated: thisMonth.Sum(c => c.OriginalAmount),
            ThisMonthChargeCount: thisMonth.Count,
            MonthlyHistory: monthly,
            RecentSettlements: settlements.Take(20).ToList(),
            CreditBalance: creditBalance,
            RecentPayments: payments.Take(20).ToList());
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
        var unsettled = outstandingCharges.Sum(c => c.Amount);
        if (unsettled <= 0)
            throw new InvalidOperationException("You have no outstanding admin fee balance to pay.");

        // Net out any credit from manually recorded payments; the settlement is only for the rest.
        var credit = await GetCreditAsync(ownerId);
        var creditApplied = Math.Min(credit, unsettled);
        var payable = unsettled - creditApplied;
        if (payable <= 0)
            throw new InvalidOperationException("Your balance is fully covered by payments already recorded — nothing to pay right now.");

        var settlement = new AdminFeeSettlement
        {
            OwnerId = ownerId,
            Amount = payable,
            CreditApplied = creditApplied,
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

        // Manually recorded payments (credit). Counted as Paid when received in the selected period.
        var allPayments = await _db.AdminFeePayments
            .Where(p => ownerIds.Contains(p.OwnerId) && !p.IsVoided)
            .AsNoTracking().ToListAsync();
        var paymentsInPeriod = allPayments
            .Where(p => (!year.HasValue || p.PaidOn.Year == year) && (!month.HasValue || p.PaidOn.Month == month))
            .ToList();

        // Credit left per owner and every unsettled charge (ignoring the period filter), so credit can be
        // applied to the oldest fees first and the period then reports only what is still owed in it.
        var usedByOwner = (await _db.AdminFeeSettlements
                .Where(s => ownerIds.Contains(s.OwnerId) && s.Status != AdminFeeSettlementStatus.Rejected)
                .AsNoTracking().ToListAsync())
            .GroupBy(s => s.OwnerId).ToDictionary(g => g.Key, g => g.Sum(s => s.CreditApplied));
        var allUnsettled = await _db.AdminFeeCharges
            .Where(c => ownerIds.Contains(c.OwnerId) && c.SettlementId == null)
            .AsNoTracking().ToListAsync();

        var owners = await _db.Users.Where(u => ownerIds.Contains(u.Id)).AsNoTracking().ToListAsync();

        return facilities.Select(f =>
        {
            var mine = charges.Where(c => c.OwnerId == f.OwnerId).ToList();

            var credit = Math.Max(0m,
                allPayments.Where(p => p.OwnerId == f.OwnerId).Sum(p => p.Amount) - usedByOwner.GetValueOrDefault(f.OwnerId ?? ""));
            var remaining = credit;
            decimal outstandingInPeriod = 0m;
            foreach (var c in allUnsettled.Where(c => c.OwnerId == f.OwnerId).OrderBy(c => c.AccruedAt))
            {
                var used = Math.Min(remaining, c.Amount);
                remaining -= used;
                bool inPeriod = (!year.HasValue || c.AccruedAt.Year == year) && (!month.HasValue || c.AccruedAt.Month == month);
                if (inPeriod) outstandingInPeriod += c.Amount - used;
            }

            return new AdminFeePlatformRow(
                FacilitySettingsId: f.Id,
                FacilityName: f.FacilityName,
                OwnerEmail: owners.FirstOrDefault(u => u.Id == f.OwnerId)?.Email ?? "(no owner)",
                Generated: mine.Sum(c => c.OriginalAmount),
                Paid: verifiedSettlements.Where(s => s.OwnerId == f.OwnerId).Sum(s => s.Amount)
                      + paymentsInPeriod.Where(p => p.OwnerId == f.OwnerId).Sum(p => p.Amount),
                Outstanding: outstandingInPeriod,
                // Grouped by settlement id: with AsNoTracking each charge carries its own copy of the
                // settlement, so a plain Distinct() over the objects would count a settlement per charge.
                PendingVerification: mine.Where(c => c.Settlement?.Status == AdminFeeSettlementStatus.Submitted)
                                         .GroupBy(c => c.SettlementId)
                                         .Sum(g => g.First().Settlement!.Amount),
                CreditBalance: remaining);
        }).ToList();
    }

    /// <summary>Recent manually recorded payments (including voided ones, for history) across all
    /// facilities, or just one owner's.</summary>
    public async Task<List<AdminFeePayment>> GetRecentPaymentsAsync(string? ownerId = null, int take = 50)
    {
        var query = _db.AdminFeePayments.AsQueryable();
        if (ownerId != null) query = query.Where(p => p.OwnerId == ownerId);
        return await query.OrderByDescending(p => p.CreatedAt).Take(take).AsNoTracking().ToListAsync();
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
