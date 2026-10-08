using FalconNotes.Core.Domain;

namespace FalconNotes.Core.Backup.Restore;

/// <summary>A file chosen for a restore.</summary>
/// <param name="Name">The file's name.</param>
/// <param name="OpenReadAsync">Opens it; it need not be seekable.</param>
/// <param name="ModifiedUtc">When it was last changed, if the platform says; stands in for missing note dates.</param>
public sealed record RestoreSource(string Name, Func<Task<Stream>> OpenReadAsync, DateTime? ModifiedUtc = null);

/// <summary>A file a restored note brings.</summary>
/// <param name="Name">Its name.</param>
/// <param name="ContentType">Its type.</param>
/// <param name="Size">Its size.</param>
/// <param name="Open">Opens its content.</param>
public sealed record RestoreAttachment(string Name, string ContentType, long Size, Func<Stream> Open);

/// <summary>A note read from a backup, ready to restore. Port of <c>ImportItem</c> in <c>web/import/parse.ts</c>.</summary>
/// <param name="Source">Where it came from, for messages: a file name or a path inside an archive.</param>
/// <param name="Id">Its original ID, or null for a file that is not from an export.</param>
/// <param name="Content">Its text.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it was last edited.</param>
/// <param name="Pinned">Whether it was pinned.</param>
/// <param name="Archived">Whether it was archived.</param>
/// <param name="Kind">What it is.</param>
/// <param name="DailyDate">The day it is the daily note of (<c>yyyy-MM-dd</c>), if any.</param>
/// <param name="Labels">The names of its labels.</param>
/// <param name="Attachments">Its files found in the archive.</param>
/// <param name="Missing">Its files the archive does not contain.</param>
public sealed record RestoreItem(
    string Source,
    Guid? Id,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool Pinned,
    bool Archived,
    NoteKind Kind,
    string? DailyDate,
    IReadOnlyList<string> Labels,
    IReadOnlyList<RestoreAttachment> Attachments,
    IReadOnlyList<string> Missing);

/// <summary>
/// What a restore found in the chosen files: the notes and the files that could not be read. Holds the archives open
/// until it is disposed, which also deletes their copies in <c>cache/restore/</c>.
/// </summary>
public sealed class RestorePlan : IDisposable
{
    private readonly List<IDisposable> _open = [];
    private readonly List<string> _copies = [];

    /// <summary>The notes, in the order of the files and of each archive's manifest.</summary>
    public List<RestoreItem> Items { get; } = [];

    /// <summary>Files or entries that could not be read, as messages.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>
    /// Label colours from the exports' manifests, by <see cref="Labels.LabelRules.NameKey"/>. A label a note names
    /// that is not here gets the next colour in turn.
    /// </summary>
    public Dictionary<string, LabelColor> LabelColors { get; } = new(StringComparer.Ordinal);

    /// <summary>How many files the notes bring.</summary>
    public int FileCount => Items.Sum(item => item.Attachments.Count);

    internal void Keep(IDisposable archive, string copy)
    {
        _open.Add(archive);
        _copies.Add(copy);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _open.ForEach(archive => archive.Dispose());
        _open.Clear();
        foreach (var copy in _copies)
        {
            try
            {
                File.Delete(copy);
            }
            catch (IOException)
            {
                // Removed at the next start with the rest of cache/restore/.
            }
        }

        _copies.Clear();
    }
}
