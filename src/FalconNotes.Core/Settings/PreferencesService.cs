using FalconNotes.Core.Domain;
using FalconNotes.Core.Events;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Settings;

/// <summary>
/// Reads and saves the preferences (docs/04, Preferences): camelCase JSON in <c>Settings['preferences']</c>. Unknown
/// keys are ignored, missing ones take their defaults, and values that are not offered fall back to the default.
/// </summary>
/// <param name="storage">The open database.</param>
/// <param name="feed">Change events.</param>
public sealed class PreferencesService(StorageContext storage, ChangeFeed feed)
{
    private const string Key = "preferences";

    /// <summary>The saved preferences, or the defaults.</summary>
    /// <returns>The preferences.</returns>
    public Task<Preferences> GetAsync() =>
        storage.Database.ReadAsync(connection => Normalise(SettingsStore.Get<Preferences>(connection, Key) ?? new Preferences()));

    /// <summary>Saves the preferences.</summary>
    /// <param name="preferences">The preferences.</param>
    /// <returns>What was saved (with values that are not offered replaced by their defaults).</returns>
    public async Task<Preferences> SaveAsync(Preferences preferences)
    {
        var clean = Normalise(preferences);
        await storage.Database.InTransactionAsync((connection, transaction) =>
        {
            SettingsStore.Set(connection, Key, clean, transaction);
            return true;
        });
        feed.RaisePreferencesChanged();
        return clean;
    }

    /// <summary>Replaces values that are not offered (an unknown date format, a stray menu order) with defaults.</summary>
    /// <param name="preferences">The preferences.</param>
    /// <returns>The cleaned preferences.</returns>
    public static Preferences Normalise(Preferences preferences) => preferences with
    {
        DateFormat = DateFormats.All.Contains(preferences.DateFormat) ? preferences.DateFormat : "yyyy-MM-dd",
        Theme = Enum.IsDefined(preferences.Theme) ? preferences.Theme : Theme.System,
        Accent = Enum.IsDefined(preferences.Accent) ? preferences.Accent : Accent.Falcon,
        MenuTextSize = Enum.IsDefined(preferences.MenuTextSize) ? preferences.MenuTextSize : MenuTextSize.Medium,
        WeekStart = Enum.IsDefined(preferences.WeekStart) ? preferences.WeekStart : WeekStart.Auto,
        MenuOrder = MenuOrder.Save(MenuOrder.Read(preferences.MenuOrder ?? "")),
    };
}
