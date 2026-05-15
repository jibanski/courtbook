using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Validation;

public class MustBeTrueAttribute : ValidationAttribute
{
    public MustBeTrueAttribute() : base("You must agree to the Privacy Policy to continue.") { }

    public override bool IsValid(object? value) => value is true;
}
