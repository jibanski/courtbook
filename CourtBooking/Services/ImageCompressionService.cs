using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace CourtBooking.Services;

/// <summary>
/// Re-encodes user-uploaded photos (payment-proof screenshots, court photos) to JPEG,
/// downscaling anything larger than <see cref="MaxDimension"/>. Runs automatically on
/// upload so customers no longer need to manually shrink phone screenshots themselves.
/// </summary>
public class ImageCompressionService
{
    private const int MaxDimension = 1600;
    private const int JpegQuality  = 75;

    /// <summary>Reads an uploaded image, resizes/re-encodes it, and returns JPEG bytes.
    /// Throws <see cref="UnknownImageFormatException"/> if the stream isn't a decodable image.</summary>
    public async Task<byte[]> CompressAsync(Stream input)
    {
        using var image = await Image.LoadAsync(input);

        if (image.Width > MaxDimension || image.Height > MaxDimension)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxDimension, MaxDimension)
            }));
        }

        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = JpegQuality });
        return ms.ToArray();
    }
}
