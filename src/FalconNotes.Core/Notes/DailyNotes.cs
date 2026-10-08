using FalconNotes.Core.Domain;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Notes;

/// <summary>
/// Saving the Today card and its template (docs/04, Daily notes). Port of <c>saveDailyNote</c>, <c>templateText</c> and
/// <c>useDailyTemplate</c> in <c>web/lib/daily.ts</c>.
/// </summary>
/// <param name="notes">The note service.</param>
public sealed class DailyNotes(NoteService notes)
{
    /// <summary>The text a template note gives a new daily note: everything but its title, since the daily note has the date.</summary>
    /// <param name="content">The template note's text.</param>
    /// <returns>The text to start with.</returns>
    public static string TemplateText(string content) => Titles.Split(content).Body;

    /// <summary>
    /// The daily-note template, when one is chosen: an ordinary note, whose text starts each new daily note. A template
    /// that was deleted, or is in the trash, counts as none.
    /// </summary>
    /// <param name="preferences">The preferences, which hold the template's ID.</param>
    /// <returns>The template note, or null.</returns>
    public async Task<Note?> GetTemplateAsync(Preferences preferences)
    {
        if (!preferences.DailyNotes || !Guid.TryParseExact(preferences.DailyNoteTemplate, "D", out var id))
        {
            return null;
        }

        return await notes.GetAsync(id) is { TrashedAtUtc: null } note ? note : null;
    }

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
