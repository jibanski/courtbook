using CourtBooking.Data;
using CourtBooking.Helpers;
using CourtBooking.Models;
using CourtBooking.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace CourtBooking.Services;

public class BookingService
{
    private readonly ApplicationDbContext _db;
    private readonly ConcurrentDictionary<string, Task<bool>> _holidayCache = new();
    private readonly ConcurrentDictionary<int, Task<List<CourtRateTier>>> _rateTiersCache = new();
    private readonly ConcurrentDictionary<int, Task<List<CourtScheduleBlock>>> _scheduleBlocksCache = new();
    private readonly ConcurrentDictionary<int, Task<List<CourtBundle>>> _bundlesCache = new();
    private readonly ConcurrentDictionary<int, Task<List<CourtBundleRateBlock>>> _bundleRateBlocksCache = new();

    public BookingService(ApplicationDbContext db) => _db = db;

    public async Task<List<int>> GetBookedHoursAsync(int courtId, DateOnly date)
    {
        // Only confirmed/completed bookings count as fully "booked"
        var bookings = await _db.Bookings
            .Where(b => b.CourtId == courtId && b.BookingDate == date
                     && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed))
            .AsNoTracking()
            .ToListAsync();

        var bookedHours = new List<int>();
        foreach (var b in bookings)
        {
            int endHour = b.EndTime == TimeOnly.MinValue ? 24 : b.EndTime.Hour;
            for (int h = b.StartTime.Hour; h < endHour; h++)
                bookedHours.Add(h);
        }
        return bookedHours;
    }

    public async Task<List<int>> GetPendingHoursAsync(int courtId, DateOnly date)
    {
        var bookings = await _db.Bookings
            .Where(b => b.CourtId == courtId && b.BookingDate == date && b.Status == BookingStatus.Pending)
            .AsNoTracking()
            .ToListAsync();

        var pendingHours = new List<int>();
        foreach (var b in bookings)
        {
            int endHour = b.EndTime == TimeOnly.MinValue ? 24 : b.EndTime.Hour;
            for (int h = b.StartTime.Hour; h < endHour; h++)
                pendingHours.Add(h);
        }
        return pendingHours;
    }

    /// <summary>
    /// Pending bundle bookings for a court/date, keyed by their start hour so availability views
    /// can render each reservation as one blocked range instead of separate hourly tiles.
    /// </summary>
    public async Task<Dictionary<int, Booking>> GetPendingBundleWindowsAsync(int courtId, DateOnly date)
    {
        var bookings = await _db.Bookings
            .Where(b => b.CourtId == courtId
                     && b.BookingDate == date
                     && b.Status == BookingStatus.Pending
                     && b.CourtBundleId != null)
            .Include(b => b.CourtBundle)
            .AsNoTracking()
            .ToListAsync();

        return bookings
            .GroupBy(b => b.StartTime.Hour)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.CreatedAt).First());
    }

    /// <summary>
    /// Returns hours blocked on <paramref name="date"/> by either:
    /// • inactive CourtTimeSlot records (hourly grid blocks), or
    /// • CourtBlock date/time range records.
    /// </summary>
    public async Task<List<int>> GetBlockedHoursAsync(int courtId, DateOnly date)
    {
        // Hour-level blocks (inactive time-slot markers)
        var slotBlocked = await _db.CourtTimeSlots
            .Where(s => s.CourtId == courtId && s.SlotDate == date && !s.IsActive)
            .AsNoTracking()
            .ToListAsync();

        var hours = slotBlocked
            .SelectMany(s => Enumerable.Range(s.StartHour, s.EndHour - s.StartHour))
            .ToHashSet();

        // Date/time range blocks that overlap this date
        var rangeBlocks = await _db.CourtBlocks
            .Where(b => b.CourtId == courtId && b.StartDate <= date && b.EndDate >= date)
            .AsNoTracking()
            .ToListAsync();

        foreach (var blk in rangeBlocks)
        {
            var (from, to) = blk.HoursOn(date);
            for (int h = from; h < to; h++) hours.Add(h);
        }

        return hours.Distinct().ToList();
    }

    /// <summary>Returns a reason string per blocked hour for CourtBlock range blocks that have a reason set.</summary>
    public async Task<Dictionary<int, string>> GetBlockReasonsAsync(int courtId, DateOnly date)
    {
        var rangeBlocks = await _db.CourtBlocks
            .Where(b => b.CourtId == courtId && b.StartDate <= date && b.EndDate >= date && b.Reason != null)
            .AsNoTracking()
            .ToListAsync();

        var reasons = new Dictionary<int, string>();
        foreach (var blk in rangeBlocks)
        {
            var (from, to) = blk.HoursOn(date);
            for (int h = from; h < to; h++)
                reasons.TryAdd(h, blk.Reason!);
        }
        return reasons;
    }

    public async Task<bool> IsSlotAvailableAsync(int courtId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        // Reject past slots and the 20-minute grace window before a slot starts (PHT = UTC+8)
        var localNow = PhtClock.Now;
        var today    = DateOnly.FromDateTime(localNow);
        if (date < today) return false;
        if (date == today && (start.Hour * 60 + start.Minute + 20) < (localNow.Hour * 60 + localNow.Minute)) return false;

        // end.Hour==0 means midnight (24:00 wrapped to 00:00); treat as end-of-day
        int endHourInt = end.Hour == 0 ? 24 : end.Hour;

        // Sequential awaits — EF Core DbContext is not thread-safe; Task.WhenAll on the same context causes errors
        var slotBlocked = await _db.CourtTimeSlots.AnyAsync(s =>
            s.CourtId == courtId &&
            s.SlotDate == date &&
            !s.IsActive &&
            s.StartHour < endHourInt &&
            s.EndHour   > start.Hour);
        if (slotBlocked) return false;

        var rangeBlocks = await _db.CourtBlocks
            .Where(b => b.CourtId == courtId && b.StartDate <= date && b.EndDate >= date)
            .ToListAsync();

        foreach (var blk in rangeBlocks)
        {
            var (from, to) = blk.HoursOn(date);
            if (from < endHourInt && to > start.Hour) return false;
        }

        var bookings = await _db.Bookings
            .Where(b =>
                b.CourtId == courtId &&
                b.BookingDate == date &&
                b.Status != BookingStatus.Cancelled)
            .ToListAsync();
        
        foreach (var b in bookings)
        {
            // Normalize midnight times: treat 00:00 as 24:00 (end-of-day)
            TimeOnly existingEnd = b.EndTime == TimeOnly.MinValue ? TimeOnly.MaxValue : b.EndTime;
            TimeOnly newEnd = end == TimeOnly.MinValue ? TimeOnly.MaxValue : end;
            
            // Check for overlap: existing starts before new ends AND existing ends after new starts
            if (b.StartTime < newEnd && existingEnd > start)
                return false;
        }
        
        return true;
    }

    public async Task<List<int>> GetUnavailableSlotIdsAsync(int courtId, DateOnly date, IEnumerable<CourtTimeSlot> slots)
    {
        var bookings = await _db.Bookings
            .Where(b => b.CourtId == courtId && b.BookingDate == date && b.Status != BookingStatus.Cancelled)
            .ToListAsync();

        return slots
            .Where(slot => bookings.Any(b =>
                b.StartTime < new TimeOnly(slot.EndHour % 24, 0) &&
                (b.EndTime == TimeOnly.MinValue || b.EndTime > new TimeOnly(slot.StartHour % 24, 0))))
            .Select(s => s.Id)
            .ToList();
    }

    public async Task<Booking> CreateBookingAsync(Booking booking)
    {
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        return booking;
    }

    // ── Recurring weekly schedule & tiered rates ────────────────────────────────

    public Task<bool> IsHolidayAsync(string ownerId, DateOnly date)
    {
        var cacheKey = $"{ownerId}:{date:yyyyMMdd}";
        return _holidayCache.GetOrAdd(cacheKey, _ =>
            _db.FacilityHolidays.AsNoTracking().AnyAsync(h => h.OwnerId == ownerId && h.Date == date));
    }

    public Task<List<CourtRateTier>> GetRateTiersAsync(int courtId) =>
        _rateTiersCache.GetOrAdd(courtId, _ => _db.CourtRateTiers.AsNoTracking().Where(t => t.CourtId == courtId)
            .OrderBy(t => t.StartHour).ThenBy(t => t.EndHour).ThenBy(t => t.DaysOfWeek)
            .ToListAsync());

    public Task<List<CourtScheduleBlock>> GetScheduleBlocksAsync(int courtId) =>
        _scheduleBlocksCache.GetOrAdd(courtId, _ => _db.CourtScheduleBlocks.AsNoTracking().Where(b => b.CourtId == courtId)
            .OrderBy(b => b.StartHour).ThenBy(b => b.EndHour).ThenBy(b => b.DaysOfWeek)
            .ToListAsync());

    /// <summary>Resolved (BookingType, hourly rate) for every hour in the court's opening window on <paramref name="date"/>.</summary>
    public async Task<Dictionary<int, (BookingType Type, decimal Rate)>> GetHourlyScheduleAsync(Court court, DateOnly date)
    {
        var isHoliday = court.OwnerId != null && await IsHolidayAsync(court.OwnerId, date);
        var tiers  = await GetRateTiersAsync(court.Id);
        var blocks = (await GetScheduleBlocksAsync(court.Id)).Where(b => b.IsActive).ToList();

        var result = new Dictionary<int, (BookingType, decimal)>();
        for (int h = court.OpeningHour; h < court.ClosingHour; h++)
        {
            var type = ScheduleRules.ResolveBookingType(blocks, date, isHoliday, h);
            var rate = ScheduleRules.ResolveHourlyRate(tiers, court.PricePerHour, date, isHoliday, h);
            result[h] = (type, rate);
        }
        return result;
    }

    /// <summary>Total price for a grid-based booking, summing the resolved tiered rate across the range.</summary>
    public async Task<decimal> GetTotalPriceAsync(Court court, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var isHoliday = court.OwnerId != null && await IsHolidayAsync(court.OwnerId, date);
        var tiers = await GetRateTiersAsync(court.Id);
        return ScheduleRules.ResolveTotalPrice(tiers, court.PricePerHour, date, isHoliday, start, end);
    }

    /// <summary>
    /// Re-resolves and saves <see cref="Booking.TotalPrice"/> for every not-yet-paid, non-cancelled booking
    /// on this court, using the court's current rate/tiers. Called after an owner edits the base rate or a
    /// rate tier so pending bookings reflect the new price instead of the one snapshotted when they were made.
    /// Skips bundle rows (<see cref="Booking.CourtBundleId"/> set) — those are priced from the bundle's flat
    /// rate, not this court's hourly rate — and anything already <see cref="PaymentStatus.Paid"/>/Refunded.
    /// </summary>
    public async Task<int> ResyncUnpaidPricesAsync(int courtId)
    {
        var court = await _db.Courts.FindAsync(courtId);
        if (court is null) return 0;

        var affected = await _db.Bookings
            .Where(b => b.CourtId == courtId
                     && b.CourtBundleId == null
                     && b.PaymentStatus == PaymentStatus.Unpaid
                     && b.Status != BookingStatus.Cancelled)
            .AsNoTracking()
            .Include(b => b.AddOns)
            .ToListAsync();

        foreach (var b in affected)
        {
            var courtTotal = await GetTotalPriceAsync(court, b.BookingDate, b.StartTime, b.EndTime);
            var addOnsTotal = b.AddOns.Sum(a => a.Quantity * a.UnitPrice);
            b.TotalPrice = courtTotal + addOnsTotal;
        }

        if (affected.Count > 0) await _db.SaveChangesAsync();
        return affected.Count;
    }

    // ── Add-on rentals ───────────────────────────────────────────────────────────

    /// <summary>Active add-on items in this facility owner's catalog, selectable when booking any of their courts.</summary>
    public Task<List<AddOnItem>> GetActiveAddOnsAsync(string ownerId) =>
        _db.AddOnItems.AsNoTracking().Where(a => a.OwnerId == ownerId && a.IsActive).OrderBy(a => a.Name).ToListAsync();

    /// <summary>
    /// Reads quantity form fields named <c>addon_{Id}</c> for each of the owner's active add-ons,
    /// builds a <see cref="BookingAddOn"/> for every quantity &gt; 0 (snapshotting the current price),
    /// and returns them along with their combined total. Shared by the customer and staff walk-in
    /// booking flows so add-on handling stays identical between them.
    /// </summary>
    public async Task<(List<BookingAddOn> AddOns, decimal Total)> ResolveSelectedAddOnsAsync(string ownerId, IFormCollection form, int durationHours = 1)
    {
        var items = await GetActiveAddOnsAsync(ownerId);
        var selections = new List<AddOnSelection>();

        foreach (var item in items)
        {
            if (!form.TryGetValue($"addon_{item.Id}", out var raw)) continue;
            if (!int.TryParse(raw, out var qty) || qty <= 0) continue;

            int? hrs = null;
            if (item.PricingType == AddOnPricingType.PerHour)
                hrs = form.TryGetValue($"addon_hrs_{item.Id}", out var hrsRaw) && int.TryParse(hrsRaw, out var h) && h > 0
                    ? h : durationHours;

            selections.Add(new AddOnSelection(item.Id, qty, hrs));
        }

        return ResolveAddOnsCore(items, selections, durationHours);
    }

    /// <summary>
    /// Same resolution/pricing logic as <see cref="ResolveSelectedAddOnsAsync"/>, but for callers whose
    /// selections don't come from an <see cref="IFormCollection"/> (e.g. a JSON-sourced multi-item cart
    /// checkout). Both funnel through <see cref="ResolveAddOnsCore"/> so pricing/validation can't diverge.
    /// </summary>
    public async Task<(List<BookingAddOn> AddOns, decimal Total)> ResolveAddOnsAsync(string ownerId, IEnumerable<AddOnSelection> selections, int durationHours = 1)
    {
        var items = await GetActiveAddOnsAsync(ownerId);
        return ResolveAddOnsCore(items, selections, durationHours);
    }

    private static (List<BookingAddOn> AddOns, decimal Total) ResolveAddOnsCore(
        List<AddOnItem> catalog, IEnumerable<AddOnSelection> selections, int durationHours)
    {
        var result = new List<BookingAddOn>();
        decimal total = 0;

        foreach (var selection in selections)
        {
            var item = catalog.FirstOrDefault(i => i.Id == selection.AddOnItemId);
            if (item is null || selection.Quantity <= 0) continue;

            if (item.PricingType == AddOnPricingType.PerHour)
            {
                var hrs = selection.Hours is > 0 ? selection.Hours.Value : durationHours;
                result.Add(new BookingAddOn { AddOnItemId = item.Id, Quantity = hrs, UnitPrice = item.Price, PricingType = item.PricingType });
                total += hrs * item.Price;
            }
            else
            {
                result.Add(new BookingAddOn { AddOnItemId = item.Id, Quantity = selection.Quantity, UnitPrice = item.Price, PricingType = item.PricingType });
                total += selection.Quantity * item.Price;
            }
        }

        return (result, total);
    }

    /// <summary>
    /// Same <c>addon_{Id}</c>/<c>addon_hrs_{Id}</c> form-reading logic as <see cref="ResolveSelectedAddOnsAsync"/>,
    /// but builds <see cref="AddOnRentalItem"/> line items for a standalone add-on-only sale (no court/booking
    /// attached) instead of <see cref="BookingAddOn"/>.
    /// </summary>
    public async Task<(List<AddOnRentalItem> Items, decimal Total)> ResolveSelectedAddOnRentalItemsAsync(string ownerId, IFormCollection form, int durationHours = 1)
    {
        var catalog = await GetActiveAddOnsAsync(ownerId);
        var result = new List<AddOnRentalItem>();
        decimal total = 0;

        foreach (var item in catalog)
        {
            if (!form.TryGetValue($"addon_{item.Id}", out var raw)) continue;
            if (!int.TryParse(raw, out var qty) || qty <= 0) continue;

            if (item.PricingType == AddOnPricingType.PerHour)
            {
                var hrs = form.TryGetValue($"addon_hrs_{item.Id}", out var hrsRaw) && int.TryParse(hrsRaw, out var h) && h > 0
                    ? h : durationHours;
                result.Add(new AddOnRentalItem { AddOnItemId = item.Id, Quantity = hrs, UnitPrice = item.Price, PricingType = item.PricingType });
                total += hrs * item.Price;
            }
            else
            {
                result.Add(new AddOnRentalItem { AddOnItemId = item.Id, Quantity = qty, UnitPrice = item.Price, PricingType = item.PricingType });
                total += qty * item.Price;
            }
        }

        return (result, total);
    }

    /// <summary>One requested add-on line from a non-form source (e.g. a cart-checkout JSON payload).</summary>
    public record AddOnSelection(int AddOnItemId, int Quantity, int? Hours);

    /// <summary>The min and max hourly rate a court can charge across its base rate and any rate tiers —
    /// a display-only range for customer-facing pages; actual charging still resolves a single rate per hour.</summary>
    public async Task<(decimal Min, decimal Max)> GetRateRangeAsync(Court court)
    {
        var tiers = await GetRateTiersAsync(court.Id);
        var prices = tiers.Select(t => t.PricePerHour).Append(court.PricePerHour).ToList();
        return (prices.Min(), prices.Max());
    }

    /// <summary>Bulk variant of <see cref="GetRateRangeAsync"/> for list pages — one query for all rate tiers
    /// across the given courts instead of one query per court.</summary>
    public async Task<Dictionary<int, (decimal Min, decimal Max)>> GetRateRangesAsync(IEnumerable<Court> courts)
    {
        var courtList = courts as IList<Court> ?? courts.ToList();
        var courtIds = courtList.Select(c => c.Id).ToList();
        var tiersByCourtId = (await _db.CourtRateTiers.Where(t => courtIds.Contains(t.CourtId)).ToListAsync())
            .GroupBy(t => t.CourtId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.PricePerHour).ToList());

        var result = new Dictionary<int, (decimal Min, decimal Max)>();
        foreach (var court in courtList)
        {
            var prices = tiersByCourtId.TryGetValue(court.Id, out var tierPrices) ? tierPrices : new List<decimal>();
            prices.Add(court.PricePerHour);
            result[court.Id] = (prices.Min(), prices.Max());
        }
        return result;
    }

    /// <summary>True when any hour in [start, end) is scheduled as Admin-Hosted Open Play by default.</summary>
    public async Task<bool> HasOpenPlayHoursAsync(Court court, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var isHoliday = court.OwnerId != null && await IsHolidayAsync(court.OwnerId, date);
        var blocks = (await GetScheduleBlocksAsync(court.Id)).Where(b => b.IsActive).ToList();
        for (int h = start.Hour; h < end.Hour; h++)
            if (ScheduleRules.ResolveBookingType(blocks, date, isHoliday, h) == BookingType.AdminHostedOpenPlay)
                return true;
        return false;
    }

    // ── Bundled multi-court "peak hours" booking ────────────────────────────────

    /// <summary>Active bundles this court is a member of (a court may belong to more than one).</summary>
    public Task<List<CourtBundle>> GetBundlesForCourtAsync(int courtId) =>
        _bundlesCache.GetOrAdd(courtId, _ => _db.CourtBundles.AsNoTracking()
            .Where(b => b.IsActive && b.Courts.Any(c => c.CourtId == courtId))
            .Include(b => b.Courts).ThenInclude(c => c.Court)
            .ToListAsync());

    /// <summary>All rate blocks for a bundle, including paused ones (for the admin management page).</summary>
    public Task<List<CourtBundleRateBlock>> GetBundleRateBlocksAsync(int bundleId) =>
        _bundleRateBlocksCache.GetOrAdd(bundleId, _ => _db.CourtBundleRateBlocks.AsNoTracking().Where(b => b.CourtBundleId == bundleId).ToListAsync());

    /// <summary>
    /// If this court/date/hour falls inside an active bundle's active rate block, returns that
    /// (bundle, block) pair — the hour is sellable only as part of the bundle, not individually.
    /// </summary>
    public async Task<(CourtBundle Bundle, CourtBundleRateBlock Block)?> ResolveBundleForHourAsync(Court court, DateOnly date, int hour)
    {
        var isHoliday = court.OwnerId != null && await IsHolidayAsync(court.OwnerId, date);
        var bundles = await GetBundlesForCourtAsync(court.Id);
        foreach (var bundle in bundles)
        {
            var blocks = (await GetBundleRateBlocksAsync(bundle.Id)).Where(b => b.IsActive).ToList();
            var match = ScheduleRules.ResolveBundleRateBlock(blocks, date, isHoliday, hour);
            if (match is not null) return (bundle, match);
        }
        return null;
    }

    /// <summary>True when any hour in [start, end) is covered by an active bundle rate block for this court.</summary>
    public async Task<bool> HasBundleOnlyHoursAsync(Court court, DateOnly date, TimeOnly start, TimeOnly end)
    {
        for (int h = start.Hour; h < end.Hour; h++)
            if (await ResolveBundleForHourAsync(court, date, h) is not null)
                return true;
        return false;
    }

    /// <summary>"Bundled Booking Only" — sellable only if every member court is free for the whole window.</summary>
    public async Task<bool> IsBundleWindowFullyAvailableAsync(CourtBundle bundle, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var memberCourtIds = bundle.Courts.Select(c => c.CourtId).ToList();
        foreach (var courtId in memberCourtIds)
            if (!await IsSlotAvailableAsync(courtId, date, start, end))
                return false;
        return memberCourtIds.Count > 0;
    }

    // ── Public sign-up for Admin-Hosted Open Play ────────────────────────────────

    /// <summary>The recurring schedule block (if any) covering this court/date/hour — lets callers read
    /// Open Play sign-up settings (AllowPublicSignup/MaxPlayers/PricePerHead), not just the BookingType.</summary>
    public async Task<CourtScheduleBlock?> ResolveScheduleBlockForHourAsync(Court court, DateOnly date, int hour)
    {
        var isHoliday = court.OwnerId != null && await IsHolidayAsync(court.OwnerId, date);
        var blocks = (await GetScheduleBlocksAsync(court.Id)).Where(b => b.IsActive).ToList();
        return ScheduleRules.ResolveScheduleBlock(blocks, date, isHoliday, hour);
    }

    /// <summary>Non-cancelled sign-ups for this Open Play session occurrence.</summary>
    public Task<List<OpenPlaySignup>> GetOpenPlaySignupsAsync(int courtId, DateOnly date, int startHour, int endHour) =>
        _db.OpenPlaySignups
            .Where(s => s.CourtId == courtId && s.BookingDate == date &&
                        s.StartHour == startHour && s.EndHour == endHour &&
                        s.Status != BookingStatus.Cancelled)
            .Include(s => s.User)
            .ToListAsync();

    /// <summary>Spots left in this Open Play session — only meaningful when the block allows public sign-up.</summary>
    public async Task<int> GetOpenPlaySpotsRemainingAsync(CourtScheduleBlock block, int courtId, DateOnly date)
    {
        if (!block.AllowPublicSignup || !block.MaxPlayers.HasValue) return 0;
        var taken = (await GetOpenPlaySignupsAsync(courtId, date, block.StartHour, block.EndHour)).Sum(s => s.SpotCount);
        return Math.Max(0, block.MaxPlayers.Value - taken);
    }

    /// <summary>
    /// Spots left for a STAFF-initiated walk-in registration into an Open Play session. Unlike
    /// <see cref="GetOpenPlaySpotsRemainingAsync"/>, this ignores <see cref="CourtScheduleBlock.AllowPublicSignup"/> —
    /// front-desk staff can register a walk-in into any Admin-Hosted Open Play block regardless of
    /// whether online self-signup is enabled for customers. Returns null when the block has no
    /// configured <see cref="CourtScheduleBlock.MaxPlayers"/> cap — treat that as unlimited capacity.
    /// </summary>
    public async Task<int?> GetOpenPlaySpotsRemainingForStaffAsync(CourtScheduleBlock block, int courtId, DateOnly date)
    {
        if (!block.MaxPlayers.HasValue) return null;
        var taken = (await GetOpenPlaySignupsAsync(courtId, date, block.StartHour, block.EndHour)).Sum(s => s.SpotCount);
        return Math.Max(0, block.MaxPlayers.Value - taken);
    }

    /// <summary>Builds a fully-populated <see cref="CourtAvailabilityViewModel"/> for one court on one
    /// date — the same slot/hourly-grid computation <c>FacilityController.BookCourt</c> used to do
    /// inline. Extracted so a "book across all courts" page can call it once per court without
    /// duplicating this logic.</summary>
    public async Task<CourtAvailabilityViewModel> GetCourtAvailabilityAsync(Court court, DateOnly date)
    {
        var vm = new CourtAvailabilityViewModel { Court = court, Date = date };
        (vm.RateRangeMin, vm.RateRangeMax) = await GetRateRangeAsync(court);

        var slots = await _db.CourtTimeSlots
            .Where(s => s.CourtId == court.Id && s.IsActive && s.SlotDate == date)
            .OrderBy(s => s.StartHour)
            .ToListAsync();

        if (slots.Any())
        {
            vm.TimeSlots          = slots;
            vm.UnavailableSlotIds = await GetUnavailableSlotIdsAsync(court.Id, date, slots);
            foreach (var s in slots)
            {
                vm.SlotPrices[s.Id] = await GetTotalPriceAsync(
                    court, date, new TimeOnly(s.StartHour % 24, 0), new TimeOnly(s.EndHour % 24, 0));
            }

            return vm;
        }

        var bookedHours           = await GetBookedHoursAsync(court.Id, date);
        var pendingHours          = await GetPendingHoursAsync(court.Id, date);
        var pendingBundleWindows  = await GetPendingBundleWindowsAsync(court.Id, date);
        var blockedHours          = await GetBlockedHoursAsync(court.Id, date);
        var blockReasons          = await GetBlockReasonsAsync(court.Id, date);
        var schedule              = await GetHourlyScheduleAsync(court, date);

        var bundleOnlyHours    = new Dictionary<int, (CourtBundle Bundle, CourtBundleRateBlock Block)>();
        var openPlaySignupInfo = new Dictionary<int, (CourtScheduleBlock Block, int SpotsRemaining)>();
        for (int h = court.OpeningHour; h < court.ClosingHour; h++)
        {
            var match = await ResolveBundleForHourAsync(court, date, h);
            if (match is not null) { bundleOnlyHours[h] = match.Value; continue; }

            if (schedule.TryGetValue(h, out var s) && s.Type == BookingType.AdminHostedOpenPlay)
            {
                var block = await ResolveScheduleBlockForHourAsync(court, date, h);
                if (block is { AllowPublicSignup: true })
                {
                    var spotsRemaining = await GetOpenPlaySpotsRemainingAsync(block, court.Id, date);
                    openPlaySignupInfo[h] = (block, spotsRemaining);
                }
            }
        }

        vm.BookedHours          = bookedHours;
        vm.PendingHours         = pendingHours;
        vm.PendingBundleWindows = pendingBundleWindows;
        vm.BlockedHours         = blockedHours;
        vm.BlockReasons         = blockReasons;
        vm.BundleOnlyHours      = bundleOnlyHours;
        vm.OpenPlaySignupInfo   = openPlaySignupInfo;
        vm.OpenPlayHours = schedule
            .Where(kv => kv.Value.Type == BookingType.AdminHostedOpenPlay && !bundleOnlyHours.ContainsKey(kv.Key))
            .Select(kv => kv.Key).ToList();
        vm.HourlyRates = schedule.ToDictionary(kv => kv.Key, kv => kv.Value.Rate);
        vm.AvailableHours = Enumerable
            .Range(court.OpeningHour, court.ClosingHour - court.OpeningHour)
            .Where(h => !bookedHours.Contains(h) && !pendingHours.Contains(h) && !blockedHours.Contains(h)
                     && !vm.OpenPlayHours.Contains(h) && !bundleOnlyHours.ContainsKey(h))
            .ToList();

        return vm;
    }

    // ── Staff walk-in cash bookings ─────────────────────────────────────────────

    /// <summary>Sales walk-ins logged by staff for these courts — regular court bookings, Open Play
    /// sign-ups, and standalone add-on-only rentals — merged into one list, any payment method
    /// (Cash, GCash, Maya, GoTyme), optionally filtered to one staff member and/or a date range.
    /// Used by both the staff's own log and the owner's reconciliation view — despite the "Cash Log"
    /// name (kept for URL/nav stability), it covers every staff/owner-logged sale so nothing slips
    /// through untracked, with <see cref="CashLogRow.PaymentMethod"/> letting the views split cash
    /// from digital totals. <paramref name="ownerId"/> scopes the add-on-rental portion (which has no
    /// court to filter by); pass null to skip it.</summary>
    public async Task<List<CashLogRow>> GetCashLogAsync(List<int> courtIds, string? staffId, DateOnly? from, DateOnly? to, string? ownerId = null)
    {
        var bookingQuery = _db.Bookings
            .Where(b => courtIds.Contains(b.CourtId)
                     && b.LoggedByStaffId != null && b.Status != BookingStatus.Cancelled)
            .Include(b => b.Court)
            .Include(b => b.User)
            .Include(b => b.AddOns).ThenInclude(a => a.AddOnItem)
            .AsQueryable();

        var signupQuery = _db.OpenPlaySignups
            .Where(s => courtIds.Contains(s.CourtId)
                     && s.LoggedByStaffId != null && s.Status != BookingStatus.Cancelled)
            .Include(s => s.Court)
            .Include(s => s.User)
            .AsQueryable();

        var rentalQuery = ownerId != null
            ? _db.AddOnRentals
                .Where(r => r.OwnerId == ownerId
                         && r.LoggedByStaffId != null && r.Status != BookingStatus.Cancelled)
                .Include(r => r.User)
                .Include(r => r.Items).ThenInclude(i => i.AddOnItem)
                .AsQueryable()
            : null;

        if (staffId != null)
        {
            bookingQuery = bookingQuery.Where(b => b.LoggedByStaffId == staffId);
            signupQuery  = signupQuery.Where(s => s.LoggedByStaffId == staffId);
            rentalQuery  = rentalQuery?.Where(r => r.LoggedByStaffId == staffId);
        }
        if (from.HasValue)
        {
            // Filter by when the sale was logged (CreatedAt), not the court's BookingDate — a
            // staff member may log a cash payment today for a booking dated some other day, and
            // the Sales Log needs to reflect when the money actually came in.
            // DateTimeKind.Utc must be specified explicitly — Npgsql rejects Kind=Unspecified
            // DateTimes as a parameter against a "timestamp with time zone" column (works fine
            // against SQLite locally, but throws in production on Postgres).
            var fromDt = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-8);
            bookingQuery = bookingQuery.Where(b => b.CreatedAt >= fromDt);
            signupQuery  = signupQuery.Where(s => s.CreatedAt >= fromDt);
            rentalQuery  = rentalQuery?.Where(r => r.CreatedAt >= fromDt);
        }
        if (to.HasValue)
        {
            var toDt = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-8);
            bookingQuery = bookingQuery.Where(b => b.CreatedAt < toDt);
            signupQuery  = signupQuery.Where(s => s.CreatedAt < toDt);
            rentalQuery  = rentalQuery?.Where(r => r.CreatedAt < toDt);
        }

        var bookings = await bookingQuery.ToListAsync();
        var signups  = await signupQuery.ToListAsync();
        var rentals  = rentalQuery != null ? await rentalQuery.ToListAsync() : new List<AddOnRental>();

        var rows = bookings.Select(b =>
        {
            var addOnsTotal = b.AddOns.Sum(a => a.Quantity * a.UnitPrice);
            return new CashLogRow
            {
                Id              = b.Id,
                IsOpenPlay      = false,
                BookingDate     = b.BookingDate,
                StartTime       = b.StartTime,
                EndTime         = b.EndTime,
                CourtName       = b.Court.Name,
                CustomerName    = b.CustomerNameSnapshot ?? b.User.FullName,
                CourtRental     = b.TotalPrice - addOnsTotal,
                AddOnsTotal     = addOnsTotal,
                AddOnsSummary   = b.AddOns.Any() ? string.Join(", ", b.AddOns.Select(a => $"{a.Quantity}x {a.AddOnItem.Name}")) : null,
                TotalPrice      = b.TotalPrice,
                PaymentMethod   = b.PaymentMethod,
                Status          = b.Status,
                LoggedByStaffId = b.LoggedByStaffId,
                CreatedAt       = b.CreatedAt
            };
        }).ToList();

        rows.AddRange(signups.Select(s => new CashLogRow
        {
            Id              = s.Id,
            IsOpenPlay      = true,
            BookingDate     = s.BookingDate,
            StartTime       = new TimeOnly(s.StartHour % 24, 0),
            EndTime         = new TimeOnly(s.EndHour % 24, 0),
            CourtName       = s.Court.Name,
            CustomerName    = s.CustomerNameSnapshot ?? s.User.FullName,
            SpotCount       = s.SpotCount,
            PlayerNames     = s.PlayerNames,
            CourtRental     = s.TotalPrice,
            AddOnsTotal     = 0,
            AddOnsSummary   = null,
            TotalPrice      = s.TotalPrice,
            PaymentMethod   = s.PaymentMethod,
            Status          = s.Status,
            LoggedByStaffId = s.LoggedByStaffId,
            CreatedAt       = s.CreatedAt
        }));

        rows.AddRange(rentals.Select(r =>
        {
            var localCreated = r.CreatedAt.AddHours(8);
            return new CashLogRow
            {
                Id              = r.Id,
                IsAddOnOnly     = true,
                BookingDate     = DateOnly.FromDateTime(localCreated),
                StartTime       = TimeOnly.FromDateTime(localCreated),
                EndTime         = TimeOnly.FromDateTime(localCreated),
                CourtName       = "Add-on Rental",
                CustomerName    = r.CustomerNameSnapshot ?? r.User.FullName,
                CourtRental     = 0,
                AddOnsTotal     = r.TotalPrice,
                AddOnsSummary   = r.Items.Any() ? string.Join(", ", r.Items.Select(i => $"{i.Quantity}x {i.AddOnItem.Name}")) : null,
                TotalPrice      = r.TotalPrice,
                PaymentMethod   = r.PaymentMethod,
                Status          = r.Status,
                LoggedByStaffId = r.LoggedByStaffId,
                CreatedAt       = r.CreatedAt
            };
        }));

        return rows.OrderByDescending(r => r.LoggedDate).ThenBy(r => r.CreatedAt).ToList();
    }
}
