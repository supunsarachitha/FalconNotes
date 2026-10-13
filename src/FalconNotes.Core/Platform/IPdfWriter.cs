using FalconNotes.Core.Pdf;

namespace FalconNotes.Core.Platform;

/// <summary>
/// Sets a note on pages and writes them as a PDF, for Export to PDF (docs/04, Export to PDF; docs/12). Implemented
/// per platform with the system's own PDF and text drawing, so the app gains no library. A platform that has none
/// yet registers none, and Settings then does not offer the feature.
/// </summary>
public interface IPdfWriter
{
    /// <summary>Writes the PDF: A4 pages, black on white whatever the app's theme.</summary>
    /// <param name="note">What to draw.</param>
    /// <param name="output">Where the PDF goes.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the whole file is written.</returns>
    Task WriteAsync(PdfNote note, Stream output, CancellationToken cancellationToken = default);
}
