using System.Collections.Concurrent;
using System.Security.Cryptography;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Platform;

namespace FalconNotes.UI.Dev;

/// <summary>
/// Phase 0 spike S3 only (Debug builds): encrypts test files into <c>spike-media/</c> with a key that lives for the
/// process, and serves them to the media handler. Phase 1's attachment store replaces it.
/// </summary>
/// <param name="directories">Where to keep the encrypted copies.</param>
public sealed class SpikeMediaLibrary(IAppDirectories directories) : IMediaSource
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(AttachmentKey.SizeBytes);
    private readonly Guid _owner = Guid.NewGuid();
    private readonly ConcurrentDictionary<Guid, (string Path, string ContentType)> _files = new();

    /// <summary>Encrypts <paramref name="source"/> into the library.</summary>
    /// <param name="source">The plaintext.</param>
    /// <param name="contentType">Its type.</param>
    /// <returns>The new attachment's ID.</returns>
    public async Task<Guid> AddAsync(Stream source, string contentType)
    {
        var id = Guid.CreateVersion7();
        var folder = Path.Combine(directories.DataDirectory, "spike-media");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{id:N}.bin");
        using var key = new AttachmentKey((byte[])_key.Clone());
        await using (var output = File.Create(path))
        {
            await AttachmentCipher.EncryptAsync(source, output, key, _owner, id);
        }

        _files[id] = (path, contentType);
        return id;
    }

    /// <inheritdoc />
    public Task<MediaFile?> OpenAsync(Guid id)
    {
        if (!_files.TryGetValue(id, out var file))
        {
            return Task.FromResult<MediaFile?>(null);
        }

        var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.RandomAccess);
        var key = new AttachmentKey((byte[])_key.Clone());
        return Task.FromResult<MediaFile?>(new MediaFile(DecryptingAttachmentStream.Open(stream, key, _owner, id), file.ContentType));
    }
}
