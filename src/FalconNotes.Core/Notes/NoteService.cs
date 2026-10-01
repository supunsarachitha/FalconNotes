using FalconNotes.Core.Attachments;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Events;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Text;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Notes;

/// <summary>
/// Creates, lists, edits, pins, archives, labels, moves, trashes and deletes notes, by the rules of docs/04. Adapted
/// from the server's <c>NoteService</c>: one user, plain text in <c>NoteBodies</c> (the database is encrypted as a
/// whole), no quota. Every change raises <see cref="ChangeFeed.NotesChanged"/> after it commits.
/// </summary>
/// <param name="storage">The open database.</param>
/// <param name="files">The attachment files.</param>
/// <param name="feed">Change events.</param>
/// <param name="time">The clock.</param>
public sealed class NoteService(StorageContext storage, AttachmentStore files, ChangeFeed feed, TimeProvider time)
{
    /// <summary>The most characters in a note (UTF-16 length).</summary>
    public const int MaxContentLength = 100_000;

    /// <summary>Notes per page.</summary>
    public const int PageSize = 20;

    /// <summary>How long a note stays in the trash before it is deleted for good.</summary>
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(30);

    /// <summary>Notes a search reads at a time.</summary>
    private const int SearchBatchSize = 200;

    private Database Db => storage.Database;

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// One page of a list, newest first. With <see cref="NoteQuery.Search"/>, notes are read in batches of 200 until
    /// a page of matches is full; cancel when the query changes.
    /// </summary>
    /// <param name="query">Which notes.</param>
    /// <param name="cursor">Where the page starts; null for the first.</param>
    /// <param name="pageSize">Notes per page.</param>
    /// <param name="cancellationToken">Cancels the search between batches.</param>
    /// <returns>The page and the next cursor.</returns>
    public Task<NotePage> ListAsync(NoteQuery query, NoteCursor? cursor = null, int pageSize = PageSize, CancellationToken cancellationToken = default)
    {
        query = Normalise(query);
        return Db.ReadAsync(connection =>
        {
            if (query.Search is not { } search)
            {
                var rows = NoteRepository.ListRows(connection, query, cursor, pageSize + 1);
                var page = NoteRepository.Load(connection, rows.Take(pageSize).ToList());
                return new NotePage(page, rows.Count > pageSize ? CursorAfter(rows[pageSize - 1], query.State) : null);
            }

            var matches = new List<NoteRow>();
            var bodies = new Dictionary<Guid, string>();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = NoteRepository.ListRows(connection, query, cursor, SearchBatchSize);
                var ids = batch.Select(r => r.Id).ToList();
                var text = NoteRepository.Bodies(connection, ids);
                var names = NoteRepository.AttachmentsOf(connection, ids);
                foreach (var row in batch)
                {
                    cursor = CursorAfter(row, query.State);
                    var content = text.GetValueOrDefault(row.Id, "");
                    if (content.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || names.GetValueOrDefault(row.Id, []).Any(f => f.Attachment.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)))
                    {
                        matches.Add(row);
                        bodies[row.Id] = content;
                        if (matches.Count == pageSize)
                        {
                            return new NotePage(NoteRepository.Load(connection, matches, bodies), cursor);
                        }
                    }
                }

                if (batch.Count < SearchBatchSize)
                {
                    return new NotePage(NoteRepository.Load(connection, matches, bodies), null);
                }
            }
        }, cancellationToken);
    }

    /// <summary>One note.</summary>
    /// <param name="id">The note.</param>
    /// <returns>The note, or null when there is none.</returns>
    public Task<Note?> GetAsync(Guid id) =>
        Db.ReadAsync(connection => NoteRepository.GetRow(connection, id) is { } row ? NoteRepository.Load(connection, [row])[0] : null);

    /// <summary>A day's daily note.</summary>
    /// <param name="date">The day.</param>
    /// <returns>The note, or null when the day has none.</returns>
    public Task<Note?> GetDailyAsync(DateOnly date) =>
        Db.ReadAsync(connection => NoteRepository.GetDailyRow(connection, date) is { } row ? NoteRepository.Load(connection, [row])[0] : null);

    /// <summary>Every habit not in the trash, archived ones included, oldest first (docs/04, Lists: Habits).</summary>
    /// <returns>The habits.</returns>
    public Task<IReadOnlyList<Note>> ListHabitsAsync() =>
        Db.ReadAsync<IReadOnlyList<Note>>(connection => NoteRepository.Load(connection, NoteRepository.HabitRows(connection)));

    /// <summary>Creates a note and attaches the files added while it was written.</summary>
    /// <param name="content">The Markdown text.</param>
    /// <param name="kind">What the note is for.</param>
    /// <param name="attachmentIds">Files added while writing, not yet attached.</param>
    /// <param name="dailyDate">The day it is the daily note of, if any.</param>
    /// <param name="isPinned">Whether it starts pinned.</param>
    /// <returns>The new note.</returns>
    /// <exception cref="UserFacingException">The text is too long or empty without files, or a daily note is not a
    /// timeline note.</exception>
    /// <exception cref="DailyNoteExistsException">The day has a daily note already.</exception>
    public async Task<Note> CreateAsync(
        string content, NoteKind kind = NoteKind.Note, IReadOnlyCollection<Guid>? attachmentIds = null, DateOnly? dailyDate = null, bool isPinned = false)
    {
        attachmentIds ??= [];
        ValidateContent(content, attachmentIds.Count);
        if (dailyDate is not null && kind != NoteKind.Note)
        {
            throw new UserFacingException("Only timeline notes can be daily notes.", "dailyDate");
        }

        var id = Guid.CreateVersion7();
        var now = Now;
        try
        {
            await Db.InTransactionAsync((connection, transaction) =>
            {
                using (var insert = Sql.Command(connection, """
                    INSERT INTO Notes (Id, Kind, DailyDate, IsPinned, CreatedAt, UpdatedAt, ContentBytes)
                    VALUES ($id, $kind, $daily, $pinned, $now, $now, $bytes);
                    INSERT INTO NoteBodies (NoteId, Content) VALUES ($id, $content);
                    """, transaction))
                {
                    insert.With("$id", Sql.Id(id)).With("$kind", (int)kind).With("$daily", Sql.Date(dailyDate))
                        .With("$pinned", isPinned ? 1 : 0).With("$now", Sql.Time(now))
                        .With("$bytes", NoteRepository.ContentBytes(content)).With("$content", content)
                        .ExecuteNonQuery();
                }

                NoteRepository.Attach(connection, transaction, id, attachmentIds);
                NoteRepository.SetTags(connection, transaction, id, TagParser.Extract(content));
                return true;
            });
        }
        catch (SqliteException e) when (dailyDate is { } day && e.SqliteErrorCode == 19 /* constraint: IX_Notes_Daily */)
        {
            throw new DailyNoteExistsException(day);
        }

        feed.RaiseNotesChanged();
        return (await GetAsync(id))!;
    }

    /// <summary>
    /// Replaces a note's text and, when <paramref name="attachmentIds"/> is given, its files: files left out are
    /// deleted, new ones attached. <c>UpdatedAt</c> changes; tags are read again from the text.
    /// </summary>
    /// <param name="id">The note.</param>
    /// <param name="content">The new text.</param>
    /// <param name="attachmentIds">The files it should have; null to keep them as they are.</param>
    /// <returns>The note, or null when there is none.</returns>
    /// <exception cref="UserFacingException">The text is too long or empty without files.</exception>
    public async Task<Note?> UpdateAsync(Guid id, string content, IReadOnlyCollection<Guid>? attachmentIds = null)
    {
        var now = Now;
        var removed = await Db.InTransactionAsync<List<string>?>((connection, transaction) =>
        {
            if (NoteRepository.GetRow(connection, id, transaction) is null)
            {
                return null;
            }

            var current = NoteRepository.AttachmentsOf(connection, [id], transaction).GetValueOrDefault(id, []);
            var wanted = attachmentIds?.Distinct().ToList() ?? current.Select(f => f.Attachment.Id).ToList();
            ValidateContent(content, wanted.Count);

            using (var update = Sql.Command(connection, """
                UPDATE NoteBodies SET Content = $content WHERE NoteId = $id;
                UPDATE Notes SET UpdatedAt = $now, ContentBytes = $bytes, Revision = Revision + 1 WHERE Id = $id;
                """, transaction))
            {
                update.With("$id", Sql.Id(id)).With("$content", content).With("$now", Sql.Time(now))
                    .With("$bytes", NoteRepository.ContentBytes(content)).ExecuteNonQuery();
            }

            var gone = new List<string>();
            if (attachmentIds is not null)
            {
                foreach (var file in current.Where(f => !wanted.Contains(f.Attachment.Id)))
                {
                    using var delete = Sql.Command(connection, "DELETE FROM Attachments WHERE Id = $id", transaction);
                    delete.With("$id", Sql.Id(file.Attachment.Id)).ExecuteNonQuery();
                    gone.Add(file.StorageKey);
                }

                NoteRepository.Attach(connection, transaction, id, wanted.Where(w => current.All(f => f.Attachment.Id != w)).ToList());
            }

            NoteRepository.SetTags(connection, transaction, id, TagParser.Extract(content));
            NoteRepository.RemoveUnusedTags(connection, transaction);
            return gone;
        });

        if (removed is null)
        {
            return null;
        }

        removed.ForEach(files.Delete);
        feed.RaiseNotesChanged();
        return await GetAsync(id);
    }

    /// <summary>
    /// Pins, archives, trashes, moves or labels a note (each change only when given). <c>UpdatedAt</c> stays.
    /// </summary>
    /// <param name="id">The note.</param>
    /// <param name="change">The changes.</param>
    /// <returns>The note, or null when there is none.</returns>
    /// <exception cref="UserFacingException">A move to or from habits, too many labels, or a label that does not
    /// exist.</exception>
    public async Task<Note?> PatchAsync(Guid id, NotePatch change)
    {
        var now = Now;
        var found = await Db.InTransactionAsync((connection, transaction) =>
        {
            if (NoteRepository.GetRow(connection, id, transaction) is not { } note)
            {
                return false;
            }

            // A habit's text has a fixed shape (its days, one per line), so it never moves to or from the other kinds.
            if (change.Kind is { } moveTo && moveTo != note.Kind && (moveTo == NoteKind.Habit || note.Kind == NoteKind.Habit))
            {
                throw new UserFacingException("Habits cannot become other kinds of notes, and notes cannot become habits.", "kind");
            }

            var kind = change.Kind ?? note.Kind;
            var archivedAt = change.IsArchived is { } archived ? (archived ? note.ArchivedAtUtc ?? now : null) : note.ArchivedAtUtc;
            var trashedAt = change.IsTrashed is { } trashed ? (trashed ? note.TrashedAtUtc ?? now : null) : note.TrashedAtUtc;

            // A daily note moved out of the timeline, or to the trash, gives up its day; restored, it is an ordinary note.
            var dailyDate = kind != NoteKind.Note || trashedAt is not null ? null : note.DailyDate;

            using (var update = Sql.Command(connection, """
                UPDATE Notes SET Kind = $kind, IsPinned = $pinned, ArchivedAt = $archived, TrashedAt = $trashed,
                                 DailyDate = $daily, Revision = Revision + 1
                WHERE Id = $id
                """, transaction))
            {
                update.With("$id", Sql.Id(id)).With("$kind", (int)kind).With("$pinned", (change.IsPinned ?? note.IsPinned) ? 1 : 0)
                    .With("$archived", archivedAt is { } a ? Sql.Time(a) : null)
                    .With("$trashed", trashedAt is { } t ? Sql.Time(t) : null)
                    .With("$daily", Sql.Date(dailyDate)).ExecuteNonQuery();
            }

            if (change.LabelIds is { } labelIds)
            {
                SetLabels(connection, transaction, id, labelIds);
            }

            return true;
        });

        if (!found)
        {
            return null;
        }

        feed.RaiseNotesChanged();
        return await GetAsync(id);
    }

    /// <summary>Deletes a note for good, with its files; tags no note uses any more go too.</summary>
    /// <param name="id">The note.</param>
    /// <returns>False when there is no such note.</returns>
    public async Task<bool> DeleteAsync(Guid id)
    {
        var deleted = await DeleteWhereAsync("Id = $id", ("$id", Sql.Id(id)));
        return deleted.Notes > 0;
    }

    /// <summary>Deletes every note in the trash for good, with their files.</summary>
    /// <returns>How many notes and files were deleted.</returns>
    public Task<DeletedCount> EmptyTrashAsync() => DeleteWhereAsync("TrashedAt IS NOT NULL");

    /// <summary>Deletes the notes that have been in the trash for more than 30 days, at start and hourly.</summary>
    /// <returns>How many notes and files were deleted.</returns>
    public Task<DeletedCount> PurgeExpiredTrashAsync() =>
        DeleteWhereAsync("TrashedAt IS NOT NULL AND TrashedAt < $cutoff", ("$cutoff", Sql.Time(Now - TrashRetention)));

    /// <summary>
    /// Deletes every note, tag, label and file in one transaction, then the files (Backup &amp; data → Delete all notes
    /// and files). The profile, preferences and app lock stay.
    /// </summary>
    /// <returns>How many notes and files were deleted.</returns>
    public async Task<DeletedCount> DeleteAllAsync()
    {
        var (notes, keys) = await Db.InTransactionAsync((connection, transaction) =>
        {
            var storageKeys = new List<string>();
            using (var select = Sql.Command(connection, "SELECT StorageKey FROM Attachments", transaction))
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    storageKeys.Add(reader.GetString(0));
                }
            }

            using var delete = Sql.Command(connection, """
                DELETE FROM Attachments; DELETE FROM NoteLabels; DELETE FROM NoteTags; DELETE FROM NoteBodies;
                DELETE FROM Labels; DELETE FROM Tags;
                """, transaction);
            delete.ExecuteNonQuery();
            using var count = Sql.Command(connection, "DELETE FROM Notes", transaction);
            return (count.ExecuteNonQuery(), storageKeys);
        });

        keys.ForEach(files.Delete);
        feed.RaiseLabelsChanged();
        feed.RaiseNotesChanged();
        return new DeletedCount(notes, keys.Count);
    }

    /// <summary>
    /// The tags of active notes of these kinds, with how many notes use each, sorted by name. Tags with no such note
    /// are left out.
    /// </summary>
    /// <param name="kinds">The enabled kinds.</param>
    /// <returns>The tags.</returns>
    public Task<IReadOnlyList<TagCount>> TagCountsAsync(IReadOnlyList<NoteKind> kinds) =>
        Db.ReadAsync<IReadOnlyList<TagCount>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT t.Name, COUNT(*) FROM NoteTags nt
                JOIN Tags t ON t.Id = nt.TagId
                JOIN Notes n ON n.Id = nt.NoteId
                WHERE n.ArchivedAt IS NULL AND n.TrashedAt IS NULL AND n.Kind IN ({command.InList("$kind", kinds.Select(k => (object)(int)k))})
                GROUP BY nt.TagId
                """;
            var tags = new List<TagCount>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tags.Add(new TagCount(reader.GetString(0), reader.GetInt32(1)));
            }

            return tags.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        });

    /// <summary>
    /// How many active notes of these kinds were created on each local day of a range, for the calendar's dots.
    /// </summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day, at most 62 days later.</param>
    /// <param name="kinds">The enabled kinds.</param>
    /// <param name="zone">The device's time zone.</param>
    /// <returns>The days that have notes, in order.</returns>
    /// <exception cref="ArgumentException">The range is empty or longer than 62 days.</exception>
    public Task<IReadOnlyList<(DateOnly Day, int Count)>> CalendarAsync(DateOnly from, DateOnly to, IReadOnlyList<NoteKind> kinds, TimeZoneInfo zone)
    {
        if (to < from || to.DayNumber - from.DayNumber > 62)
        {
            throw new ArgumentException("Ask for at most 62 days, ending on or after the first.", nameof(to));
        }

        var (start, _) = DateFormats.DayRange(from, zone);
        var (_, end) = DateFormats.DayRange(to, zone);
        return Db.ReadAsync<IReadOnlyList<(DateOnly, int)>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT CreatedAt FROM Notes
                WHERE ArchivedAt IS NULL AND TrashedAt IS NULL AND CreatedAt >= $start AND CreatedAt < $end
                  AND Kind IN ({command.InList("$kind", kinds.Select(k => (object)(int)k))})
                """;
            command.With("$start", Sql.Time(start)).With("$end", Sql.Time(end));
            var days = new List<DateOnly>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                days.Add(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(reader.GetTime(0), zone)));
            }

            return days.GroupBy(d => d).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())).ToList();
        });
    }

    /// <summary>
    /// How much the notes and files take: the text of every note (the archive and trash included) and every file.
    /// </summary>
    /// <returns>The usage.</returns>
    public Task<StorageUsage> StorageUsageAsync() =>
        Db.ReadAsync(connection =>
        {
            using var command = Sql.Command(connection, """
                SELECT (SELECT COALESCE(SUM(ContentBytes), 0) FROM Notes), (SELECT COUNT(*) FROM Notes),
                       (SELECT COALESCE(SUM(SizeBytes), 0) FROM Attachments), (SELECT COUNT(*) FROM Attachments)
                """);
            using var reader = command.ExecuteReader();
            reader.Read();
            return new StorageUsage(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetInt32(3));
        });

    /// <summary>The trash's "days left" for a note: 0 means it is deleted for good today (docs/04).</summary>
    /// <param name="trashedAtUtc">When it went to the trash.</param>
    /// <param name="nowUtc">Now.</param>
    /// <returns>Whole days left, at least 0.</returns>
    public static int DaysLeftInTrash(DateTime trashedAtUtc, DateTime nowUtc) =>
        Math.Max(0, (int)Math.Ceiling((trashedAtUtc + TrashRetention - nowUtc) / TimeSpan.FromDays(1)));

    private async Task<DeletedCount> DeleteWhereAsync(string where, params (string Name, object Value)[] parameters)
    {
        var (notes, keys) = await Db.InTransactionAsync((connection, transaction) =>
            NoteRepository.Delete(connection, transaction, NoteRepository.Ids(connection, transaction, where, parameters)));
        if (notes == 0)
        {
            return new DeletedCount(0, 0);
        }

        keys.ForEach(files.Delete);
        feed.RaiseNotesChanged();
        return new DeletedCount(notes, keys.Count);
    }

    private static void SetLabels(SqliteConnection connection, SqliteTransaction transaction, Guid noteId, IReadOnlyList<Guid> labelIds)
    {
        var ids = labelIds.Distinct().ToList();
        if (ids.Count > Labels.LabelRules.MaxPerNote)
        {
            throw new UserFacingException($"A note can have at most {Labels.LabelRules.MaxPerNote} labels.", "labelIds");
        }

        using var command = Sql.Command(connection, "", transaction);
        command.CommandText = $"""
            DELETE FROM NoteLabels WHERE NoteId = $note;
            INSERT INTO NoteLabels (NoteId, LabelId) SELECT $note, Id FROM Labels WHERE Id IN ({command.InList("$label", ids.Select(id => (object)Sql.Id(id)))});
            """;
        command.With("$note", Sql.Id(noteId)).ExecuteNonQuery();
        using var check = Sql.Command(connection, "SELECT COUNT(*) FROM NoteLabels WHERE NoteId = $note", transaction).With("$note", Sql.Id(noteId));
        if (Convert.ToInt32(check.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != ids.Count)
        {
            throw new UserFacingException("One or more labels do not exist.", "labelIds");
        }
    }

    private static void ValidateContent(string content, int attachmentCount)
    {
        if (content.Length > MaxContentLength)
        {
            throw new UserFacingException($"A note can be at most {MaxContentLength:N0} characters long.", "content");
        }

        if (string.IsNullOrWhiteSpace(content) && attachmentCount == 0)
        {
            throw new UserFacingException("Write something or attach a file.", "content");
        }
    }

    private static NoteQuery Normalise(NoteQuery query)
    {
        var tag = string.IsNullOrWhiteSpace(query.Tag) ? null : query.Tag.Trim().TrimStart('#').ToLowerInvariant();
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var kinds = query.Kinds.Count == 0 ? [NoteKind.Note] : query.Kinds;
        return query with { Tag = string.IsNullOrEmpty(tag) ? null : tag, Search = search, Kinds = kinds };
    }

    /// <summary>
    /// The position after a note: its creation time, or in the trash, which lists the most recently deleted first,
    /// the time it was deleted.
    /// </summary>
    private static NoteCursor CursorAfter(NoteRow row, NoteState state) =>
        new(state == NoteState.Trash ? row.TrashedAtUtc ?? row.CreatedAtUtc : row.CreatedAtUtc, row.Id);
}

/// <summary>Changes to a note's state; null leaves that part as it is.</summary>
/// <param name="IsPinned">Pin or unpin.</param>
/// <param name="IsArchived">Archive or restore from the archive.</param>
/// <param name="IsTrashed">Move to or restore from the trash.</param>
/// <param name="Kind">Move to another kind (never to or from habits).</param>
/// <param name="LabelIds">The labels it should have.</param>
public sealed record NotePatch(
    bool? IsPinned = null, bool? IsArchived = null, bool? IsTrashed = null, NoteKind? Kind = null, IReadOnlyList<Guid>? LabelIds = null);

/// <summary>How much the notes and files take (Settings → Profile → Storage).</summary>
/// <param name="NotesBytes">The text of every note, in UTF-8 bytes.</param>
/// <param name="NoteCount">Every note, of every kind and state.</param>
/// <param name="FilesBytes">Every file's size.</param>
/// <param name="FileCount">Every file, including ones not attached yet.</param>
public sealed record StorageUsage(long NotesBytes, int NoteCount, long FilesBytes, int FileCount)
{
    /// <summary>Notes and files together.</summary>
    public long TotalBytes => NotesBytes + FilesBytes;
}
