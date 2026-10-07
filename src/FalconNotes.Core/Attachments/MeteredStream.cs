namespace FalconNotes.Core.Attachments;

/// <summary>
/// Reads through another stream, counting bytes for progress and refusing more than a limit. Adapted from the
/// server's <c>LengthLimitedStream</c>.
/// </summary>
/// <param name="inner">The source.</param>
/// <param name="limit">The most bytes allowed.</param>
/// <param name="progress">Told how many bytes have been read so far, if given.</param>
internal sealed class MeteredStream(Stream inner, long limit, IProgress<long>? progress) : Stream
{
    /// <summary>Bytes read so far.</summary>
    public long BytesRead { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

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

    private int Count(int read)
    {
        BytesRead += read;
        if (BytesRead > limit)
        {
            throw new FileTooLargeException();
        }

        progress?.Report(BytesRead);
        return read;
    }
}
