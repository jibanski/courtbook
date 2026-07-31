using CourtBooking.Data;
using CourtBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Services;

/// <summary>
/// Background service that periodically checks for expired booking and Open Play sign-up reservations.
/// When a Pending booking/sign-up has passed its 15-minute ReservedUntil time, this service
/// automatically cancels it to release the slot back for other customers.
/// 
/// Runs every 1 minute to ensure slots are released promptly.
/// </summary>
public class ReservationExpiryCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval     = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _services;
    private readonly ILogger<ReservationExpiryCleanupService> _logger;

    public ReservationExpiryCleanupService(
        IServiceProvider services,
        ILogger<ReservationExpiryCleanupService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ReservationExpiry] starting; first check in {Delay}", StartupDelay);
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReservationExpiry] unhandled exception during cleanup sweep");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;

        List<Booking> expiredBookings;
        List<OpenPlaySignup> expiredSignups;

        // PostgreSQL stores ReservedUntil as TEXT but model expects DateTime.
        // Wrap in try-catch to gracefully skip cleanup if type mismatch occurs,
        // allowing the app to function while we migrate the column type.
        try
        {
            // Find expired Bookings that are still Pending
            expiredBookings = await db.Bookings
                .Where(b => b.Status == BookingStatus.Pending
                         && b.ReservedUntil.HasValue
                         && b.ReservedUntil.Value <= now)
                .ToListAsync(ct);
        }
        catch (InvalidCastException ex)
        {
            _logger.LogWarning(ex, "[ReservationExpiry] skipping booking cleanup due to ReservedUntil TEXT/DateTime type mismatch");
            expiredBookings = new List<Booking>();
        }

        try
        {
            // Find expired OpenPlaySignups that are still Pending
            expiredSignups = await db.OpenPlaySignups
                .Where(s => s.Status == BookingStatus.Pending
                         && s.ReservedUntil.HasValue
                         && s.ReservedUntil.Value <= now)
                .ToListAsync(ct);
        }
        catch (InvalidCastException ex)
        {
            _logger.LogWarning(ex, "[ReservationExpiry] skipping signup cleanup due to ReservedUntil TEXT/DateTime type mismatch");
            expiredSignups = new List<OpenPlaySignup>();
        }

        if (expiredBookings.Count > 0)
        {
            foreach (var booking in expiredBookings)
            {
                booking.Status = BookingStatus.Cancelled;
                _logger.LogInformation("[ReservationExpiry] Cancelled booking #{Id} (reservation expired)", booking.Id);
            }
            await db.SaveChangesAsync(ct);
        }

        if (expiredSignups.Count > 0)
        {
            foreach (var signup in expiredSignups)
            {
                signup.Status = BookingStatus.Cancelled;
                _logger.LogInformation("[ReservationExpiry] Cancelled signup #{Id} (reservation expired)", signup.Id);
            }
            await db.SaveChangesAsync(ct);
        }

        if (expiredBookings.Count > 0 || expiredSignups.Count > 0)
        {
            _logger.LogInformation("[ReservationExpiry] Cleaned up {BookingCount} bookings and {SignupCount} signups",
                expiredBookings.Count, expiredSignups.Count);
        }
    }
}
