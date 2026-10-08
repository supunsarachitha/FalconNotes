using FalconNotes.Core.Domain;
using FalconNotes.Core.Platform;

namespace FalconNotes.Core.Attachments;

/// <summary>A file to add: its content, name and type, after shrinking or as it was.</summary>
/// <param name="Content">The content.</param>
/// <param name="FileName">The name.</param>
/// <param name="ContentType">The type.</param>
public sealed record FileToAdd(Stream Content, string FileName, string ContentType);

/// <summary>
/// Shrinks photos before they are added, when the preference is on (docs/04, Attachments). Port of
/// <c>web/lib/shrinkPhoto.ts</c>: upright, at most the chosen size's longest side, JPEG at its quality
/// (<see cref="Presets"/>), kept only when at least a tenth smaller. Re-encoding drops location and camera details.
/// The pixel work is the platform's <see cref="IImageCodec"/>.
/// </summary>
/// <param name="codec">The platform's image codec.</param>
public sealed class PhotoShrinker(IImageCodec codec)
{
    /// <summary>The longest side of a shrunk photo, in pixels, at the largest size.</summary>
    public const int MaxPhotoSide = 2560;

    /// <summary>
    /// How far each photo size shrinks: the longest side in pixels, and the JPEG quality. Like the "standard" and "HD"
    /// choices of messaging and photo apps, smaller photos are also saved at a lower quality, where it shows least.
    /// </summary>
    public static readonly IReadOnlyDictionary<PhotoSize, (int MaxSide, int Quality)> Presets = new Dictionary<PhotoSize, (int, int)>
    {
        [PhotoSize.Large] = (MaxPhotoSide, 85),
        [PhotoSize.Medium] = (1920, 80),
        [PhotoSize.Small] = (1280, 75),
    };

    /// <summary>The shrunk photo is kept only when it is at most this share of the original's size.</summary>
    private const double Worthwhile = 0.9;

    // Still images. GIFs are left alone (they may be animated), and so is SVG.
    private static readonly HashSet<string> Shrinkable = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/avif", "image/bmp", "image/heic", "image/heif",
    };

    /// <summary>Whether a file of this type is a still image worth trying to shrink.</summary>
    /// <param name="contentType">The type.</param>
    /// <returns>True for still images.</returns>
    public static bool CanShrink(string contentType) => Shrinkable.Contains(contentType);

    /// <summary>The size to draw an image at: at most <paramref name="max"/> pixels on its longest side, never enlarged.</summary>
    /// <param name="width">The image's width.</param>
    /// <param name="height">The image's height.</param>
    /// <param name="max">The longest side allowed.</param>
    /// <returns>The size, at least 1 × 1.</returns>
    public static (int Width, int Height) FitWithin(int width, int height, int max = MaxPhotoSide)
    {
        var scale = Math.Min(1.0, (double)max / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Floor(width * scale + 0.5)), Math.Max(1, (int)Math.Floor(height * scale + 0.5)));
    }

    /// <summary>The shrunk photo's name: the same name, as a <c>.jpg</c>.</summary>
    /// <param name="name">The original name.</param>
    /// <returns>The new name.</returns>
    public static string JpegName(string name)
    {
        var dot = name.LastIndexOf('.');
        var stem = dot >= 0 ? name[..dot] : name;
        return $"{(stem.Length > 0 ? stem : "photo")}.jpg";
    }

    /// <summary>
    /// Shrinks a photo, or returns it unchanged when it is not a still image, cannot be decoded, has transparent
    /// pixels (which JPEG cannot keep, unless it was a JPEG), or would hardly get smaller.
    /// </summary>
    /// <param name="file">The file as picked; its content must be seekable to be returned unchanged after an attempt.</param>
    /// <param name="size">How far to shrink it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The file to add.</returns>
    public async Task<FileToAdd> ShrinkAsync(FileToAdd file, PhotoSize size = PhotoSize.Large, CancellationToken cancellationToken = default)
    {
        if (!CanShrink(file.ContentType) || !file.Content.CanSeek)
        {
            return file;
        }

        var start = file.Content.Position;
        var originalSize = file.Content.Length - start;
        EncodedImage? encoded;
        try
        {
            var (maxSide, quality) = Presets[size];
            encoded = await codec.EncodeJpegAsync(file.Content, (w, h) => FitWithin(w, h, maxSide), quality, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            encoded = null;
        }

        file.Content.Position = start;
        if (encoded is null
            || (encoded.HasTransparency && !file.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            || encoded.Jpeg.Length > originalSize * Worthwhile)
        {
            return file;
        }

        return new FileToAdd(new MemoryStream(encoded.Jpeg, writable: false), JpegName(file.FileName), "image/jpeg");
    }
}
