using CourtBooking.Data;
using CourtBooking.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CourtBooking.Services;

/// <summary>
/// Creates (or reuses) a passwordless "guest" <see cref="ApplicationUser"/> so someone
/// can book a court, join Open Play, or buy a bundle without registering an account.
/// Mirrors the passwordless account creation <c>AccountController</c>'s external-login
/// flow already uses for OAuth sign-ins — a guest is a real user row, just never signed in.
/// </summary>
public class GuestCheckoutService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public GuestCheckoutService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db          = db;
        _userManager = userManager;
    }

    public async Task<ApplicationUser> GetOrCreateGuestUserAsync(string fullName, string email, string phone)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        var parts     = fullName.Trim().Split(' ', 2);
        var firstName = parts[0];
        var lastName  = parts.Length > 1 ? parts[1] : "";

        // An email already tied to a real (non-guest) account can't be reused for guest
        // checkout — Identity's unique username constraint would reject it anyway. Ask
        // the visitor to log in instead of letting UserManager.CreateAsync throw.
        if (existing is not null && !existing.IsGuest)
            throw new GuestEmailConflictException(
                "An account already exists with this email. Please log in to continue.");

        if (existing is not null)
        {
            existing.FirstName   = firstName;
            existing.LastName    = lastName;
            existing.PhoneNumber = phone;
            await _userManager.UpdateAsync(existing);
            return existing;
        }

        var guest = new ApplicationUser
        {
            UserName    = email.Trim(),
            Email       = email.Trim(),
            FirstName   = firstName,
            LastName    = lastName,
            PhoneNumber = phone,
            IsGuest     = true,
            EmailConfirmed = false
        };

        var result = await _userManager.CreateAsync(guest);
        if (!result.Succeeded)
            throw new InvalidOperationException("Could not create guest checkout: " + string.Join(" ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(guest, "Customer");
        return guest;
    }
}

/// <summary>Thrown when a guest checkout email already belongs to a registered (non-guest) account.</summary>
public class GuestEmailConflictException : Exception
{
    public GuestEmailConflictException(string message) : base(message) { }
}
