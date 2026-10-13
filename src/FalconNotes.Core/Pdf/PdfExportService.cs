using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Platform;

namespace FalconNotes.Core.Pdf;

/// <summary>
/// Saves one note as a PDF where the user chooses (docs/04, Export to PDF). Like an export, the file is written into
/// <c>cache/export/{random}.pdf</c>, handed to the system's save dialog under the note's first line as its name, and
/// deleted whatever happens. The PDF is not encrypted: it is a copy the user asked for, to keep outside the app.
/// </summary>
/// <param name="writer">Draws the pages.</param>
/// <param name="saver">The save dialog.</param>
/// <param name="directories">Where the cache is.</param>
/// <param name="time">The clock, for the device's time zone.</param>
public sealed partial class PdfExportService(IPdfWriter writer, IFileSaver saver, IAppDirectories directories, TimeProvider time)
{
    private const int MaxNameLength = 60;

    /// <summary>Writes the note's PDF and lets the user save it.</summary>
    /// <param name="note">The note.</param>
    /// <param name="content">Its text as the card shows it now.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>True when the file was saved; false when the user cancelled the save dialog.</returns>
    public async Task<bool> ExportAsync(Note note, string content, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(directories.CacheDirectory, "export");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.pdf");
        try
        {
            var use24Hour = !CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("tt", StringComparison.Ordinal);
            var pdf = await Task.Run(() => PdfNoteBuilder.Build(note, content, time.LocalTimeZone, use24Hour), cancellationToken);
            await using (var file = File.Create(path))
            {
                await writer.WriteAsync(pdf, file, cancellationToken);
            }

            await using var saved = File.OpenRead(path);
            return await saver.SaveAsync(FileName(content), saved, cancellationToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The name the save dialog suggests: the note's first line as a person would write it, such as
    /// <c>Shopping list.pdf</c>. Not an export's slug: that keeps letters and digits only, which takes the vowel
    /// signs out of Sinhala, Tamil, Hindi or Thai words and leaves them misspelt.
    /// </summary>
    /// <param name="content">The note's text.</param>
    /// <returns>The file name; <c>note.pdf</c> for a note with no words.</returns>
    public static string FileName(string content)
    {
        var line = content.Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.Length > 0) ?? "";
        line = LineMarkers().Replace(line, "");
        var withoutTags = Tags().Replace(line, " ");
        if (withoutTags.Any(char.IsLetterOrDigit))
        {
            line = withoutTags;
        }

        var name = new StringBuilder(MaxNameLength);
        var gap = false;
        foreach (var rune in line.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var joins = category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                || rune.Value is 0x200C or 0x200D; // the joiners that shape Sinhala and other conjuncts
            if (Rune.IsLetterOrDigit(rune) || (joins && name.Length > 0 && !gap) || (rune.Value is '-' or '_' && name.Length > 0 && !gap))
            {
                var text = rune.ToString();
                if (name.Length + text.Length + (gap ? 1 : 0) > MaxNameLength)
                {
                    break;
                }

                if (gap)
                {
                    name.Append(' ');
                }

                gap = false;
                name.Append(text);
            }
            else if (rune.Value is not ('\'' or '’') && name.Length > 0)
            {
                gap = true; // anything else, Markdown's marks too, separates words
            }
        }

        var result = name.ToString().TrimEnd('-', '_');
        return (result.Length == 0 ? "note" : result) + ".pdf";
    }

    /// <summary>Heading markers, quote markers, list bullets and task boxes at the start of a line.</summary>
    [GeneratedRegex(@"^(?:#{1,6}\s+|>\s*|[-*+]\s+(?:\[[ xX]\]\s+)?|\d+[.)]\s+)+", RegexOptions.CultureInvariant)]
    private static partial Regex LineMarkers();

    /// <summary>#tags, as the note tag parser recognises them.</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}_/#&])#[\p{L}\p{N}_][\p{L}\p{N}_/-]*", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();
}
