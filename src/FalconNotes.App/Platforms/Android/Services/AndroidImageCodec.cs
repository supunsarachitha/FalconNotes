using System.Runtime.Versioning;
using Android.Graphics;
using Android.Media;
using FalconNotes.Core.Platform;
using Java.Nio;
using Orientation = Android.Media.Orientation;
using Stream = System.IO.Stream;

namespace FalconNotes.App.Services;

/// <summary>
/// <see cref="IImageCodec"/> on Android's own codecs, with no third-party code (docs/02, Dependencies; docs/12,
/// Photos): <c>ImageDecoder</c> reads the photo upright and already at the wanted size, and <c>Bitmap.compress</c>
/// writes the JPEG, which carries no EXIF, so no location or camera details.
/// </summary>
/// <remarks>
/// Android 8.0 and 8.1 have no <c>ImageDecoder</c>. There <c>BitmapFactory</c> decodes, and the EXIF orientation is
/// read and applied by hand.
/// </remarks>
public sealed class AndroidImageCodec : IImageCodec
{
    /// <summary>
    /// The decoders take the whole file from memory, so a larger one is not tried and is added as it is. No photo
    /// comes near this size.
    /// </summary>
    private const long MaxFileBytes = 64L * 1024 * 1024;

    /// <summary>How many rows of pixels are looked at in one go for transparency, to keep the copy small.</summary>
    private const int RowsPerBand = 64;

    /// <inheritdoc />
    public Task<EncodedImage?> EncodeJpegAsync(
        Stream source, Func<int, int, (int Width, int Height)> fit, int quality, CancellationToken cancellationToken) =>
        Task.Run(() => EncodeJpeg(source, fit, quality, cancellationToken), cancellationToken);

    private static EncodedImage? EncodeJpeg(
        Stream source, Func<int, int, (int Width, int Height)> fit, int quality, CancellationToken cancellationToken)
    {
        var length = source.Length - source.Position;
        if (length is <= 0 or > MaxFileBytes)
        {
            return null;
        }

        var file = new byte[length];
        source.ReadExactly(file);
        cancellationToken.ThrowIfCancellationRequested();

        var bitmap = OperatingSystem.IsAndroidVersionAtLeast(28) ? Decode(file, fit) : DecodeLegacy(file, fit);
        if (bitmap is null)
        {
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transparent = HasTransparency(bitmap);
            using var jpeg = new MemoryStream();
            return bitmap.Compress(Bitmap.CompressFormat.Jpeg!, quality, jpeg) ? new EncodedImage(jpeg.ToArray(), transparent) : null;
        }
        finally
        {
            bitmap.Recycle();
            bitmap.Dispose();
        }
    }

    [SupportedOSPlatform("android28.0")]
    private static Bitmap? Decode(byte[] file, Func<int, int, (int Width, int Height)> fit)
    {
        using var buffer = ByteBuffer.Wrap(file);
        using var source = ImageDecoder.CreateSource(buffer);
        using var listener = new HeaderListener(fit);
        try
        {
            return ImageDecoder.DecodeBitmap(source, listener);
        }
        catch (Java.IO.IOException)
        {
            return null; // a format this device cannot decode, or an animation (see HeaderListener)
        }
    }

    private static Bitmap? DecodeLegacy(byte[] file, Func<int, int, (int Width, int Height)> fit)
    {
        if (IsAnimatedWebP(file))
        {
            return null; // left as it is, as HeaderListener does on later versions
        }

        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(file, 0, file.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
        {
            return null;
        }

        // The size is given for the upright image; a photo stored on its side is decoded on its side and turned after.
        var orientation = ReadOrientation(file);
        var onItsSide = orientation is Orientation.Transpose or Orientation.Rotate90 or Orientation.Transverse or Orientation.Rotate270;
        var upright = onItsSide ? fit(bounds.OutHeight, bounds.OutWidth) : fit(bounds.OutWidth, bounds.OutHeight);
        var (width, height) = onItsSide ? (upright.Height, upright.Width) : upright;

        // Decode at the smallest half, quarter, eighth… that is still large enough, then scale that down smoothly.
        var sample = 1;
        while (bounds.OutWidth / (sample * 2) >= width && bounds.OutHeight / (sample * 2) >= height)
        {
            sample *= 2;
        }

        using var options = new BitmapFactory.Options { InSampleSize = sample };
        var decoded = BitmapFactory.DecodeByteArray(file, 0, file.Length, options);
        if (decoded is null)
        {
            return null;
        }

        using var matrix = new Matrix();
        matrix.SetScale((float)width / decoded.Width, (float)height / decoded.Height);
        switch (orientation)
        {
            case Orientation.FlipHorizontal:
                matrix.PostScale(-1, 1);
                break;
            case Orientation.Rotate180:
                matrix.PostRotate(180);
                break;
            case Orientation.FlipVertical:
                matrix.PostScale(1, -1);
                break;
            case Orientation.Transpose:
                matrix.PostRotate(90);
                matrix.PostScale(-1, 1);
                break;
            case Orientation.Rotate90:
                matrix.PostRotate(90);
                break;
            case Orientation.Transverse:
                matrix.PostRotate(270);
                matrix.PostScale(-1, 1);
                break;
            case Orientation.Rotate270:
                matrix.PostRotate(270);
                break;
        }

        if (matrix.IsIdentity)
        {
            return decoded;
        }

        try
        {
            return Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
        }
        finally
        {
            decoded.Recycle();
            decoded.Dispose();
        }
    }

    /// <summary>
    /// <c>BitmapFactory</c> cannot say whether an image is animated, but a WebP's header can: the animation flag of
    /// its <c>VP8X</c> chunk. WebP is the only animated format, apart from GIF, that Android 8 decodes.
    /// </summary>
    private static bool IsAnimatedWebP(byte[] file) =>
        file.Length > 20 && file.AsSpan(0, 4).SequenceEqual("RIFF"u8) && file.AsSpan(8, 8).SequenceEqual("WEBPVP8X"u8)
        && (file[20] & 0x02) != 0;

    private static Orientation ReadOrientation(byte[] file)
    {
        try
        {
            using var content = new MemoryStream(file, writable: false);
            using var exif = new ExifInterface(content);
            return (Orientation)exif.GetAttributeInt(ExifInterface.TagOrientation, (int)Orientation.Normal);
        }
        catch (Java.IO.IOException)
        {
            return Orientation.Normal; // not a format ExifInterface reads on this version; such files rarely carry one
        }
    }

    private static bool HasTransparency(Bitmap bitmap)
    {
        if (!bitmap.HasAlpha)
        {
            return false;
        }

        var width = bitmap.Width;
        var pixels = new int[width * Math.Min(RowsPerBand, bitmap.Height)];
        for (var y = 0; y < bitmap.Height; y += RowsPerBand)
        {
            var rows = Math.Min(RowsPerBand, bitmap.Height - y);
            bitmap.GetPixels(pixels, 0, width, 0, y, width, rows);
            for (var i = 0; i < width * rows; i++)
            {
                if ((uint)pixels[i] >> 24 != 0xFF)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Sets the size to decode at once the image's own size is known, and refuses animations.</summary>
    [SupportedOSPlatform("android28.0")]
    private sealed class HeaderListener(Func<int, int, (int Width, int Height)> fit) : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source source)
        {
            // An animated WebP or AVIF would become a JPEG of its first frame; it is left as it is, like a GIF.
            if (info.IsAnimated)
            {
                throw new Java.IO.IOException("animated");
            }

            var (width, height) = fit(info.Size.Width, info.Size.Height);
            decoder.SetTargetSize(width, height);

            // The default may be a hardware bitmap, whose pixels cannot be read to look for transparency.
            decoder.Allocator = ImageDecoderAllocator.Software;
        }
    }
}
