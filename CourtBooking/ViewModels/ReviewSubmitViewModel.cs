using System.ComponentModel.DataAnnotations;

namespace CourtBooking.ViewModels;

public class ReviewSubmitViewModel
{
    [Range(1, 5, ErrorMessage = "Please choose a rating from 1 to 5 stars.")]
    public int Rating { get; set; } = 5;

    [MaxLength(120)]
    [Display(Name = "Headline (optional)")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Please share a few sentences about your experience.")]
    [MinLength(20, ErrorMessage = "Please add a bit more detail — at least 20 characters.")]
    [MaxLength(600)]
    [Display(Name = "Your review")]
    public string Body { get; set; } = string.Empty;
}
