using CourtBooking.Data;
using CourtBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Services;

public class BookingService
{
    private readonly ApplicationDbContext _db;

    public BookingService(ApplicationDbContext db) => _db = db;

    public async Task<List<int>> GetBookedHoursAsync(int courtId, DateOnly date)
    {
        var bookings = await _db.Bookings
            .Where(b => b.CourtId == courtId && b.BookingDate == date && b.Status != BookingStatus.Cancelled)
            .ToListAsync();

        var bookedHours = new List<int>();
        foreach (var b in bookings)
        {
            for (int h = b.StartTime.Hour; h < b.EndTime.Hour; h++)
                bookedHours.Add(h);
        }
        return bookedHours;
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
            .ToListAsync();

        var hours = slotBlocked
            .SelectMany(s => Enumerable.Range(s.StartHour, s.EndHour - s.StartHour))
            .ToHashSet();

        // Date/time range blocks that overlap this date
        var rangeBlocks = await _db.CourtBlocks
            .Where(b => b.CourtId == courtId && b.StartDate <= date && b.EndDate >= date)
            .ToListAsync();

        foreach (var blk in rangeBlocks)
        {
            var (from, to) = blk.HoursOn(date);
            for (int h = from; h < to; h++) hours.Add(h);
        }

        return hours.Distinct().ToList();
    }

    public async Task<bool> IsSlotAvailableAsync(int courtId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        // Reject past slots (Philippine Standard Time = UTC+8)
        var localNow = DateTime.UtcNow.AddHours(8);
        var today    = DateOnly.FromDateTime(localNow);
        if (date < today) return false;
        if (date == today && start.Hour <= localNow.Hour) return false;

        // Reject if an inactive time-slot marker overlaps
        var slotBlocked = await _db.CourtTimeSlots.AnyAsync(s =>
            s.CourtId == courtId &&
            s.SlotDate == date &&
            !s.IsActive &&
            s.StartHour < end.Hour &&
            s.EndHour   > start.Hour);
        if (slotBlocked) return false;

        // Reject if a date/time range CourtBlock overlaps
        var rangeBlocks = await _db.CourtBlocks
            .Where(b => b.CourtId == courtId && b.StartDate <= date && b.EndDate >= date)
            .ToListAsync();

        foreach (var blk in rangeBlocks)
        {
            var (from, to) = blk.HoursOn(date);
            if (from < end.Hour && to > start.Hour) return false;
        }

        return !await _db.Bookings.AnyAsync(b =>
            b.CourtId == courtId &&
            b.BookingDate == date &&
            b.Status != BookingStatus.Cancelled &&
            b.StartTime < end &&
            b.EndTime > start);
    }

    public async Task<List<int>> GetUnavailableSlotIdsAsync(int courtId, DateOnly date, IEnumerable<CourtTimeSlot> slots)
    {
        var bookings = await _db.Bookings
            .Where(b => b.CourtId == courtId && b.BookingDate == date && b.Status != BookingStatus.Cancelled)
            .ToListAsync();

        return slots
            .Where(slot => bookings.Any(b =>
                b.StartTime < new TimeOnly(slot.EndHour, 0) &&
                b.EndTime   > new TimeOnly(slot.StartHour, 0)))
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

    public Task<bool> IsHolidayAsync(string ownerId, DateOnly date) =>
        _db.FacilityHolidays.AnyAsync(h => h.OwnerId == ownerId && h.Date == date);

    public Task<List<CourtRateTier>> GetRateTiersAsync(int courtId) =>
        _db.CourtRateTiers.Where(t => t.CourtId == courtId).ToListAsync();

    public Task<List<CourtScheduleBlock>> GetScheduleBlocksAsync(int courtId) =>
        _db.CourtScheduleBlocks.Where(b => b.CourtId == courtId).ToListAsync();

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
        _db.CourtBundles
            .Where(b => b.IsActive && b.Courts.Any(c => c.CourtId == courtId))
            .Include(b => b.Courts).ThenInclude(c => c.Court)
            .ToListAsync();

    /// <summary>All rate blocks for a bundle, including paused ones (for the admin management page).</summary>
    public Task<List<CourtBundleRateBlock>> GetBundleRateBlocksAsync(int bundleId) =>
        _db.CourtBundleRateBlocks.Where(b => b.CourtBundleId == bundleId).ToListAsync();

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
}
