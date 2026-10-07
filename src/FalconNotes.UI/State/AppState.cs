using FalconNotes.Core.Domain;
using FalconNotes.Core.Events;
using FalconNotes.Core.Settings;

namespace FalconNotes.UI.State;

/// <summary>
/// What every screen needs to know: the profile and the preferences (port of the web app's sign-in status and
/// <c>lib/preferences.ts</c>). A preference change shows at once and is saved in the background; if saving fails it is
/// rolled back with a message (docs/04, Preferences).
/// </summary>
/// <param name="preferences">Reads and saves the preferences.</param>
/// <param name="profiles">Reads the profile.</param>
/// <param name="feed">Change events.</param>
/// <param name="toasts">Shows the rollback message.</param>
public sealed class AppState(PreferencesService preferences, ProfileService profiles, ChangeFeed feed, Toasts toasts)
{
    private readonly SemaphoreSlim _saving = new(1, 1);

    /// <summary>Raised when the profile or the preferences change; may run on any thread.</summary>
    public event Action? Changed;

    /// <summary>The profile, or null before the Welcome screen created it.</summary>
    public Profile? Profile { get; private set; }

    /// <summary>The preferences as the screens should show them now.</summary>
    public Preferences Preferences { get; private set; } = new();

    /// <summary>The kinds of notes the user has turned on.</summary>
    public IReadOnlyList<NoteKind> EnabledKinds => NoteKinds.Enabled(Preferences);

    /// <summary>Loads the profile and preferences, after start-up opened the database.</summary>
    /// <returns>A task that completes when they are loaded.</returns>
    public async Task LoadAsync()
    {
        Profile = await profiles.GetAsync();
        Preferences = await preferences.GetAsync();
        feed.ProfileChanged -= OnProfileChanged;
        feed.ProfileChanged += OnProfileChanged;
        Changed?.Invoke();
    }

    /// <summary>Forgets everything, as erasing all data does.</summary>
    public void Clear()
    {
        Profile = null;
        Preferences = new Preferences();
        Changed?.Invoke();
    }

    /// <summary>Changes preferences: shown at once, saved in the background, rolled back if saving fails.</summary>
    /// <param name="change">The change.</param>
    /// <returns>A task that completes when the change is saved or rolled back.</returns>
    public async Task UpdatePreferencesAsync(Func<Preferences, Preferences> change)
    {
        var before = Preferences;
        Preferences = PreferencesService.Normalise(change(before));
        Changed?.Invoke();
        await _saving.WaitAsync();
        try
        {
            await preferences.SaveAsync(Preferences);
        }
        catch (Exception)
        {
            Preferences = before;
            Changed?.Invoke();
            toasts.Error("Your setting could not be saved. Please try again.");
        }
        finally
        {
            _saving.Release();
        }
    }

    private async void OnProfileChanged()
    {
        try
        {
            Profile = await profiles.GetAsync();
            Changed?.Invoke();
        }
        catch (InvalidOperationException)
        {
            // The database closed (erase): nothing to show.
        }
    }
}
