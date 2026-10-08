using System.Globalization;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Events;
using FalconNotes.Core.Labels;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Backup.Restore;

/// <summary>How a restore is going, and in the end how it went.</summary>
/// <param name="Total">Notes to restore.</param>
/// <param name="Done">Notes handled so far.</param>
/// <param name="Restored">Notes restored.</param>
/// <param name="Skipped">Notes that were here already.</param>
/// <param name="Files">Files restored.</param>
/// <param name="Labels">Labels created for the restored notes (labels already here are reused, matched by name).</param>
/// <param name="Failed">Notes (or their files) and labels that could not be restored, with the reason.</param>
/// <param name="Stopped">Why the restore stopped before the end (the device is full), or null.</param>
public sealed record RestoreProgress(
    int Total, int Done, int Restored, int Skipped, int Files, int Labels, IReadOnlyList<(string Source, string Reason)> Failed,
    string? Stopped = null);

/// <summary>
/// Restores notes read by <see cref="RestoreReader"/>. Port of <c>web/import/importer.ts</c> with the server's
/// <c>NoteService.ImportAsync</c> rules (docs/05, Restoring): notes already here are skipped, the labels the others
/// carry are found or created by name, each note's files are stored, then the notes are written in transactions of up
/// to 200 with their original IDs, dates, state, kind, daily date and labels. A note that fails is reported and its
/// files removed; the rest carry on, unless the device is full.
/// </summary>
/// <param name="storage">The open database.</param>
/// <param name="attachments">Stores the files.</param>
/// <param name="labels">Creates the labels the notes bring.</param>
/// <param name="feed">Change events.</param>
/// <param name="time">The clock.</param>
public sealed class RestoreRunner(StorageContext storage, AttachmentService attachments, LabelService labels, ChangeFeed feed, TimeProvider time)
{
    /// <summary>Notes written per transaction.</summary>
    public const int BatchSize = 200;

    /// <summary>Why a cancelled restore stopped.</summary>
    public const string Cancelled = "The restore was cancelled.";

    /// <summary>How far in the future a creation time may lie (clock differences between devices).</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromHours(24);

    /// <summary>Restores the items, reporting progress after each note.</summary>
    /// <param name="items">The notes to restore.</param>
    /// <param name="progress">Told how far the restore has got.</param>
    /// <param name="labelColors">
    /// The colours the backups recorded for their labels (<see cref="RestorePlan.LabelColors"/>); a label not in it
    /// gets the next colour in turn.
    /// </param>
    /// <param name="cancellationToken">Stops between notes; notes restored so far stay.</param>
    /// <returns>How it went.</returns>
    public async Task<RestoreProgress> RunAsync(
        IReadOnlyList<RestoreItem> items, IProgress<RestoreProgress>? progress = null,
        IReadOnlyDictionary<string, LabelColor>? labelColors = null, CancellationToken cancellationToken = default)
    {
        int done = 0, restored = 0, skipped = 0, fileCount = 0, labelCount = 0;
        var failed = new List<(string, string)>();
        RestoreProgress Snapshot(string? stopped = null) =>
            new(items.Count, done, restored, skipped, fileCount, labelCount, failed.ToList(), stopped);
        progress?.Report(Snapshot());

        var existing = await ExistingAsync(items.Where(i => i.Id is not null).Select(i => i.Id!.Value).Distinct().ToList());
        var labelIds = await ResolveLabelsAsync(items.Where(i => i.Id is not { } id || !existing.Contains(id)), labelColors, failed);
        labelCount = labelIds.Created;
        if (failed.Count > 0)
        {
            progress?.Report(Snapshot());
        }

        foreach (var batch in items.Chunk(BatchSize))
        {
            var ready = new List<(RestoreItem Item, Guid Id, List<Guid> Files)>();
            foreach (var item in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await CommitAsync(ready);
                    return Finish(Snapshot(Cancelled));
                }

                if (item.Id is { } id && existing.Contains(id))
                {
                    skipped++;
                    done++;
                    progress?.Report(Snapshot());
                    continue;
                }

                var stored = new List<Guid>();
                try
                {
                    foreach (var attachment in item.Attachments)
                    {
                        await using var content = attachment.Open();
                        stored.Add((await attachments.AddAsync(content, attachment.Name, attachment.ContentType, cancellationToken: cancellationToken)).Id);
                    }

                    Validate(item, stored.Count);
                    var noteId = item.Id ?? Guid.CreateVersion7();
                    existing.Add(noteId);
                    ready.Add((item, noteId, stored));
                }
                catch (OperationCanceledException)
                {
                    await RemoveAsync(stored);
                    await CommitAsync(ready);
                    return Finish(Snapshot(Cancelled));
                }
                catch (NotEnoughSpaceException e)
                {
                    // The rest would not fit either: keep what is ready, then stop. Running the restore again
                    // once there is room skips the notes restored so far.
                    await RemoveAsync(stored);
                    await CommitAsync(ready);
                    return Finish(Snapshot(e.Message));
                }
                catch (Exception e) when (e is UserFacingException or IOException or InvalidDataException or System.Security.Cryptography.CryptographicException)
                {
                    await RemoveAsync(stored);
                    failed.Add((item.Source, e.Message));
                    done++;
                    progress?.Report(Snapshot());
                }
            }

            await CommitAsync(ready);
        }

        return Finish(Snapshot());

        async Task CommitAsync(List<(RestoreItem Item, Guid Id, List<Guid> Files)> ready)
        {
            var written = await WriteAsync(ready, labelIds.ByKey, failed);
            foreach (var (item, _, files) in ready)
            {
                done++;
                if (written.Contains(item))
                {
                    restored++;
                    fileCount += files.Count;
                    if (item.Missing.Count > 0)
                    {
                        failed.Add((item.Source, $"Restored without {string.Join(", ", item.Missing)}, which the archive does not contain."));
                    }
                }

                progress?.Report(Snapshot());
            }
        }

        RestoreProgress Finish(RestoreProgress result)
        {
            if (restored > 0)
            {
                feed.RaiseNotesChanged();
            }

            progress?.Report(result);
            return result;
        }
    }

    /// <summary>The message shown when a restore is done (docs/05, Restoring: step 5).</summary>
    /// <param name="result">How it went.</param>
    /// <returns>For example "Restored 35 notes and 8 files. 2 notes were already here."</returns>
    public static string Summary(RestoreProgress result)
    {
        var text = $"Restored {Count(result.Restored, "note")} and {Count(result.Files, "file")}.";
        if (result.Labels > 0)
        {
            text += $" Added {Count(result.Labels, "label")}.";
        }

        if (result.Skipped > 0)
        {
            text += $" {Count(result.Skipped, "note")} {(result.Skipped == 1 ? "was" : "were")} already here.";
        }

        return text;
    }

    private static string Count(int n, string noun) => string.Create(CultureInfo.InvariantCulture, $"{n:N0} {noun}{(n == 1 ? "" : "s")}");

    private void Validate(RestoreItem item, int fileCount)
    {
        if (item.Content.Length > NoteService.MaxContentLength)
        {
            throw new UserFacingException($"A note can be at most {NoteService.MaxContentLength:N0} characters long.");
        }

        if (string.IsNullOrWhiteSpace(item.Content) && fileCount == 0)
        {
            throw new UserFacingException("Write something or attach a file.");
        }

        if (item.CreatedAt > time.GetUtcNow() + FutureTolerance)
        {
            throw new UserFacingException("A note cannot have been created in the future.");
        }
    }

    /// <summary>
    /// The label IDs for every label name the notes carry, by <see cref="LabelRules.NameKey"/>: a label of the same
    /// name that is here already, or a new one in the colour the backup recorded. A label that cannot be created (a
    /// name over 40 characters, or past the limit of 100) is reported, and the notes are restored without it. Port of
    /// <c>resolveLabels</c> in <c>web/import/importer.ts</c>.
    /// </summary>
    private async Task<(Dictionary<string, Guid> ByKey, int Created)> ResolveLabelsAsync(
        IEnumerable<RestoreItem> items, IReadOnlyDictionary<string, LabelColor>? colors, List<(string, string)> failed)
    {
        // The names as first written, in the order the notes bring them, which is the order they are created in.
        var wanted = items.SelectMany(item => item.Labels).Select(name => name.Trim()).Where(name => name.Length > 0)
            .DistinctBy(LabelRules.NameKey).ToList();
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return (ids, 0);
        }

        var here = await storage.Database.ReadAsync(connection =>
        {
            using var command = Sql.Command(connection, "SELECT Id, Name FROM Labels ORDER BY CreatedAt, Id");
            var found = new List<(Guid Id, string Name)>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                found.Add((reader.GetId(0), reader.GetString(1)));
            }

            return found;
        });
        foreach (var (id, name) in here)
        {
            ids.TryAdd(LabelRules.NameKey(name), id);
        }

        var created = 0;
        foreach (var name in wanted.Where(name => !ids.ContainsKey(LabelRules.NameKey(name))))
        {
            var key = LabelRules.NameKey(name);
            try
            {
                ids[key] = (await labels.CreateAsync(name, colors is not null && colors.TryGetValue(key, out var color) ? color : null)).Id;
                created++;
            }
            catch (UserFacingException e)
            {
                failed.Add(($"Label “{name}”", $"{e.Message} Notes are restored without it."));
            }
        }

        return (ids, created);
    }

    /// <summary>Writes the ready notes in one transaction; returns the ones written. On failure, reports them all.</summary>
    private async Task<HashSet<RestoreItem>> WriteAsync(
        List<(RestoreItem Item, Guid Id, List<Guid> Files)> ready, Dictionary<string, Guid> labelIds, List<(string, string)> failed)
    {
        if (ready.Count == 0)
        {
            return [];
        }

        var now = time.GetUtcNow().UtcDateTime;
        try
        {
            await storage.Database.InTransactionAsync((connection, transaction) =>
            {
                foreach (var (item, id, files) in ready)
                {
                    var created = item.CreatedAt.UtcDateTime;
                    var updated = item.UpdatedAt.UtcDateTime;
                    updated = updated > now ? now : updated; // edit times never lie in the future,
                    updated = updated < created ? created : updated; // nor before the note was created
                    string? daily = null;
                    if (item.Kind == NoteKind.Note && item.DailyDate is { } day)
                    {
                        using var taken = Sql.Command(connection, "SELECT 1 FROM Notes WHERE DailyDate = $day", transaction).With("$day", day);
                        daily = taken.ExecuteScalar() is null ? day : null;
                    }

                    using var insert = Sql.Command(connection, """
                        INSERT INTO Notes (Id, Kind, DailyDate, IsPinned, ArchivedAt, CreatedAt, UpdatedAt, ContentBytes)
                        VALUES ($id, $kind, $daily, $pinned, $archived, $created, $updated, $bytes);
                        INSERT INTO NoteBodies (NoteId, Content) VALUES ($id, $content);
                        """, transaction);
                    insert.With("$id", Sql.Id(id)).With("$kind", (int)item.Kind).With("$daily", daily)
                        .With("$pinned", item.Pinned ? 1 : 0).With("$archived", item.Archived ? Sql.Time(updated) : null)
                        .With("$created", Sql.Time(created)).With("$updated", Sql.Time(updated))
                        .With("$bytes", NoteRepository.ContentBytes(item.Content)).With("$content", item.Content).ExecuteNonQuery();
                    NoteRepository.Attach(connection, transaction, id, files);
                    NoteRepository.SetTags(connection, transaction, id, TagParser.Extract(item.Content));

                    // A label deleted since it was resolved is simply not put on the note.
                    foreach (var labelId in item.Labels.Select(LabelRules.NameKey).Where(labelIds.ContainsKey).Select(key => labelIds[key])
                                 .Distinct().Take(LabelRules.MaxPerNote))
                    {
                        using var label = Sql.Command(connection,
                            "INSERT OR IGNORE INTO NoteLabels (NoteId, LabelId) SELECT $note, Id FROM Labels WHERE Id = $label", transaction);
                        label.With("$note", Sql.Id(id)).With("$label", Sql.Id(labelId)).ExecuteNonQuery();
                    }
                }

                return true;
            });
            return ready.Select(r => r.Item).ToHashSet();
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or UserFacingException)
        {
            foreach (var (item, _, files) in ready)
            {
                await RemoveAsync(files);
                failed.Add((item.Source, "That didn't work. Please try again."));
            }

            return [];
        }
    }

    private async Task RemoveAsync(IEnumerable<Guid> files)
    {
        foreach (var id in files)
        {
            await attachments.RemoveUnattachedAsync(id);
        }
    }

    private Task<HashSet<Guid>> ExistingAsync(IReadOnlyList<Guid> ids) =>
        storage.Database.ReadAsync(connection =>
        {
            var found = new HashSet<Guid>();
            foreach (var chunk in ids.Chunk(500))
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT Id FROM Notes WHERE Id IN ({command.InList("$id", chunk.Select(id => (object)Sql.Id(id)))})";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    found.Add(reader.GetId(0));
                }
            }

            return found;
        });
}
