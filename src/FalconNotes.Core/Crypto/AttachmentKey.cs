using System.Security.Cryptography;

namespace FalconNotes.Core.Crypto;

/// <summary>
/// The 256-bit key that attachment files are encrypted with (the server's "user data key"; docs/03, Keys). Dispose it
/// when the operation that needs it is done, which wipes the key bytes from memory.
/// </summary>
public sealed class AttachmentKey : IDisposable
{
    /// <summary>The key's length in bytes.</summary>
    public const int SizeBytes = 32;

    private readonly byte[] _key;

    /// <summary>Wraps key bytes; this instance takes ownership of the array.</summary>
    /// <param name="key">The 32 key bytes.</param>
    /// <param name="version">Generation of the key, recorded in every file it encrypts.</param>
    /// <exception cref="ArgumentException">The key is not 32 bytes long.</exception>
    public AttachmentKey(byte[] key, byte version = 1)
    {
        if (key.Length != SizeBytes)
        {
            throw new ArgumentException($"An attachment key must be {SizeBytes} bytes.", nameof(key));
        }

        _key = key;
        Version = version;
    }

    /// <summary>The key bytes.</summary>
    public ReadOnlySpan<byte> Span => _key;

    /// <summary>Generation of the key.</summary>
    public byte Version { get; }

    /// <summary>Wipes the key from memory.</summary>
    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
