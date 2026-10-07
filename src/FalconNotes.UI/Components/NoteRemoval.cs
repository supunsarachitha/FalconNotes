using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.State;

namespace FalconNotes.UI.Components;

/// <summary>
/// Deleting a note, todo list or habit (port of the <c>useRemoveNote</c> hook in components/NoteRemoval.tsx), as a
/// plain helper rather than a component per the port map: the caller renders a <see cref="ConfirmDialog"/> bound to
/// the state this exposes. With the trash on (the default) it moves to the trash at once, and the toast offers
/// Undo; with the trash off, the dialog asks first and it is deleted for good.
/// </summary>
/// <param name="note">The note, list or habit.</param>
/// <param name="noun">"note", "list" or "habit".</param>
/// <param name="confirmTitle">The confirmation's title when the trash is off, e.g. "Delete this note?".</param>
/// <param name="confirmDescription">What the confirmation says is deleted for good.</param>
/// <param name="notes">Trashes, restores or deletes the note.</param>
/// <param name="state">Reads the trash preference.</param>
/// <param name="toasts">Shows the outcome, with Undo.</param>
/// <param name="onChanged">Called after anything here changes what the caller should show.</param>
public sealed class NoteRemoval(
    Note note, string noun, string confirmTitle, string confirmDescription, NoteService notes, AppState state, Toasts toasts, Action onChanged)
{
    /// <summary>Whether the trash is on: deleting moves there instead of asking first.</summary>
    public bool InTrash => state.Preferences.Trash;

    /// <summary>The menu's label for this action.</summary>
    public string MenuLabel => InTrash ? "Move to trash" : "Delete…";

    /// <summary>The confirmation dialog's title (trash off only).</summary>
    public string ConfirmTitle => confirmTitle;

    /// <summary>The confirmation dialog's description (trash off only).</summary>
    public string ConfirmDescription => confirmDescription;

    /// <summary>Whether the confirmation dialog is open.</summary>
    public bool Confirming { get; private set; }

    /// <summary>Whether the confirmed delete is running.</summary>
    public bool Removing { get; private set; }

    /// <summary>Starts removing it: trashes at once, or opens the confirmation.</summary>
    public async Task StartAsync()
    {
        if (!InTrash)
        {
            Confirming = true;
            onChanged();
            return;
        }

        try
        {
            await notes.PatchAsync(note.Id, new NotePatch(IsTrashed: true));
            toasts.Info($"{Capitalize(noun)} moved to the trash.", new ToastAction("Undo", RestoreAsync));
        }
        catch (Exception)
        {
            toasts.Error($"Could not move the {noun} to the trash. Please try again.");
        }
    }

    /// <summary>Closes the confirmation without deleting anything.</summary>
    public void CancelConfirm()
    {
        Confirming = false;
        onChanged();
    }

    /// <summary>Deletes it for good (the confirmation's danger button).</summary>
    public async Task ConfirmAsync()
    {
        Removing = true;
        onChanged();
        try
        {
            await notes.DeleteAsync(note.Id);
            Confirming = false;
            toasts.Info($"{Capitalize(noun)} deleted.");
        }
        catch (Exception)
        {
            toasts.Error($"Could not delete the {noun}.");
        }
        finally
        {
            Removing = false;
            onChanged();
        }
    }

    private async Task RestoreAsync()
    {
        try
        {
            await notes.PatchAsync(note.Id, new NotePatch(IsTrashed: false));
        }
        catch (Exception)
        {
            toasts.Error("Could not restore it. Find it in Settings → Backup & data → Trash.");
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
