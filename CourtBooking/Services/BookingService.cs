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

    public async Task<bool> IsSlotAvailableAsync(int courtId, DateOnly date, TimeOnly start, TimeOnly end)
    {
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
