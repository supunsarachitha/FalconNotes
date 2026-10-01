namespace FalconNotes.Core.Events;

/// <summary>
/// Tells the screens that data changed, in place of the web app's query cache (docs/02, Runtime model). Services raise
/// an event after their change has committed; lists, counts and the calendar reload. Handlers may run on any thread:
/// components must hop to the UI thread (<c>InvokeAsync</c>) and unsubscribe in <c>Dispose</c>.
/// </summary>
public sealed class ChangeFeed
{
    /// <summary>A note, its files, tags or labels changed.</summary>
    public event Action? NotesChanged;

    /// <summary>A label was created, renamed, recoloured or deleted.</summary>
    public event Action? LabelsChanged;

    /// <summary>A preference changed.</summary>
    public event Action? PreferencesChanged;

    /// <summary>The profile (display name) changed.</summary>
    public event Action? ProfileChanged;

    /// <summary>Raises <see cref="NotesChanged"/>.</summary>
    public void RaiseNotesChanged() => NotesChanged?.Invoke();

    /// <summary>Raises <see cref="LabelsChanged"/>.</summary>
    public void RaiseLabelsChanged() => LabelsChanged?.Invoke();

    /// <summary>Raises <see cref="PreferencesChanged"/>.</summary>
    public void RaisePreferencesChanged() => PreferencesChanged?.Invoke();

    /// <summary>Raises <see cref="ProfileChanged"/>.</summary>
    public void RaiseProfileChanged() => ProfileChanged?.Invoke();
}
