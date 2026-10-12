namespace FalconNotes.Core.Notes;

/// <summary>
/// Which locked notes are open to be read just now (docs/04, Locked notes). Unlocking a note with the PIN does not
/// take its lock off: it opens the note here, in memory only, and it locks again by itself when the page or the app
/// is left (<see cref="CloseAll"/>), when the app lock locks, and when the app is closed, since nothing here is saved.
/// A note being edited is held open until the edit ends, so that leaving the app for a moment, to pick a file for it,
/// does not take the editor's files away or lose what was typed.
/// </summary>
public sealed class NoteLocks
{
    private readonly object _gate = new();
    private readonly HashSet<Guid> _open = [];
    private readonly HashSet<Guid> _held = [];
    private readonly HashSet<Guid> _closeWhenReleased = [];

    /// <summary>Raised when a note opens or locks again; may run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Whether a locked note is open to be read.</summary>
    /// <param name="noteId">The note.</param>
    /// <returns>Whether it was unlocked and has not locked again since.</returns>
    public bool IsOpen(Guid noteId)
    {
        lock (_gate)
        {
            return _open.Contains(noteId);
        }
    }

    /// <summary>Opens a locked note. The caller has already checked the PIN or biometrics.</summary>
    /// <param name="noteId">The note.</param>
    public void Open(Guid noteId)
    {
        lock (_gate)
        {
            _open.Add(noteId);
            _closeWhenReleased.Remove(noteId);
        }

        Changed?.Invoke();
    }

    /// <summary>Locks one note again at once (Lock now), or forgets it when its lock was taken off or it was deleted.</summary>
    /// <param name="noteId">The note.</param>
    public void Close(Guid noteId)
    {
        bool closed;
        lock (_gate)
        {
            closed = _open.Remove(noteId);
            _held.Remove(noteId);
            _closeWhenReleased.Remove(noteId);
        }

        if (closed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Locks every open note again: the page was left, the app went to the background, or the app lock locked. A note
    /// held for an edit locks when the edit ends instead.
    /// </summary>
    public void CloseAll()
    {
        bool closed;
        lock (_gate)
        {
            _closeWhenReleased.UnionWith(_held);
            closed = _open.RemoveWhere(id => !_held.Contains(id)) > 0;
        }

        if (closed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Keeps an open note open while it is being edited.</summary>
    /// <param name="noteId">The note.</param>
    public void Hold(Guid noteId)
    {
        lock (_gate)
        {
            if (_open.Contains(noteId))
            {
                _held.Add(noteId);
            }
        }
    }

    /// <summary>Ends a hold. If the page or the app was left meanwhile, the note locks now.</summary>
    /// <param name="noteId">The note.</param>
    public void Release(Guid noteId)
    {
        bool closed;
        lock (_gate)
        {
            _held.Remove(noteId);
            closed = _closeWhenReleased.Remove(noteId) && _open.Remove(noteId);
        }

        if (closed)
        {
            Changed?.Invoke();
        }
    }
}
