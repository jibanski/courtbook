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
    public DbSet<CourtBlock> CourtBlocks { get; set; }
    public DbSet<CourtRateTier> CourtRateTiers { get; set; }
    public DbSet<CourtScheduleBlock> CourtScheduleBlocks { get; set; }
    public DbSet<FacilityHoliday> FacilityHolidays { get; set; }
    public DbSet<CourtBundle> CourtBundles { get; set; }
    public DbSet<CourtBundleCourt> CourtBundleCourts { get; set; }
    public DbSet<CourtBundleRateBlock> CourtBundleRateBlocks { get; set; }
    public DbSet<OpenPlaySignup> OpenPlaySignups { get; set; }
    public DbSet<Review> Reviews { get; set; }
    public DbSet<PlatformConfig> PlatformConfig { get; set; }

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

        builder.Entity<CourtBlock>()
            .HasOne(b => b.Court)
            .WithMany()
            .HasForeignKey(b => b.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CourtRateTier>()
            .HasOne(t => t.Court)
            .WithMany()
            .HasForeignKey(t => t.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CourtScheduleBlock>()
            .HasOne(b => b.Court)
            .WithMany()
            .HasForeignKey(b => b.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CourtBundleCourt>()
            .HasOne(bc => bc.CourtBundle)
            .WithMany(b => b.Courts)
            .HasForeignKey(bc => bc.CourtBundleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CourtBundleCourt>()
            .HasOne(bc => bc.Court)
            .WithMany()
            .HasForeignKey(bc => bc.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CourtBundleRateBlock>()
            .HasOne(rb => rb.CourtBundle)
            .WithMany()
            .HasForeignKey(rb => rb.CourtBundleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Booking>()
            .HasOne(b => b.CourtBundle)
            .WithMany()
            .HasForeignKey(b => b.CourtBundleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<OpenPlaySignup>()
            .HasOne(s => s.Court)
            .WithMany()
            .HasForeignKey(s => s.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OpenPlaySignup>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
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

        // FacilitySettings — one record per Admin user, linked by OwnerId
        builder.Entity<FacilitySettings>()
            .HasOne(s => s.Owner)
            .WithMany()
            .HasForeignKey(s => s.OwnerId)
            .OnDelete(DeleteBehavior.SetNull);

        // Courts — owned by the Admin who created them
        builder.Entity<Court>()
            .HasOne(c => c.Owner)
            .WithMany()
            .HasForeignKey(c => c.OwnerId)
            .OnDelete(DeleteBehavior.SetNull);

        // Reviews — testimonials about CourtBook from facility owners
        builder.Entity<Review>()
            .HasOne(r => r.Owner)
            .WithMany()
            .HasForeignKey(r => r.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Review>().HasIndex(r => new { r.IsApproved, r.IsFeatured, r.DisplayOrder });
    }
}
