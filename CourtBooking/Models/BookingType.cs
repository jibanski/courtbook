namespace CourtBooking.Models;

/// <summary>How a recurring scheduled hour may be used by customers.</summary>
public enum BookingType
{
    /// <summary>Ordinary hourly court rental — directly bookable by a customer.</summary>
    HourlyRental,

    /// <summary>Reserved for an admin-hosted open play session — not directly bookable as an hourly rental.</summary>
    AdminHostedOpenPlay
}
