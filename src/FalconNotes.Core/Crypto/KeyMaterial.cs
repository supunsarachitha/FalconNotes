using System.Security.Cryptography;
using System.Text;

namespace FalconNotes.Core.Crypto;

/// <summary>
/// The keys derived from the device key with HKDF-SHA256 (RFC 5869), as docs/03 (Keys) sets out. Adapted from the
/// Maple Notes server's <c>KeyMaterial</c>, with this app's own salt and labels.
/// </summary>
/// <remarks>
/// <code>
/// device key ──HKDF──┬── database key    raw SQLite3MC key for falcon.db and its copies
///                    ├── attachment key  encrypts attachment files (AttachmentCipher)
///                    └── fingerprint     8 hex characters, shown in Settings, identifies the key
/// </code>
/// Every derived key has its own label, so learning one reveals nothing about the others or the device key.
/// </remarks>
public sealed class KeyMaterial : IDisposable
{
    private static readonly byte[] Salt = "FalconNotes.KeyDerivation"u8.ToArray();

    private readonly byte[] _databaseKey;
    private readonly byte[] _attachmentKey;

    /// <summary>Derives every key from <paramref name="deviceKey"/>, which is not kept.</summary>
    /// <param name="deviceKey">The 256-bit device key.</param>
    /// <exception cref="ArgumentException">The device key is not 32 bytes long.</exception>
    public KeyMaterial(ReadOnlySpan<byte> deviceKey)
    {
        if (deviceKey.Length != DeviceKeyStore.KeySizeBytes)
        {
            throw new ArgumentException($"The device key must be {DeviceKeyStore.KeySizeBytes} bytes.", nameof(deviceKey));
        }

        _databaseKey = Derive(deviceKey, "falcon-notes/v1/database", 32);
        _attachmentKey = Derive(deviceKey, "falcon-notes/v1/attachments", 32);
        Fingerprint = Convert.ToHexStringLower(Derive(deviceKey, "falcon-notes/v1/fingerprint", 4));
    }

    /// <summary>The raw 256-bit database key.</summary>
    public ReadOnlySpan<byte> DatabaseKey => _databaseKey;

    /// <summary>A new <see cref="AttachmentKey"/> holding a copy of the attachment key; dispose it after use.</summary>
    /// <returns>The key.</returns>
    public AttachmentKey CreateAttachmentKey() => new((byte[])_attachmentKey.Clone());

    /// <summary>A short, non-secret identifier of the device key (8 hex characters), shown in Settings.</summary>
    public string Fingerprint { get; }

    /// <summary>Wipes the derived keys from memory.</summary>
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_databaseKey);
        CryptographicOperations.ZeroMemory(_attachmentKey);
    }

    private static byte[] Derive(ReadOnlySpan<byte> deviceKey, string purpose, int length)
    {
        var output = new byte[length];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, deviceKey, output, Salt, Encoding.UTF8.GetBytes(purpose));
        return output;
    }
}
