using System.ComponentModel.DataAnnotations;

namespace CourtBooking.Models;

/// <summary>
/// Single-row table (Id = 1) storing platform-wide settings such as
/// the donation QR code images. Data persists in PostgreSQL across deploys.
/// </summary>
public class PlatformConfig
{
    public int Id { get; set; } = 1;

    // GCash donation QR
    public byte[]? GCashQrData        { get; set; }
    [MaxLength(50)]
    public string? GCashQrContentType { get; set; }

    // Maya donation QR
    public byte[]? MayaQrData         { get; set; }
    [MaxLength(50)]
    public string? MayaQrContentType  { get; set; }

    // Metrobank donation QR
    public byte[]? MetrobankQrData        { get; set; }
    [MaxLength(50)]
    public string? MetrobankQrContentType { get; set; }

    // BPI donation QR
    public byte[]? BpiQrData        { get; set; }
    [MaxLength(50)]
    public string? BpiQrContentType { get; set; }

    // Platform logo (shown in landing page navbar / footer)
    public byte[]? LogoData        { get; set; }
    [MaxLength(50)]
    public string? LogoContentType { get; set; }
}
