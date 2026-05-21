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
}
