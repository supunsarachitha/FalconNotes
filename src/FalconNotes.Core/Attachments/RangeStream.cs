namespace FalconNotes.Core.Attachments;

/// <summary>A read-only window of <paramref name="length"/> bytes onto a seekable stream, which it owns.</summary>
/// <param name="inner">The whole file; positioned at the window's start by the constructor.</param>
/// <param name="start">Where the window starts.</param>
/// <param name="length">How many bytes it holds.</param>
public sealed class RangeStream(Stream inner, long start, long length) : Stream
{
    private readonly long _start = SeekTo(inner, start);
    private long _read;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => length;

    /// <inheritdoc />
    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var remaining = length - _read;
        if (remaining <= 0)
        {
            return 0;
        }

        var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
        _read += read;
        return read;
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var remaining = length - _read;
        if (remaining <= 0)
        {
            return 0;
        }

        var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, remaining)], cancellationToken);
        _read += read;
        return read;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private static long SeekTo(Stream stream, long position)
    {
        stream.Position = position;
        return position;
    }
}
