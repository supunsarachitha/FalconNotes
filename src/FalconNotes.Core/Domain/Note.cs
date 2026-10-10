namespace FalconNotes.Core.Domain;

/// <summary>A note as the screens use it: its text, state, tags, files and labels.</summary>
/// <param name="Id">The note's ID (UUID v7).</param>
/// <param name="Kind">What the note is for.</param>
/// <param name="Content">The Markdown text.</param>
/// <param name="DailyDate">The day it is the daily note of, if any.</param>
/// <param name="IsPinned">Pinned to the top.</param>
/// <param name="ArchivedAtUtc">When it was archived, or null.</param>
/// <param name="TrashedAtUtc">When it went to the trash, or null.</param>
/// <param name="CreatedAtUtc">When it was created; never changes.</param>
/// <param name="UpdatedAtUtc">When its text or files last changed.</param>
/// <param name="Revision">Goes up by one on every change.</param>
/// <param name="Tags">Its tags, sorted ordinally.</param>
/// <param name="Attachments">Its files, oldest first.</param>
/// <param name="LabelIds">Its labels' IDs, sorted.</param>
public sealed record Note(
    Guid Id,
    NoteKind Kind,
    string Content,
    DateOnly? DailyDate,
    bool IsPinned,
    DateTime? ArchivedAtUtc,
    DateTime? TrashedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    long Revision,
    IReadOnlyList<string> Tags,
    IReadOnlyList<Attachment> Attachments,
    IReadOnlyList<Guid> LabelIds)
{
    /// <summary>Whether the note is in the archive.</summary>
    public bool IsArchived => ArchivedAtUtc is not null;

    /// <summary>Whether the note is in the trash.</summary>
    public bool IsTrashed => TrashedAtUtc is not null;
}

/// <summary>A file attached to a note, or added while a note is being written.</summary>
/// <param name="Id">The attachment's ID (UUID v7).</param>
/// <param name="NoteId">Its note, or null while the note is being written.</param>
/// <param name="FileName">The sanitised file name.</param>
/// <param name="ContentType">Its type.</param>
/// <param name="SizeBytes">Its size before encryption.</param>
/// <param name="CreatedAtUtc">When it was added.</param>
/// <param name="Revision">Goes up when its content is replaced; part of the media URL.</param>
public sealed record Attachment(
    Guid Id, Guid? NoteId, string FileName, string ContentType, long SizeBytes, DateTime CreatedAtUtc, long Revision)
{
    /// <summary>Whether the file is an image the app shows inline.</summary>
    public bool IsImage => Attachments.UploadPolicy.IsImage(ContentType);
}

/// <summary>A coloured label put on notes by hand.</summary>
/// <param name="Id">The label's ID (UUID v7).</param>
/// <param name="Name">Its name, 1–40 characters.</param>
/// <param name="Color">Its colour.</param>
/// <param name="CreatedAtUtc">When it was created.</param>
/// <param name="HideNotes">
/// Whether notes and quick notes with this label are left out of Home and the Quick notes tab, and shown only on the
/// label's own page. Removing the label from a note, or turning this off, brings the note back.
/// </param>
public sealed record Label(Guid Id, string Name, LabelColor Color, DateTime CreatedAtUtc, bool HideNotes = false);

/// <summary>Which notes a list holds (docs/04, Lists).</summary>
public enum NoteState
{
    /// <summary>Not pinned, not archived, not in the trash.</summary>
    Feed,

    /// <summary>Pinned, not archived, not in the trash.</summary>
    Pinned,

    /// <summary>Not archived, not in the trash: searches and filters.</summary>
    Active,

    /// <summary>Archived, not in the trash.</summary>
    Archived,

    /// <summary>In the trash; newest deleted first.</summary>
    Trash,
}

/// <summary>Where the next page of a list starts: after this time and ID (keyset paging).</summary>
/// <param name="Time">The last note's creation time, or for the trash the time it was deleted.</param>
/// <param name="Id">The last note's ID.</param>
public readonly record struct NoteCursor(DateTime Time, Guid Id);

/// <summary>A page of notes and where the next one starts.</summary>
/// <param name="Items">The notes.</param>
/// <param name="Next">The next page's cursor, or null at the end.</param>
public sealed record NotePage(IReadOnlyList<Note> Items, NoteCursor? Next);

/// <summary>How many notes and files a deletion removed.</summary>
/// <param name="Notes">Notes deleted.</param>
/// <param name="Files">Files deleted.</param>
public sealed record DeletedCount(int Notes, int Files);

/// <summary>A problem the user can fix or must be told about, with the message to show (docs/07, toasts).</summary>
/// <param name="message">The message, in the app's wording.</param>
/// <param name="field">The field it concerns, if any.</param>
public class UserFacingException(string message, string? field = null) : Exception(message)
{
    /// <summary>The field it concerns, if any.</summary>
    public string? Field { get; } = field;
}

/// <summary>The day already has a daily note: post into it instead (docs/04, Daily notes).</summary>
/// <param name="date">The day.</param>
public sealed class DailyNoteExistsException(DateOnly date)
    : UserFacingException("This day already has a daily note.", "dailyDate")
{
    /// <summary>The day.</summary>
    public DateOnly Date { get; } = date;
}
