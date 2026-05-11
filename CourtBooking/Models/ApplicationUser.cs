using Microsoft.AspNetCore.Identity;

namespace CourtBooking.Models;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumberAlt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public string FullName => $"{FirstName} {LastName}".Trim();
}
