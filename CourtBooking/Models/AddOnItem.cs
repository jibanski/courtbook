using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBooking.Models;

/// <summary>A rentable extra a facility offers alongside a court booking (e.g. paddles, shuttlecocks).</summary>
public class AddOnItem
{
    public int Id { get; set; }

    /// <summary>The Admin (facility owner) this item belongs to — same idiom as <c>Court.OwnerId</c>.</summary>
    [Required, MaxLength(450)]
    public string OwnerId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "numeric(10,2)")]
    [Range(0, 100000)]
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;
}
