using System.Security.Cryptography;
using System.Text;

namespace CourtBooking.Services;

public class KeyGeneratorService
{
    private readonly string _secret;

    public KeyGeneratorService(IConfiguration config)
    {
        _secret = config["Subscription:Secret"]
            ?? throw new InvalidOperationException("Subscription:Secret is not configured.");
    }

    /// <summary>
    /// Generates a unique, deterministic activation key for a subscriber.
    /// Format: CB-XXXX-XXXX-XXXX  (based on HMAC-SHA256 of email+plan)
    /// </summary>
    public string GenerateKey(string email, string plan)
    {
        var payload = $"{email.Trim().ToLower()}|{plan.Trim().ToLower()}";
        var keyBytes = Encoding.UTF8.GetBytes(_secret);
        var msgBytes = Encoding.UTF8.GetBytes(payload);
        var hash     = HMACSHA256.HashData(keyBytes, msgBytes);

        // Use first 6 bytes → 12 hex chars → format CB-XXXX-XXXX-XXXX
        var hex = Convert.ToHexString(hash)[..12].ToUpper();
        return $"CB-{hex[0..4]}-{hex[4..8]}-{hex[8..12]}";
    }

    /// <summary>
    /// Verifies an activation key entered by the subscriber.
    /// Tries both plans in case the admin stored a different plan label.
    /// </summary>
    public bool VerifyKey(string inputKey, string email, string? plan = null)
    {
        var normalised = inputKey.Trim().ToUpper();

        if (plan is not null)
            return normalised == GenerateKey(email, plan);

        // Try all known plans
        return new[] { "monthly", "annual" }
            .Any(p => normalised == GenerateKey(email, p));
    }
}
