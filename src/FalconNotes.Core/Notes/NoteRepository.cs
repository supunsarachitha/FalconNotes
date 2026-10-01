using System.Text;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Notes;

/// <summary>A note's row without its text, tags, files or labels.</summary>
internal sealed record NoteRow(
    Guid Id, NoteKind Kind, DateOnly? DailyDate, bool IsPinned, DateTime? ArchivedAtUtc, DateTime? TrashedAtUtc,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, long Revision);

/// <summary>An attachment's row, with where its file is.</summary>
internal sealed record AttachmentRow(Attachment Attachment, string StorageKey);

/// <summary>
/// The SQL behind notes. Synchronous: callers run it inside <see cref="Database.ReadAsync{T}"/> or
/// <see cref="Database.InTransactionAsync{T}"/>, which move it off the UI thread. Lists read only the <c>Notes</c>
/// table and its indexes; <c>NoteBodies</c> is read for the notes returned (docs/13).
/// </summary>
internal static class NoteRepository
{
    private const string Columns =
        "n.Id, n.Kind, n.DailyDate, n.IsPinned, n.ArchivedAt, n.TrashedAt, n.CreatedAt, n.UpdatedAt, n.Revision";

    /// <summary>Reads a page of rows of a list, newest first (the trash: most recently deleted first).</summary>
    public static List<NoteRow> ListRows(SqliteConnection connection, NoteQuery query, NoteCursor? cursor, int count)
    {
        using var command = connection.CreateCommand();
        var where = Where(command, query, cursor, kindsByIndex: true, out var orderColumn);
        command.CommandText = $"""
            SELECT {Columns} FROM Notes n
            WHERE {where}
            ORDER BY {orderColumn} DESC, n.Id DESC
            LIMIT $count
            """;
        command.With("$count", count);
        return ReadRows(command);
    }

    /// <summary>
    /// Reads a list's rows with their text in order, one at a time, until <paramref name="accept"/> has taken
    /// <paramref name="count"/> of them or the list ends. Used by search: one pass in index order (no sorting), reading
    /// each body once, so a search that matches nothing reads every note just once.
    /// </summary>
    /// <returns>The accepted rows with their text, and whether the list ended before the page filled.</returns>
    public static (List<(NoteRow Row, string Content)> Rows, bool Ended) Scan(
        SqliteConnection connection, NoteQuery query, NoteCursor? cursor, int count,
        Func<NoteRow, string, bool> accept, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        // Kinds are not matched through an index here, so SQLite walks the time index in order instead of sorting.
        var where = Where(command, query, cursor, kindsByIndex: false, out var orderColumn);
        command.CommandText = $"""
            SELECT {Columns}, b.Content FROM Notes n
            JOIN NoteBodies b ON b.NoteId = n.Id
            WHERE {where}
            ORDER BY {orderColumn} DESC, n.Id DESC
            """;
        var accepted = new List<(NoteRow, string)>();
        using var reader = command.ExecuteReader();
        var read = 0;
        while (reader.Read())
        {
            if (++read % 256 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = ReadRow(reader);
            var content = reader.GetString(9);
            if (accept(row, content))
            {
                accepted.Add((row, content));
                if (accepted.Count == count)
                {
                    return (accepted, false);
                }
            }
        }

        return (accepted, true);
    }

    /// <summary>The file names of every attached file, by note: search matches them too.</summary>
    public static Dictionary<Guid, List<string>> AttachmentNames(SqliteConnection connection)
    {
        using var command = Sql.Command(connection, "SELECT NoteId, FileName FROM Attachments WHERE NoteId IS NOT NULL");
        var names = new Dictionary<Guid, List<string>>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetId(0);
            if (!names.TryGetValue(id, out var list))
            {
                names[id] = list = [];
            }

            list.Add(reader.GetString(1));
        }

        return names;
    }

    private static string Where(SqliteCommand command, NoteQuery query, NoteCursor? cursor, bool kindsByIndex, out string orderColumn)
    {
        var where = new List<string>();
        if (query.Kinds.Distinct().Count() < NoteKinds.All.Count)
        {
            where.Add($"{(kindsByIndex ? "" : "+")}n.Kind IN ({command.InList("$kind", query.Kinds.Distinct().Select(k => (object)(int)k))})");
        }

        switch (query.State)
        {
            case NoteState.Feed or NoteState.Pinned:
                // Compared with a parameter so SQLite seeks the index on it.
                where.Add("n.IsPinned = $pinned AND n.ArchivedAt IS NULL AND n.TrashedAt IS NULL");
                command.With("$pinned", query.State == NoteState.Pinned ? 1 : 0);
                break;
            case NoteState.Active:
                where.Add("n.ArchivedAt IS NULL AND n.TrashedAt IS NULL");
                break;
            case NoteState.Archived:
                where.Add("n.ArchivedAt IS NOT NULL AND n.TrashedAt IS NULL");
                break;
            case NoteState.Trash:
                where.Add("n.TrashedAt IS NOT NULL");
                break;
        }

        if (query.CreatedFromUtc is { } from)
        {
            where.Add("n.CreatedAt >= $from");
            command.With("$from", Sql.Time(from));
        }

        if (query.CreatedBeforeUtc is { } before)
        {
            where.Add("n.CreatedAt < $before");
            command.With("$before", Sql.Time(before));
        }

        if (query.Tag is { } tag)
        {
            where.Add("""
                n.Id IN (SELECT nt.NoteId FROM NoteTags nt
                         WHERE nt.TagId IN (SELECT Id FROM Tags WHERE Name = $tag OR substr(Name, 1, $prefixLength) = $prefix))
                """);
            command.With("$tag", tag).With("$prefix", tag + "/").With("$prefixLength", tag.Length + 1);
        }

        if (query.Label is { } label)
        {
            where.Add("n.Id IN (SELECT NoteId FROM NoteLabels WHERE LabelId = $label)");
            command.With("$label", Sql.Id(label));
        }

        orderColumn = query.State == NoteState.Trash ? "n.TrashedAt" : "n.CreatedAt";
        if (cursor is { } position)
        {
            where.Add($"({orderColumn} < $cursorTime OR ({orderColumn} = $cursorTime AND n.Id < $cursorId))");
            command.With("$cursorTime", Sql.Time(position.Time)).With("$cursorId", Sql.Id(position.Id));
        }

        return string.Join(" AND ", where);
    }

    /// <summary>Reads one note's row.</summary>
    public static NoteRow? GetRow(SqliteConnection connection, Guid id, SqliteTransaction? transaction = null)
    {
        using var command = Sql.Command(connection, $"SELECT {Columns} FROM Notes n WHERE n.Id = $id", transaction).With("$id", Sql.Id(id));
        return ReadRows(command).SingleOrDefault();
    }

    /// <summary>Reads the row of a day's daily note.</summary>
    public static NoteRow? GetDailyRow(SqliteConnection connection, DateOnly date)
    {
        using var command = Sql.Command(connection, $"SELECT {Columns} FROM Notes n WHERE n.DailyDate = $date").With("$date", Sql.Date(date));
        return ReadRows(command).SingleOrDefault();
    }

    /// <summary>Reads every habit that is not in the trash, oldest first (the Habits page).</summary>
    public static List<NoteRow> HabitRows(SqliteConnection connection)
    {
        using var command = Sql.Command(connection, $"""
            SELECT {Columns} FROM Notes n
            WHERE n.Kind = {(int)NoteKind.Habit} AND n.TrashedAt IS NULL
            ORDER BY n.CreatedAt, n.Id
            """);
        return ReadRows(command);
    }

    /// <summary>The text of these notes.</summary>
    public static Dictionary<Guid, string> Bodies(SqliteConnection connection, IReadOnlyCollection<Guid> ids, SqliteTransaction? transaction = null)
    {
        var bodies = new Dictionary<Guid, string>();
        if (ids.Count == 0)
        {
            return bodies;
        }

        using var command = Sql.Command(connection, "", transaction);
        command.CommandText = $"SELECT NoteId, Content FROM NoteBodies WHERE NoteId IN ({command.InList("$id", ids.Select(id => (object)Sql.Id(id)))})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            bodies[reader.GetId(0)] = reader.GetString(1);
        }

        return bodies;
    }

    /// <summary>The files of these notes, oldest first.</summary>
    public static Dictionary<Guid, List<AttachmentRow>> AttachmentsOf(
        SqliteConnection connection, IReadOnlyCollection<Guid> noteIds, SqliteTransaction? transaction = null)
    {
        var result = new Dictionary<Guid, List<AttachmentRow>>();
        if (noteIds.Count == 0)
        {
            return result;
        }

        using var command = Sql.Command(connection, "", transaction);
        command.CommandText = $"""
            SELECT Id, NoteId, FileName, ContentType, SizeBytes, CreatedAt, Revision, StorageKey FROM Attachments
            WHERE NoteId IN ({command.InList("$id", noteIds.Select(id => (object)Sql.Id(id)))})
            ORDER BY CreatedAt, Id
            """;
        foreach (var row in ReadAttachments(command))
        {
            var noteId = row.Attachment.NoteId!.Value;
            if (!result.TryGetValue(noteId, out var list))
            {
                result[noteId] = list = [];
            }

            list.Add(row);
        }

        return result;
    }

    /// <summary>Reads attachment rows from a command selecting the columns <see cref="AttachmentsOf"/> selects.</summary>
    public static List<AttachmentRow> ReadAttachments(SqliteCommand command)
    {
        var rows = new List<AttachmentRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new AttachmentRow(
                new Attachment(reader.GetId(0), reader.GetIdOrNull(1), reader.GetString(2), reader.GetString(3),
                    reader.GetInt64(4), reader.GetTime(5), reader.GetInt64(6)),
                reader.GetString(7)));
        }

        return rows;
    }

    /// <summary>Turns rows into notes, reading their text (unless given), tags, files and labels.</summary>
    public static List<Note> Load(
        SqliteConnection connection, IReadOnlyList<NoteRow> rows, IReadOnlyDictionary<Guid, string>? bodies = null,
        SqliteTransaction? transaction = null)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        bodies ??= Bodies(connection, ids, transaction);
        var files = AttachmentsOf(connection, ids, transaction);
        var tags = Pairs(connection, transaction, ids, "SELECT nt.NoteId, t.Name FROM NoteTags nt JOIN Tags t ON t.Id = nt.TagId WHERE nt.NoteId IN ({0})");
        var labels = Pairs(connection, transaction, ids, "SELECT NoteId, LabelId FROM NoteLabels WHERE NoteId IN ({0})");

        return rows.Select(row => new Note(
            row.Id,
            row.Kind,
            bodies.GetValueOrDefault(row.Id, ""),
            row.DailyDate,
            row.IsPinned,
            row.ArchivedAtUtc,
            row.TrashedAtUtc,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Revision,
            tags.GetValueOrDefault(row.Id, []).Order(StringComparer.Ordinal).ToList(),
            files.GetValueOrDefault(row.Id, []).Select(f => f.Attachment).ToList(),
            labels.GetValueOrDefault(row.Id, []).Order(StringComparer.Ordinal).Select(id => Guid.ParseExact(id, "D")).ToList()))
            .ToList();
    }

    /// <summary>Replaces a note's tags with those in its text, creating tags that are new.</summary>
    public static void SetTags(SqliteConnection connection, SqliteTransaction transaction, Guid noteId, IReadOnlyList<string> names)
    {
        using (var clear = Sql.Command(connection, "DELETE FROM NoteTags WHERE NoteId = $note", transaction))
        {
            clear.With("$note", Sql.Id(noteId)).ExecuteNonQuery();
        }

        foreach (var name in names)
        {
            using var command = Sql.Command(connection, """
                INSERT INTO Tags (Name) VALUES ($name) ON CONFLICT (Name) DO NOTHING;
                INSERT OR IGNORE INTO NoteTags (NoteId, TagId) SELECT $note, Id FROM Tags WHERE Name = $name;
                """, transaction);
            command.With("$name", name).With("$note", Sql.Id(noteId)).ExecuteNonQuery();
        }
    }

    /// <summary>Deletes tags no note uses any more.</summary>
    public static void RemoveUnusedTags(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = Sql.Command(connection,
            "DELETE FROM Tags WHERE NOT EXISTS (SELECT 1 FROM NoteTags WHERE TagId = Tags.Id)", transaction);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Attaches files added while the note was written. They must exist and belong to no note yet.
    /// </summary>
    /// <exception cref="UserFacingException">A file does not exist or is attached to another note.</exception>
    public static void Attach(SqliteConnection connection, SqliteTransaction transaction, Guid noteId, IReadOnlyCollection<Guid> attachmentIds)
    {
        var ids = attachmentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        using var command = Sql.Command(connection, "", transaction);
        command.CommandText = $"UPDATE Attachments SET NoteId = $note WHERE NoteId IS NULL AND Id IN ({command.InList("$id", ids.Select(id => (object)Sql.Id(id)))})";
        if (command.With("$note", Sql.Id(noteId)).ExecuteNonQuery() != ids.Count)
        {
            throw new UserFacingException("One or more files do not exist or are already attached to another note.", "attachmentIds");
        }
    }

    /// <summary>
    /// Deletes notes for good: their rows (files, tags and labels links go by cascade), then unused tags. Returns the
    /// files' storage keys, for the caller to delete after the transaction commits.
    /// </summary>
    public static (int Notes, List<string> StorageKeys) Delete(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyCollection<Guid> noteIds)
    {
        if (noteIds.Count == 0)
        {
            return (0, []);
        }

        var keys = AttachmentsOf(connection, noteIds, transaction).Values.SelectMany(list => list).Select(f => f.StorageKey).ToList();
        using var command = Sql.Command(connection, "", transaction);
        command.CommandText = $"DELETE FROM Notes WHERE Id IN ({command.InList("$id", noteIds.Select(id => (object)Sql.Id(id)))})";
        var deleted = command.ExecuteNonQuery();
        RemoveUnusedTags(connection, transaction);
        return (deleted, keys);
    }

    /// <summary>The IDs of notes matching a condition (no parameters).</summary>
    public static List<Guid> Ids(SqliteConnection connection, SqliteTransaction? transaction, string where, params (string Name, object Value)[] parameters)
    {
        using var command = Sql.Command(connection, $"SELECT Id FROM Notes WHERE {where}", transaction);
        foreach (var (name, value) in parameters)
        {
            command.With(name, value);
        }

        var ids = new List<Guid>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetId(0));
        }

        return ids;
    }

    private static Dictionary<Guid, List<string>> Pairs(SqliteConnection connection, SqliteTransaction? transaction, IReadOnlyCollection<Guid> ids, string sql)
    {
        using var command = Sql.Command(connection, "", transaction);
        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, sql, command.InList("$id", ids.Select(id => (object)Sql.Id(id))));
        var result = new Dictionary<Guid, List<string>>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetId(0);
            if (!result.TryGetValue(id, out var list))
            {
                result[id] = list = [];
            }

            list.Add(reader.GetString(1));
        }

        return result;
    }

    private static List<NoteRow> ReadRows(SqliteCommand command)
    {
        var rows = new List<NoteRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(ReadRow(reader));
        }

        return rows;
    }

    private static NoteRow ReadRow(SqliteDataReader reader) =>
        new(
            reader.GetId(0),
            (NoteKind)reader.GetInt32(1),
            reader.GetDateOrNull(2),
            reader.GetInt64(3) != 0,
            reader.GetTimeOrNull(4),
            reader.GetTimeOrNull(5),
            reader.GetTime(6),
            reader.GetTime(7),
            reader.GetInt64(8));

    /// <summary>The UTF-8 length of a note's text, for storage use.</summary>
    public static long ContentBytes(string content) => Encoding.UTF8.GetByteCount(content);
}
