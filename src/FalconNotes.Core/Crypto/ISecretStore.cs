namespace FalconNotes.Core.Crypto;

/// <summary>
/// The operating system's secure storage (Android Keystore, Windows DPAPI, the macOS Keychain), which keeps the device
/// key apart from the encrypted files (docs/03, Keys). Implemented per platform in the App.
/// </summary>
public interface ISecretStore
{
    /// <summary>Reads a secret.</summary>
    /// <param name="name">The secret's name.</param>
    /// <returns>The value, or null when there is none.</returns>
    Task<string?> GetAsync(string name);

    /// <summary>Stores a secret, replacing any earlier value.</summary>
    /// <param name="name">The secret's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>A task that completes when the secret is stored.</returns>
    Task SetAsync(string name, string value);

    /// <summary>Removes a secret, if there is one.</summary>
    /// <param name="name">The secret's name.</param>
    /// <returns>A task that completes when the secret is gone.</returns>
    Task RemoveAsync(string name);
}
