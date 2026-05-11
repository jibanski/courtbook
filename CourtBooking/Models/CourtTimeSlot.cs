namespace CourtBooking.Models;

public class CourtTimeSlot
{
    public int Id { get; set; }
    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public DateOnly SlotDate { get; set; }
    public int StartHour { get; set; }
    public int EndHour { get; set; }
    public bool IsActive { get; set; } = true;

    public string TimeLabel => $"{StartHour:D2}:00 – {EndHour:D2}:00";
    public int DurationHours => EndHour - StartHour;
}
