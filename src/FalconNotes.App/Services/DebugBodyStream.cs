#if DEBUG
namespace FalconNotes.App.Services;

/// <summary>Phase 0 spike S3 only: logs how much of a media response body the WebView read, and any error.</summary>
internal sealed class DebugBodyStream(Stream inner, string label) : Stream
{
    private long _read;
    private int _calls;

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            var n = inner.Read(buffer, offset, count);
            _read += n;
            if (_calls++ == 0)
            {
                Console.WriteLine($"FALCONSPIKE|BODY|{label} first read {n} of {count} thread={Environment.CurrentManagedThreadId}");
            }

            return n;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FALCONSPIKE|BODY|{label} read failed after {_read}: {ex.GetType().Name} {ex.Message}");
            throw;
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Console.WriteLine($"FALCONSPIKE|BODY|{label} disposed after {_read} bytes in {_calls} reads");
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
#endif
