using FalconNotes.Core.Domain;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Notes;

/// <summary>
/// Saving the Today card (docs/04, Daily notes). Port of <c>saveDailyNote</c> in <c>web/lib/daily.ts</c>.
/// </summary>
/// <param name="notes">The note service.</param>
public sealed class DailyNotes(NoteService notes)
{
    /// <summary>
    /// Saves a day's first words as its daily note, titled with the date. If the day's note appeared meanwhile (a
    /// restore can add one), the words are added to it instead: its text, trimmed at the end, a blank line, then theirs,
    /// with the new files after its own.
    /// </summary>
    /// <param name="date">The day.</param>
    /// <param name="title">The title: the date in the chosen format.</param>
    /// <param name="body">What was written.</param>
    /// <param name="attachmentIds">Files added while writing.</param>
    /// <returns>The day's note.</returns>
    public async Task<Note> SaveAsync(DateOnly date, string title, string body, IReadOnlyList<Guid> attachmentIds)
    {
        try
        {
            return await notes.CreateAsync(Titles.Join(title, body), NoteKind.Note, attachmentIds, date);
        }
        catch (DailyNoteExistsException)
        {
            var existing = await notes.GetDailyAsync(date) ?? throw new InvalidOperationException("The day's note vanished.");
            var ids = existing.Attachments.Select(a => a.Id).Concat(attachmentIds).ToList();
            var content = body.Trim().Length > 0 ? $"{existing.Content.TrimEnd()}\n\n{body}" : existing.Content;
            return (await notes.UpdateAsync(existing.Id, content, ids))!;
        }
    }
}
