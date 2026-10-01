namespace FalconNotes.App.Services;

/// <summary>
/// A Java <c>InputStream</c> over a seekable .NET stream, for the WebView's intercepted responses (Phase 0 spike S3).
/// </summary>
/// <remarks>
/// Android's WebView applies a request's <c>Range</c> itself: it expects the whole file, checks the range against
/// <c>available()</c> and calls <c>skip()</c> to reach its start, and it takes <c>Content-Length</c> from
/// <c>available()</c> too. MAUI's own stream adapter reports 0 available and skips by reading, so ranges past the
/// start fail and video does not load. This stream reports the bytes left and skips by seeking, which decrypts only
/// the chunks that are then read. The WebView reads to the end of the stream, so a range that stops before the end of
/// the file sets <paramref name="limit"/>.
/// </remarks>
/// <param name="body">The whole response body, seekable; disposed when the WebView closes the stream.</param>
/// <param name="limit">Where the stream ends: the range's last byte + 1, or the file's length.</param>
internal sealed class MediaInputStream(Stream body, long limit) : Java.IO.InputStream
{
    /// <inheritdoc />
    public override int Available() => (int)Math.Min(int.MaxValue, Math.Max(0, limit - body.Position));

    /// <inheritdoc />
    public override long Skip(long byteCount)
    {
        if (byteCount <= 0)
        {
            return 0;
        }

        var start = body.Position;
        body.Position = Math.Min(limit, start + byteCount);
        return body.Position - start;
    }

    /// <inheritdoc />
    public override int Read() => body.Position < limit ? body.ReadByte() : -1;

    /// <inheritdoc />
    public override int Read(byte[]? buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var read = body.Read(buffer, offset, (int)Math.Min(count, Math.Max(0, limit - body.Position)));
        return read <= 0 ? -1 : read;
    }

    /// <inheritdoc />
    public override void Close()
    {
        body.Dispose();
        base.Close();
    }
}
