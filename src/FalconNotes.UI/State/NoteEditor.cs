using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;

namespace FalconNotes.UI.State;

/// <summary>
/// Edits a note whose text has a structure, such as a todo list or a habit (port of <c>lib/noteEditor.ts</c>).
/// <see cref="Value"/> shows every change at once; each call to <see cref="Commit"/> saves it in the background, one
/// save after another. <see cref="Reload"/> must be called whenever the component re-renders with a note read again
/// elsewhere (after <see cref="Core.Events.ChangeFeed.NotesChanged"/>): it replaces <see cref="Value"/> unless a save
/// of ours is still in flight, or the note is not newer than the one we last saved, so a stale read never undoes an
/// edit that is on its way to the database.
/// </summary>
/// <typeparam name="T">The parsed shape of the note's text.</typeparam>
public sealed class NoteEditor<T>
{
    private readonly NoteService _notes;
    private readonly Func<string, T> _parse;
    private readonly Func<T, string> _serialize;
    private readonly Action<Exception> _onError;
    private readonly Guid _noteId;
    private IReadOnlyList<Guid> _attachmentIds;
    private Task _saving = Task.CompletedTask;
    private int _pending;
    private (DateTime At, string Content) _lastSaved;

    /// <summary>Starts editing a note.</summary>
    /// <param name="notes">Saves each change.</param>
    /// <param name="note">The note, as first shown.</param>
    /// <param name="parse">Turns the note's text into the edited shape; must not change between calls.</param>
    /// <param name="serialize">Turns the edited shape back into text; must not change between calls.</param>
    /// <param name="onError">Runs when a save fails.</param>
    public NoteEditor(NoteService notes, Note note, Func<string, T> parse, Func<T, string> serialize, Action<Exception> onError)
    {
        _notes = notes;
        _parse = parse;
        _serialize = serialize;
        _onError = onError;
        _noteId = note.Id;
        _attachmentIds = note.Attachments.Select(a => a.Id).ToList();
        _lastSaved = (note.UpdatedAtUtc, note.Content);
        Value = parse(note.Content);
    }

    /// <summary>The note's text, as edited.</summary>
    public T Value { get; private set; }

    /// <summary>
    /// Call whenever the note is read again elsewhere. Always keeps the attachment IDs a future <see cref="Commit"/>
    /// saves with up to date; replaces <see cref="Value"/> too, unless a save of ours is still in flight or the note
    /// is not newer than the one we last saved.
    /// </summary>
    /// <param name="note">The note as read again.</param>
    public void Reload(Note note)
    {
        _attachmentIds = note.Attachments.Select(a => a.Id).ToList();
        if (_pending > 0)
        {
            return;
        }

        if (note.UpdatedAtUtc < _lastSaved.At || (note.UpdatedAtUtc == _lastSaved.At && note.Content != _lastSaved.Content))
        {
            return;
        }

        Value = _parse(note.Content);
    }

    /// <summary>Shows a change at once and saves it in the background, after any save already under way.</summary>
    /// <param name="next">The new value.</param>
    public void Commit(T next)
    {
        Value = next;
        _pending++;
        var content = _serialize(next);
        var attachmentIds = _attachmentIds;
        _saving = _saving.ContinueWith(_ => SaveAsync(content, attachmentIds), TaskScheduler.Default).Unwrap();
    }

    private async Task SaveAsync(string content, IReadOnlyList<Guid> attachmentIds)
    {
        try
        {
            var saved = await _notes.UpdateAsync(_noteId, content, attachmentIds);
            if (saved is not null)
            {
                _lastSaved = (saved.UpdatedAtUtc, saved.Content);
            }
        }
        catch (Exception e)
        {
            _onError(e);
        }
        finally
        {
            _pending--;
        }
    }
}
