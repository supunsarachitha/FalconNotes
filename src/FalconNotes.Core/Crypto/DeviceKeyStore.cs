using System.Security.Cryptography;

namespace FalconNotes.Core.Crypto;

/// <summary>
/// Reads, creates and forgets the device key: 32 random bytes, created on first run and kept only in
/// <see cref="ISecretStore"/>. Every other key is derived from it (docs/03, Keys).
/// </summary>
/// <param name="secrets">The platform's secure storage.</param>
public sealed class DeviceKeyStore(ISecretStore secrets)
{
    /// <summary>The device key's name in the secure storage.</summary>
    public const string SecretName = "falcon-notes.device-key.v1";

    /// <summary>The device key's length in bytes.</summary>
    public const int KeySizeBytes = 32;

    /// <summary>Reads the device key.</summary>
    /// <returns>The key, or null when there is none or the stored value is not a 256-bit key.</returns>
    public async Task<byte[]?> GetAsync()
    {
        var stored = await secrets.GetAsync(SecretName);
        if (stored is null)
        {
            return null;
        }

        var key = new byte[KeySizeBytes];
        return Convert.TryFromBase64String(stored, key, out var written) && written == KeySizeBytes ? key : null;
    }

    /// <summary>Creates a new random device key and stores it, replacing any earlier one.</summary>
    /// <returns>The new key.</returns>
    public async Task<byte[]> CreateAsync()
    {
        var key = RandomNumberGenerator.GetBytes(KeySizeBytes);
        await secrets.SetAsync(SecretName, Convert.ToBase64String(key));
        return key;
    }

    /// <summary>Removes the device key. The notes encrypted with it can no longer be read.</summary>
    /// <returns>A task that completes when the key is gone.</returns>
    public Task ForgetAsync() => secrets.RemoveAsync(SecretName);
}
