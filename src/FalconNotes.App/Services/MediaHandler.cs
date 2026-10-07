using System.Globalization;
using FalconNotes.Core.Attachments;
using Microsoft.Extensions.Logging;

namespace FalconNotes.App.Services;

/// <summary>
/// Answers <c>/_media/{attachmentId}</c> requests from the page with the decrypted attachment, honouring
/// <c>Range</c> so videos seek (docs/02, Serving attachments to the WebView). Uses <c>BlazorWebView</c>'s
/// <c>WebResourceRequested</c> (option A; Phase 0 spike S3). Reads nothing else from the request and never serves a
/// path outside the attachment store.
/// </summary>
/// <param name="source">Finds attachments by ID.</param>
/// <param name="logger">Logs IDs and counts, never names.</param>
public sealed class MediaHandler(IMediaSource source, ILogger<MediaHandler> logger)
{
    /// <summary>The path prefix the handler answers.</summary>
    public const string PathPrefix = "/_media/";

    /// <summary>The passive types the page may display; everything else is sent as <c>application/octet-stream</c>.</summary>
    private static readonly HashSet<string> InlineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/avif", "image/bmp",
        "video/mp4", "video/webm",
        "audio/mpeg", "audio/mp4", "audio/ogg", "audio/wav", "audio/webm", "audio/flac",
        "text/plain",
    };

    /// <summary>Handles the request when it is for <see cref="PathPrefix"/>; leaves every other request alone.</summary>
    /// <param name="e">The request.</param>
    public void Handle(WebViewWebResourceRequestedEventArgs e)
    {
        if (!e.Uri.AbsolutePath.StartsWith(PathPrefix, StringComparison.Ordinal))
        {
            return;
        }

        e.Handled = true;
        if (!string.Equals(e.Method, "GET", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(e.Uri.AbsolutePath[PathPrefix.Length..], "D", out var id))
        {
            Respond(e, 404, "Not Found", new Dictionary<string, string> { ["X-Content-Type-Options"] = "nosniff" }, new MemoryStream(), 0);
            return;
        }

        var rangeHeader = e.Headers.FirstOrDefault(h => string.Equals(h.Key, "Range", StringComparison.OrdinalIgnoreCase)).Value;
        var response = RespondAsync(id, rangeHeader);
        // The status and headers depend on the file, so the handler waits for the lookup (a database row and a file
        // header); the body itself streams. On Android this runs on the WebView's I/O thread, not the UI thread.
        var (status, reason, headers, body, limit) = response.GetAwaiter().GetResult();
        logger.LogDebug("Media {Id}: range {Range} -> {Status}", id, rangeHeader, status);
        Respond(e, status, reason, headers, body, limit);
    }

    /// <summary>
    /// Sends the response. On Android it builds the native response itself (Phase 0 spike S3): MAUI's
    /// <c>SetResponse</c> repeats the <c>Content-Type</c> header ("video/mp4, video/mp4"), and its stream adapter
    /// neither reports a length nor seeks, which breaks ranges (see <c>MediaInputStream</c>). A 64 KiB
    /// <c>BufferedInputStream</c> also cuts the calls across the Java bridge 32-fold (the WebView reads 2 KB at a time).
    /// </summary>
    private static void Respond(
        WebViewWebResourceRequestedEventArgs e, int status, string reason, Dictionary<string, string> headers, Stream body, long limit)
    {
#if ANDROID
        headers.Remove("Content-Type", out var contentType);
        headers.Remove("Content-Length");
        var input = new Android.Runtime.InputStreamInvoker(
            new Java.IO.BufferedInputStream(new Android.Runtime.InputStreamInvoker(new MediaInputStream(body, limit)), 64 * 1024));
        e.PlatformArgs!.Response = new Android.Webkit.WebResourceResponse(
            contentType ?? "application/octet-stream", null, status, reason, headers, input);
#else
        _ = limit;
        e.SetResponse(status, reason, headers, body);
#endif
    }

    private async Task<(int Status, string Reason, Dictionary<string, string> Headers, Stream Body, long Limit)> RespondAsync(
        Guid id, string? rangeHeader)
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Content-Type-Options"] = "nosniff",
            ["Cache-Control"] = "no-store",
            ["Accept-Ranges"] = "bytes",
        };

        MediaFile? file;
        try
        {
            file = await source.OpenAsync(id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Attachment {Id} could not be opened.", id);
            return (500, "Internal Server Error", headers, new MemoryStream(), 0);
        }

        if (file is null)
        {
            return (404, "Not Found", headers, new MemoryStream(), 0);
        }

        var length = file.Content.Length;
        headers["Content-Type"] = InlineTypes.Contains(file.ContentType) ? file.ContentType : "application/octet-stream";
        switch (ByteRange.Parse(rangeHeader, length, out var range))
        {
            case RangeResult.Partial:
                headers["Content-Range"] = range.ContentRange(length);
                headers["Content-Length"] = range.Length.ToString(CultureInfo.InvariantCulture);
#if ANDROID
                // The WebView skips to the range itself (see MediaInputStream), so it gets the whole file.
                return (206, "Partial Content", headers, file.Content, range.End + 1);
#else
                return (206, "Partial Content", headers, new RangeStream(file.Content, range.Start, range.Length), range.Length);
#endif
            case RangeResult.NotSatisfiable:
                await file.Content.DisposeAsync();
                headers["Content-Range"] = string.Create(CultureInfo.InvariantCulture, $"bytes */{length}");
                return (416, "Range Not Satisfiable", headers, new MemoryStream(), 0);
            default:
                headers["Content-Length"] = length.ToString(CultureInfo.InvariantCulture);
                return (200, "OK", headers, file.Content, length);
        }
    }
}
