using FalconNotes.Core.Crypto;

namespace FalconNotes.App.Services;

/// <summary>
/// <see cref="ISecretStore"/> on MAUI's <c>SecureStorage</c>: the Android Keystore, Windows DPAPI (packaged apps
/// only) and the macOS Keychain (needs the keychain entitlement). docs/02, Platform services.
/// </summary>
public sealed class SecretStore : ISecretStore
{
    /// <inheritdoc />
    public Task<string?> GetAsync(string name) => SecureStorage.Default.GetAsync(name);

    /// <inheritdoc />
    public Task SetAsync(string name, string value) => SecureStorage.Default.SetAsync(name, value);

    /// <inheritdoc />
    public Task RemoveAsync(string name)
    {
        SecureStorage.Default.Remove(name);
        return Task.CompletedTask;
    }
}
