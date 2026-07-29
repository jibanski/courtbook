using CourtBooking.Models;

namespace CourtBooking.ViewModels;

/// <summary>
/// One Open Play session (a court + date + hour window) with its roster of sign-ups,
/// grouped for the Admin "Open Play Sign-ups" page. Previously built as an anonymous
/// type passed through ViewBag — anonymous types are compiler-generated internal
/// classes, which the Razor view's dynamic (ViewBag) member access can fail to resolve
/// depending on how the views assembly is compiled. A named, public class avoids that
/// entirely.
/// </summary>
public class OpenPlaySessionRow
{
    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public DateOnly BookingDate { get; set; }
    public int StartHour { get; set; }
    public int EndHour { get; set; }
    public List<OpenPlaySignup> Signups { get; set; } = new();
    public int Taken { get; set; }
}
