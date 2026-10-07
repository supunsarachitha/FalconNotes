namespace FalconNotes.Core.Platform;

/// <summary>A photo re-encoded by <see cref="IImageCodec"/>.</summary>
/// <param name="Jpeg">The JPEG's bytes.</param>
/// <param name="HasTransparency">Whether the decoded image had any pixel that is not fully opaque.</param>
public sealed record EncodedImage(byte[] Jpeg, bool HasTransparency);

/// <summary>
/// Decodes and re-encodes photos for "Shrink photos before adding" (docs/04, Attachments). Implemented per platform;
/// the rules are in <c>PhotoShrinker</c>.
/// </summary>
public interface IImageCodec
{
    /// <summary>
    /// Decodes an image upright (applying its EXIF orientation), scales it to the size <paramref name="fit"/> gives
    /// for its upright width and height, and encodes it as a JPEG without metadata.
    /// </summary>
    /// <param name="source">The image file.</param>
    /// <param name="fit">The size to draw at, given the upright size.</param>
    /// <param name="quality">JPEG quality, 0–100.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The JPEG, or null when the image cannot be decoded.</returns>
    Task<EncodedImage?> EncodeJpegAsync(Stream source, Func<int, int, (int Width, int Height)> fit, int quality, CancellationToken cancellationToken);
}
