using System.Globalization;

namespace FalconNotes.Core.Attachments;

/// <summary>
/// One byte range from an HTTP <c>Range</c> header, as the media handler answers it (docs/02, Serving attachments to
/// the WebView). Media players ask for a single range; several ranges in one header are answered with the whole file.
/// </summary>
/// <param name="Start">The first byte, inclusive.</param>
/// <param name="End">The last byte, inclusive.</param>
public readonly record struct ByteRange(long Start, long End)
{
    /// <summary>The number of bytes in the range.</summary>
    public long Length => End - Start + 1;

    /// <summary>The <c>Content-Range</c> header value for a file of <paramref name="totalLength"/> bytes.</summary>
    /// <param name="totalLength">The whole file's length.</param>
    /// <returns>For example <c>bytes 0-99/1000</c>.</returns>
    public string ContentRange(long totalLength) =>
        string.Create(CultureInfo.InvariantCulture, $"bytes {Start}-{End}/{totalLength}");

    /// <summary>Reads a <c>Range</c> header.</summary>
    /// <param name="header">The header's value, or null when there is none.</param>
    /// <param name="totalLength">The file's length.</param>
    /// <param name="range">The range to send, when the result is <see cref="RangeResult.Partial"/>.</param>
    /// <returns>Whether to send the whole file, a part, or 416 Range Not Satisfiable.</returns>
    public static RangeResult Parse(string? header, long totalLength, out ByteRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(header))
        {
            return RangeResult.Whole;
        }

        var value = header.Trim();
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || value.Contains(','))
        {
            return RangeResult.Whole;
        }

        var spec = value["bytes=".Length..].Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return RangeResult.Whole;
        }

        var first = spec[..dash].Trim();
        var last = spec[(dash + 1)..].Trim();
        const NumberStyles Digits = NumberStyles.None;

        if (first.Length == 0)
        {
            // "bytes=-500": the last 500 bytes.
            if (!long.TryParse(last, Digits, CultureInfo.InvariantCulture, out var suffix))
            {
                return RangeResult.Whole;
            }

            if (suffix == 0 || totalLength == 0)
            {
                return RangeResult.NotSatisfiable;
            }

            range = new ByteRange(Math.Max(0, totalLength - suffix), totalLength - 1);
            return RangeResult.Partial;
        }

        if (!long.TryParse(first, Digits, CultureInfo.InvariantCulture, out var start))
        {
            return RangeResult.Whole;
        }

        if (start >= totalLength)
        {
            return RangeResult.NotSatisfiable;
        }

        var end = totalLength - 1;
        if (last.Length > 0)
        {
            if (!long.TryParse(last, Digits, CultureInfo.InvariantCulture, out var requestedEnd) || requestedEnd < start)
            {
                return RangeResult.Whole;
            }

            end = Math.Min(requestedEnd, totalLength - 1);
        }

        range = new ByteRange(start, end);
        return RangeResult.Partial;
    }
}

/// <summary>How to answer a request for a file, given its <c>Range</c> header.</summary>
public enum RangeResult
{
    /// <summary>No usable range: send the whole file with 200.</summary>
    Whole,

    /// <summary>Send one range with 206 Partial Content.</summary>
    Partial,

    /// <summary>The range starts past the end: send 416 Range Not Satisfiable.</summary>
    NotSatisfiable,
}
