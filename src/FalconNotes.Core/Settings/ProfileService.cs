using FalconNotes.Core.Domain;
using FalconNotes.Core.Events;
using FalconNotes.Core.Storage;

namespace FalconNotes.Core.Settings;

/// <summary>The local profile: the name the app uses for the user, and when it was created (docs/03, Schema).</summary>
/// <param name="DisplayName">The display name, 1–64 characters.</param>
/// <param name="CreatedAtUtc">When the profile was created ("On this device since").</param>
public sealed record Profile(string DisplayName, DateTime CreatedAtUtc);

/// <summary>Creates and changes the profile, chosen on the Welcome screen (docs/07, Welcome and Settings → Profile).</summary>
/// <param name="storage">The open database.</param>
/// <param name="feed">Change events.</param>
/// <param name="time">The clock.</param>
public sealed class ProfileService(StorageContext storage, ChangeFeed feed, TimeProvider time)
{
    /// <summary>The most characters in a display name.</summary>
    public const int MaxDisplayNameLength = 64;

    private const string Key = "profile";

    /// <summary>The profile, or null before the Welcome screen created it.</summary>
    /// <returns>The profile, or null.</returns>
    public Task<Profile?> GetAsync() => storage.Database.ReadAsync(connection => SettingsStore.Get<Profile>(connection, Key));

    /// <summary>Creates the profile on the Welcome screen: the name typed, or "Me" when none was.</summary>
    /// <param name="displayName">What the user typed, possibly nothing.</param>
    /// <returns>The profile.</returns>
    public Task<Profile> CreateAsync(string? displayName)
    {
        var name = Clean(displayName);
        return SaveAsync(new Profile(name.Length > 0 ? name : "Me", time.GetUtcNow().UtcDateTime));
    }

    /// <summary>Changes the display name. Empty is not allowed, since there is no username to fall back on.</summary>
    /// <param name="displayName">The new name.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="UserFacingException">The name is empty.</exception>
    /// <exception cref="InvalidOperationException">There is no profile yet.</exception>
    public async Task<Profile> SetDisplayNameAsync(string displayName)
    {
        var name = Clean(displayName);
        if (name.Length == 0)
        {
            throw new UserFacingException("Enter a name.", "displayName");
        }

        var profile = await GetAsync() ?? throw new InvalidOperationException("There is no profile yet.");
        return await SaveAsync(profile with { DisplayName = name });
    }

    private async Task<Profile> SaveAsync(Profile profile)
    {
        await storage.Database.InTransactionAsync((connection, transaction) =>
        {
            SettingsStore.Set(connection, Key, profile, transaction);
            return true;
        });
        feed.RaiseProfileChanged();
        return profile;
    }

    private static string Clean(string? name)
    {
        var clean = Text.Titles.OneLine(name ?? "");
        return clean.Length > MaxDisplayNameLength ? clean[..MaxDisplayNameLength].TrimEnd() : clean;
    }
}
