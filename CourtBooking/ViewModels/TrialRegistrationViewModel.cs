using System.ComponentModel.DataAnnotations;
using CourtBooking.Validation;
using Microsoft.AspNetCore.Http;

namespace CourtBooking.ViewModels;

public class TrialRegistrationViewModel
{
    /// <summary>"Admin" = Facility Owner, "Customer" = Player/Customer</summary>
    [Required]
    public string Role { get; set; } = "Customer";

    // Only required when Role == Admin; validated manually in controller
    [MaxLength(100)]
    [Display(Name = "Facility / Business Name")]
    public string? FacilityName { get; set; }

    [MaxLength(300)]
    [Display(Name = "Facility Address")]
    public string? FacilityAddress { get; set; }

    [MaxLength(200)]
    [Display(Name = "Tagline")]
    public string? BrandTagline { get; set; }

    [Display(Name = "Logo")]
    public IFormFile? BrandLogo { get; set; }

    // ── Optional payment details (Admin only) ─────────────────────────────
    [MaxLength(20)]
    [Display(Name = "GCash Number")]
    public string? GCashNumber { get; set; }

    [MaxLength(100)]
    [Display(Name = "GCash Account Name")]
    public string? GCashName { get; set; }

    [MaxLength(20)]
    [Display(Name = "Maya Number")]
    public string? MayaNumber { get; set; }

    [MaxLength(100)]
    [Display(Name = "Maya Account Name")]
    public string? MayaName { get; set; }

[Required, MaxLength(50)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(6)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [MustBeTrue]
    [Display(Name = "Privacy Policy")]
    public bool AgreeToPrivacyPolicy { get; set; }
}
