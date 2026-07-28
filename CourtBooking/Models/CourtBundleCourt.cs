namespace CourtBooking.Models;

/// <summary>Join row: one court that is a member of a <see cref="CourtBundle"/>.</summary>
public class CourtBundleCourt
{
    public int Id { get; set; }

    public int CourtBundleId { get; set; }
    public CourtBundle CourtBundle { get; set; } = null!;

    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;
}
