using Microsoft.AspNetCore.Identity;

namespace CourtBooking.Models;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumberAlt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    /// <summary>
    /// Slug of the facility this customer was onboarded from.
    /// After login, customers are always sent to /sportshub/{PreferredFacilitySlug}.
    /// </summary>
    public string? PreferredFacilitySlug { get; set; }

    public DateTime? PrivacyPolicyAcceptedAt { get; set; }

    /// <summary>True for a shadow account auto-created for a guest checkout — no password, never signed in.</summary>
    public bool IsGuest { get; set; }

    /// <summary>For a Staff account, the Id of the Admin (facility owner) who created it — scopes
    /// what courts/bookings this staff member can see, mirroring <c>Court.OwnerId</c>. Null for
    /// Admin and Customer accounts.</summary>
    public string? EmployerOwnerId { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}
