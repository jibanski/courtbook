using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Models;

public class Sport
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; } = 0;
}
