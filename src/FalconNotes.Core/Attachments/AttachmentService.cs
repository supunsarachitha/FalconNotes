using FalconNotes.Core.Crypto;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Storage;

namespace FalconNotes.Core.Attachments;

/// <summary>A file is larger than the 2 GiB a file can be.</summary>
public sealed class FileTooLargeException() : UserFacingException("This file is too large: a file can be at most 2 GB.");

/// <summary>The device ran out of space (docs/04, Attachments).</summary>
public sealed class NotEnoughSpaceException() : UserFacingException("Not enough space on this device.");

/// <summary>
/// Adds files to the encrypted store, removes files that were added but not kept, and opens files for the page and for
/// Open and Save a copy. Adapted from the server's <c>AttachmentService</c> (docs/02, Serving attachments; docs/04,
/// Attachments): every file is encrypted with the attachment key and bound to this installation and its own ID.
/// </summary>
/// <param name="storage">The open database and keys.</param>
/// <param name="store">The files.</param>
/// <param name="time">The clock.</param>
public sealed class AttachmentService(StorageContext storage, AttachmentStore store, TimeProvider time) : IMediaSource
{
    /// <summary>The largest file: 2 GiB.</summary>
    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Encrypts a file into the store and records it, not yet attached to a note: the composer attaches it on Post, and
    /// clean-up removes it after 24 hours if it never is.
    /// </summary>
    /// <param name="content">The file's content, read to the end.</param>
    /// <param name="fileName">The name it came with.</param>
    /// <param name="contentType">The type it came with, if any.</param>
    /// <param name="progress">Told how many bytes have been stored.</param>
    /// <param name="cancellationToken">Cancels; nothing is left behind.</param>
    /// <returns>The new attachment.</returns>
    /// <exception cref="FileTooLargeException">The file is larger than 2 GiB.</exception>
    /// <exception cref="NotEnoughSpaceException">The device is full.</exception>
    public async Task<Attachment> AddAsync(
        Stream content, string? fileName, string? contentType, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.CreateVersion7();
        var storageKey = AttachmentStore.CreateStorageKey(id);
        var metered = new MeteredStream(content, MaxFileBytes, progress);
        try
        {
            using var key = storage.Keys.CreateAttachmentKey();
            await Task.Run(() => store.WriteAsync(
                storageKey,
                (file, ct) => AttachmentCipher.EncryptAsync(metered, file, key, storage.InstallationId, id, ct),
                cancellationToken: cancellationToken), cancellationToken);
        }
        catch (IOException e) when (IsDiskFull(e))
        {
            throw new NotEnoughSpaceException();
        }

        var name = UploadPolicy.SanitizeFileName(fileName);
        var attachment = new Attachment(id, null, name, UploadPolicy.ResolveContentType(contentType, name), metered.BytesRead,
            time.GetUtcNow().UtcDateTime, 0);
        try
        {
            await storage.Database.InTransactionAsync((connection, transaction) =>
            {
                using var insert = Sql.Command(connection, """
                    INSERT INTO Attachments (Id, NoteId, FileName, ContentType, SizeBytes, StorageKey, CreatedAt)
                    VALUES ($id, NULL, $name, $type, $size, $key, $created)
                    """, transaction);
                return insert.With("$id", Sql.Id(id)).With("$name", attachment.FileName).With("$type", attachment.ContentType)
                    .With("$size", attachment.SizeBytes).With("$key", storageKey).With("$created", Sql.Time(attachment.CreatedAtUtc))
                    .ExecuteNonQuery();
            }, CancellationToken.None);
        }
        catch
        {
            store.Delete(storageKey);
            throw;
        }

        return attachment;
    }

    /// <summary>
    /// Removes a file that was added but is not attached to a note: removed from the composer before posting, or added
    /// during an edit that was cancelled.
    /// </summary>
    /// <param name="id">The attachment.</param>
    /// <returns>False when there is no such unattached file.</returns>
    public async Task<bool> RemoveUnattachedAsync(Guid id)
    {
        var storageKey = await storage.Database.InTransactionAsync((connection, transaction) =>
        {
            using var select = Sql.Command(connection, "SELECT StorageKey FROM Attachments WHERE Id = $id AND NoteId IS NULL", transaction)
                .With("$id", Sql.Id(id));
            if (select.ExecuteScalar() is not string key)
            {
                return null;
            }

            using var delete = Sql.Command(connection, "DELETE FROM Attachments WHERE Id = $id", transaction).With("$id", Sql.Id(id));
            delete.ExecuteNonQuery();
            return key;
        });

        if (storageKey is null)
        {
            return false;
        }

        store.Delete(storageKey);
        return true;
    }

    /// <summary>An attachment's record.</summary>
    /// <param name="id">The attachment.</param>
    /// <returns>The record, or null.</returns>
    public async Task<Attachment?> GetAsync(Guid id) => (await FindAsync(id))?.Attachment;

    /// <inheritdoc />
    public async Task<MediaFile?> OpenAsync(Guid id)
    {
        if (await FindAsync(id) is not { } row)
        {
            return null;
        }

        return new MediaFile(await Task.Run(() => OpenDecrypted(row)), row.Attachment.ContentType);
    }

    /// <summary>
    /// Writes a decrypted copy of a file into a new folder under <paramref name="folder"/>, for Open (the folder is
    /// <c>cache/open/</c>, deleted at the next start) or sharing.
    /// </summary>
    /// <param name="id">The attachment.</param>
    /// <param name="folder">Where to make the copy's folder.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>The copy's path, or null when there is no such attachment.</returns>
    public async Task<string?> CopyDecryptedAsync(Guid id, string folder, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(id) is not { } row)
        {
            return null;
        }

        var directory = Path.Combine(folder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, row.Attachment.FileName);
        await Task.Run(async () =>
        {
            await using var source = OpenDecrypted(row);
            await using var target = File.Create(path);
            await source.CopyToAsync(target, 1024 * 1024, cancellationToken);
        }, cancellationToken);
        return path;
    }

    /// <summary>Whether an exception means the disk is full (ENOSPC on Unix, ERROR_DISK_FULL or HANDLE_DISK_FULL on Windows).</summary>
    /// <param name="e">The exception.</param>
    /// <returns>Whether the device ran out of space.</returns>
    public static bool IsDiskFull(IOException e) => (e.HResult & 0xFFFF) is 28 or 112 or 39;

    private DecryptingAttachmentStream OpenDecrypted(AttachmentRow row)
    {
        using var key = storage.Keys.CreateAttachmentKey();
        return DecryptingAttachmentStream.Open(store.OpenRead(row.StorageKey), key, storage.InstallationId, row.Attachment.Id);
    }

    private Task<AttachmentRow?> FindAsync(Guid id) =>
        storage.Database.ReadAsync(connection =>
        {
            using var command = Sql.Command(connection,
                "SELECT Id, NoteId, FileName, ContentType, SizeBytes, CreatedAt, Revision, StorageKey FROM Attachments WHERE Id = $id")
                .With("$id", Sql.Id(id));
            return NoteRepository.ReadAttachments(command).SingleOrDefault();
        });
}
