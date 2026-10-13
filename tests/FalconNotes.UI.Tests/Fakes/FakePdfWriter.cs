using FalconNotes.Core.Pdf;
using FalconNotes.Core.Platform;

namespace FalconNotes.UI.Tests.Fakes;

/// <summary>Records what was asked to be drawn, and writes a few bytes instead of a real PDF.</summary>
public sealed class FakePdfWriter : IPdfWriter
{
    /// <summary>Each note written, in order.</summary>
    public List<PdfNote> Written { get; } = [];

    /// <summary>Whether writing fails, as when the device runs out of room.</summary>
    public bool Fails { get; set; }

    /// <inheritdoc />
    public async Task WriteAsync(PdfNote note, Stream output, CancellationToken cancellationToken = default)
    {
        if (Fails)
        {
            throw new IOException("No room.");
        }

        Written.Add(note);
        await output.WriteAsync("%PDF-fake"u8.ToArray(), cancellationToken);
    }
}
