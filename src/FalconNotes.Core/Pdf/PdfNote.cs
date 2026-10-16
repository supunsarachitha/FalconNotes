namespace FalconNotes.Core.Pdf;

/// <summary>
/// A note laid out for a PDF (docs/04, Export to PDF): what to draw, in order, with nothing of how. Each platform's
/// <see cref="Platform.IPdfWriter"/> sets it on pages with its own text engine, so every script and emoji the device
/// can show comes out right, and Core needs no PDF library.
/// </summary>
/// <param name="Blocks">The blocks, from the top of the first page.</param>
public sealed record PdfNote(IReadOnlyList<PdfBlock> Blocks);

/// <summary>One thing set under the last: a paragraph, a rule, a table or a picture.</summary>
public abstract record PdfBlock;

/// <summary>How a <see cref="PdfText"/> is set.</summary>
public enum PdfTextStyle
{
    /// <summary>A paragraph or a list item.</summary>
    Body,

    /// <summary>A first-level heading: a note's title.</summary>
    Heading1,

    /// <summary>A second-level heading.</summary>
    Heading2,

    /// <summary>A third-level heading.</summary>
    Heading3,

    /// <summary>A fourth, fifth or sixth-level heading.</summary>
    Heading4,

    /// <summary>A code block: one width for every character, on a tint, with its line breaks kept.</summary>
    Code,

    /// <summary>Small grey text that is about the note, not of it: when it was written, which files are not shown.</summary>
    Caption,
}

/// <summary>A run of text in one style.</summary>
/// <param name="Text">The text; a <c>\n</c> in it starts a new line.</param>
/// <param name="Bold">Bold.</param>
/// <param name="Italic">Italic.</param>
/// <param name="Strikethrough">Struck through.</param>
/// <param name="Code">Inline code.</param>
/// <param name="Link">A link's text. Paper has nothing to tap, so the address follows it as text where it differs.</param>
public sealed record PdfSpan(
    string Text, bool Bold = false, bool Italic = false, bool Strikethrough = false, bool Code = false, bool Link = false);

/// <summary>A paragraph, heading, list item or code block.</summary>
/// <param name="Style">How it is set.</param>
/// <param name="Spans">Its text.</param>
/// <param name="Indent">How many list levels deep it is; 0 outside a list.</param>
/// <param name="Marker">A list item's bullet or number, drawn to the left of its first line; null for none.</param>
/// <param name="Checked">A task item's box, ticked or not, drawn in place of a marker; null when it is not one.</param>
/// <param name="Quote">How many quotes deep it is; each draws a bar down its left.</param>
public sealed record PdfText(
    PdfTextStyle Style, IReadOnlyList<PdfSpan> Spans, int Indent = 0, string? Marker = null, bool? Checked = null, int Quote = 0)
    : PdfBlock;

/// <summary>A line across the page.</summary>
public sealed record PdfRule : PdfBlock;

/// <summary>A table. Its first row is the header.</summary>
/// <param name="Rows">The rows, each a list of cells, each cell its text.</param>
public sealed record PdfTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<PdfSpan>>> Rows) : PdfBlock;

/// <summary>One of the note's attached pictures. The writer reads it from the attachment store.</summary>
/// <param name="AttachmentId">The attachment.</param>
/// <param name="FileName">Its name, set in its place when the device cannot decode it.</param>
/// <param name="IsSticker">A sticker: drawn small, at most an inch across, as the screen shows it (docs/04, Stickers).</param>
public sealed record PdfPicture(Guid AttachmentId, string FileName, bool IsSticker = false) : PdfBlock;
