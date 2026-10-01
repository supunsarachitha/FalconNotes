using FalconNotes.Core.Attachments;
using FalconNotes.Core.Storage;

namespace FalconNotes.Core.Maintenance;

/// <summary>What one clean-up pass removed.</summary>
/// <param name="AbandonedUploads">Files added but never attached within 24 hours.</param>
/// <param name="OrphanFiles">Stored files no row refers to (after a crash, say).</param>
/// <param name="TemporaryFiles">Leftovers of interrupted writes.</param>
public sealed record AttachmentCleanupResult(int AbandonedUploads, int OrphanFiles, int TemporaryFiles);

/// <summary>
/// Removes attachment data that no longer belongs to anything, at start and hourly. Copied from the server's
/// <c>AttachmentCleanup</c> (docs/03, Attachment store).
/// </summary>
/// <param name="storage">The open database.</param>
/// <param name="store">The files.</param>
/// <param name="time">The clock.</param>
public sealed class AttachmentCleanup(StorageContext storage, AttachmentStore store, TimeProvider time)
{
    /// <summary>How long a file may stay unattached (while its note is being written).</summary>
    public static readonly TimeSpan AbandonedUploadAge = TimeSpan.FromHours(24);

    /// <summary>
    /// The minimum age of an unreferenced file before it is removed: a file is written before its row, so younger
    /// files may belong to an add that is still finishing.
    /// </summary>
    public static readonly TimeSpan OrphanFileAge = TimeSpan.FromHours(1);

    /// <summary>Runs one pass.</summary>
    /// <returns>What was removed.</returns>
    public async Task<AttachmentCleanupResult> RunAsync()
    {
        var now = time.GetUtcNow().UtcDateTime;
        var (abandoned, referenced) = await storage.Database.InTransactionAsync((connection, transaction) =>
        {
            var gone = new List<string>();
            using (var select = Sql.Command(connection, "SELECT StorageKey FROM Attachments WHERE NoteId IS NULL AND CreatedAt < $before", transaction)
                .With("$before", Sql.Time(now - AbandonedUploadAge)))
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    gone.Add(reader.GetString(0));
                }
            }

            using (var delete = Sql.Command(connection, "DELETE FROM Attachments WHERE NoteId IS NULL AND CreatedAt < $before", transaction)
                .With("$before", Sql.Time(now - AbandonedUploadAge)))
            {
                delete.ExecuteNonQuery();
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            using var all = Sql.Command(connection, "SELECT StorageKey FROM Attachments", transaction);
            using var rows = all.ExecuteReader();
            while (rows.Read())
            {
                keys.Add(rows.GetString(0));
            }

            return (gone, keys);
        });

        abandoned.ForEach(store.Delete);
        var orphans = 0;
        await Task.Run(() =>
        {
            foreach (var storageKey in store.EnumerateStorageKeys().ToList())
            {
                if (!referenced.Contains(storageKey) && store.GetLastWriteTimeUtc(storageKey) < now - OrphanFileAge)
                {
                    store.Delete(storageKey);
                    orphans++;
                }
            }
        });

        var temporary = await Task.Run(() => store.DeleteStaleTemporaryFiles(OrphanFileAge));
        return new AttachmentCleanupResult(abandoned.Count, orphans, temporary);
    }
}
