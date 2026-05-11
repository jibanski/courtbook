using CourtBooking.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Court> Courts { get; set; }
    public DbSet<Booking> Bookings { get; set; }
    public DbSet<Sport> Sports { get; set; }
    public DbSet<FacilitySettings> FacilitySettings { get; set; }
    public DbSet<CourtTimeSlot> CourtTimeSlots { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Booking>()
            .HasOne(b => b.Court)
            .WithMany(c => c.Bookings)
            .HasForeignKey(b => b.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Booking>()
            .HasOne(b => b.User)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CourtTimeSlot>()
            .HasOne(s => s.Court)
            .WithMany(c => c.TimeSlots)
            .HasForeignKey(s => s.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Sport>().HasData(
            new Sport { Id = 1, Name = "Tennis",     Description = "Racket sport played on a rectangular court.",     IsActive = true, DisplayOrder = 1 },
            new Sport { Id = 2, Name = "Badminton",  Description = "Fast-paced racket sport using a shuttlecock.",    IsActive = true, DisplayOrder = 2 },
            new Sport { Id = 3, Name = "Basketball", Description = "Team sport played on a rectangular court.",       IsActive = true, DisplayOrder = 3 },
            new Sport { Id = 4, Name = "Volleyball", Description = "Team sport played over a net.",                   IsActive = true, DisplayOrder = 4 },
            new Sport { Id = 5, Name = "Football",   Description = "Team sport played on a grass or turf field.",     IsActive = true, DisplayOrder = 5 },
            new Sport { Id = 6, Name = "Futsal",      Description = "Indoor variant of football on a smaller court.",    IsActive = true, DisplayOrder = 6 },
            new Sport { Id = 7, Name = "Pickleball", Description = "Fast-growing paddle sport combining tennis, badminton, and ping-pong.", IsActive = true, DisplayOrder = 7 }
        );

        builder.Entity<FacilitySettings>().HasData(
            new FacilitySettings { Id = 1, FacilityName = "CourtBook", PaymentInstructions = "Please send the exact amount and include your booking reference in the notes." }
        );

    }
}
