using System.ComponentModel.DataAnnotations;

namespace CourtBooking.ViewModels;

public class SubscriptionUpgradeViewModel
{
    [Required]
    public string Plan { get; set; } = "monthly";          // "monthly" | "annual"

    [Required]
    public string PaymentMethod { get; set; } = "gcash";   // "gcash" | "maya"

    [Required, MaxLength(100)]
    [Display(Name = "Payment Reference Number")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [Display(Name = "Payment Screenshot / Proof")]
    public IFormFile? Proof { get; set; }

    // Filled from config — for display
    public int    MonthlyPrice { get; set; }
    public int    AnnualPrice  { get; set; }
    public string GCashNumber  { get; set; } = string.Empty;
    public string GCashName    { get; set; } = string.Empty;
    public string MayaNumber   { get; set; } = string.Empty;
    public string MayaName     { get; set; } = string.Empty;
}

public class ActivationKeyViewModel
{
    [Required, MaxLength(100)]
    [Display(Name = "Activation Key")]
    public string Key { get; set; } = string.Empty;
}
