using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace MapleBench;

// Measures the storage options for an offline Maple Notes app with the queries its screens make:
//   sqlite-enc  SQLite3 Multiple Ciphers, SQLCipher v4 format (whole database encrypted), WAL, secure_delete
//   sqlite      the same engine and pragmas, no key
//   md-folder   one Markdown file per note with front matter, an in-memory index built at startup
// Every result line: MAPLEBENCH|variant|notes|operation|first ms|median ms|extra

public sealed record Result(string Variant, int Notes, string Op, double FirstMs, double MedianMs, string Extra = "");

public static partial class Bench
{
    public static List<Result> Results { get; } = [];
    static Action<string> _log = Console.WriteLine;

    static void Report(Result r)
    {
        Results.Add(r);
        _log($"MAPLEBENCH|{r.Variant}|{r.Notes}|{r.Op}|{r.FirstMs:F2}|{r.MedianMs:F2}|{r.Extra}");
    }

    public static void Run(string root, int[] sizes, Action<string> log)
    {
        _log = log;
        _log($"MAPLEBENCH|env|{Environment.OSVersion}|cores={Environment.ProcessorCount}|{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        foreach (var n in sizes)
        {
            var data = Generate(n);
            foreach (var cipher in Ciphers)
            {
                var dir = Path.Combine(root, $"sqlite-{cipher}-{n}");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                SqliteBench(dir, data, cipher);
            }

            var mdDir = Path.Combine(root, $"md-{n}");
            if (Directory.Exists(mdDir)) Directory.Delete(mdDir, true);
            Directory.CreateDirectory(mdDir);
            MdBench(mdDir, data);
            GC.Collect();
        }
        _log("MAPLEBENCH|done");
    }

    // ---------------------------------------------------------------- data

    public sealed class GenNote
    {
        public Guid Id;
        public string Content = "";
        public int Kind; // 0 note, 1 todo, 2 quick, 3 habit
        public string? DailyDate;
        public bool Pinned;
        public DateTime? Archived, Trashed;
        public DateTime Created, Updated;
        public List<string> Labels = [];
        public List<(Guid Id, string Name, string Type, long Size, DateTime Created)> Files = [];
    }

    static readonly string[] Words = ("maple syrup meeting budget garden project idea call email review plan draft travel " +
        "book read write code fix release design colour coffee walk run family weekend dinner recipe café résumé " +
        "naïve 日本語 テキスト report invoice doctor school train weather photo video music film list notes later today " +
        "tomorrow important quick thought remember buy sell price market news article link question answer").Split(' ');

    static readonly string[] TagPool = Enumerable.Range(0, 40).Select(i => $"topic{i}")
        .Concat(["work", "work/meetings", "work/1on1", "home", "home/garden", "ideas", "groceries", "health", "travel/japan", "reading"])
        .ToArray();

    public static List<GenNote> Generate(int n)
    {
        var rng = new Random(42);
        var start = DateTime.UtcNow.AddYears(-3);
        var span = (DateTime.UtcNow - start).TotalSeconds;
        var times = Enumerable.Range(0, n).Select(_ => start.AddSeconds(rng.NextDouble() * span)).Order().ToList();
        var labels = Enumerable.Range(0, 12).Select(i => $"L{i}").ToArray();
        var notes = new List<GenNote>(n);
        var dailyUsed = new HashSet<string>();
        for (var i = 0; i < n; i++)
        {
            var created = times[i];
            var note = new GenNote { Id = Guid.CreateVersion7(new DateTimeOffset(created)), Created = created, Updated = created };
            var roll = rng.NextDouble();
            note.Kind = i < 12 ? 3 : roll < 0.08 ? 1 : roll < 0.15 ? 2 : 0;
            var size = rng.NextDouble() switch { < 0.6 => rng.Next(40, 200), < 0.9 => rng.Next(200, 1000), _ => rng.Next(1000, 5000) };
            var text = new StringBuilder();
            if (note.Kind == 3)
            {
                text.Append("# Habit ").Append(i).Append("\n\n");
                for (var d = 0; d < 300; d++) if (rng.NextDouble() < 0.6) text.Append("- ").Append(DateOnly.FromDateTime(created.AddDays(d)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            }
            else if (note.Kind == 1)
            {
                text.Append("# List ").Append(i).Append("\n\n");
                for (var k = rng.Next(2, 12); k > 0; k--) text.Append(rng.NextDouble() < 0.4 ? "- [x] " : "- [ ] ").Append(Words[rng.Next(Words.Length)]).Append('\n');
            }
            while (text.Length < size)
            {
                text.Append(Words[rng.Next(Words.Length)]).Append(rng.NextDouble() < 0.1 ? ".\n" : " ");
                if (rng.NextDouble() < 0.02) text.Append('#').Append(TagPool[rng.Next(TagPool.Length)]).Append(' ');
            }
            note.Content = text.ToString().TrimEnd();
            note.Pinned = i % 400 == 7;
            if (rng.NextDouble() < 0.10) note.Archived = created.AddDays(1);
            if (rng.NextDouble() < 0.02) note.Trashed = DateTime.UtcNow.AddDays(-rng.Next(0, 29));
            if (note.Kind == 0 && rng.NextDouble() < 0.05)
            {
                var day = DateOnly.FromDateTime(created).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (dailyUsed.Add(day)) note.DailyDate = day;
            }
            if (rng.NextDouble() < 0.2) note.Labels.Add(labels[rng.Next(labels.Length)]);
            if (rng.NextDouble() < 0.3)
                for (var f = rng.Next(1, 4); f > 0; f--)
                    note.Files.Add((Guid.CreateVersion7(), $"photo-{i}-{f}.jpg", "image/jpeg", rng.Next(50_000, 4_000_000), created.AddSeconds(f)));
            if (rng.NextDouble() < 0.2) note.Updated = created.AddMinutes(rng.Next(2, 10_000));
            notes.Add(note);
        }
        return notes;
    }

    // Same pattern as the app's TagParser.
    [GeneratedRegex(@"```[\s\S]*?(?:```|\z)|`[^`\n]*`", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
    [GeneratedRegex(@"(?<![\p{L}\p{N}_/#&])#(?<tag>[\p{L}\p{N}_][\p{L}\p{N}_/-]{0,63})", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    public static List<string> Tags(string markdown) =>
        TagPattern().Matches(CodePattern().Replace(markdown, " "))
            .Select(m => m.Groups["tag"].Value.TrimEnd('/', '-').ToLowerInvariant())
            .Where(t => t.Length > 0 && !t.All(char.IsAsciiDigit)).Distinct(StringComparer.Ordinal).ToList();

    // ---------------------------------------------------------------- timing

    static double Time(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        return sw.Elapsed.TotalMilliseconds;
    }

    static void Measure(string variant, int n, string op, Action action, int runs = 10, string extra = "")
    {
        var first = Time(action);
        var times = Enumerable.Range(0, runs).Select(_ => Time(action)).Order().ToList();
        Report(new Result(variant, n, op, first, times[times.Count / 2], extra));
    }

    static string Ts(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture); // EF Core's SQLite format
    static string G(Guid g) => g.ToString().ToUpperInvariant(); // EF Core stores GUIDs as upper-case text

    // ---------------------------------------------------------------- SQLite

    const string Schema = """
        CREATE TABLE Notes(Id TEXT PRIMARY KEY, Kind INTEGER NOT NULL, DailyDate TEXT NULL,
          IsPinned INTEGER NOT NULL, ArchivedAtUtc TEXT NULL, TrashedAtUtc TEXT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);
        CREATE TABLE NoteBodies(NoteId TEXT PRIMARY KEY, Content TEXT NOT NULL);
        CREATE INDEX IX_Notes_Created ON Notes(CreatedAtUtc, Id);
        CREATE INDEX IX_Notes_Kind_Created ON Notes(Kind, CreatedAtUtc, Id);
        CREATE INDEX IX_Notes_List ON Notes(Kind, IsPinned, ArchivedAtUtc, TrashedAtUtc, CreatedAtUtc, Id);
        CREATE UNIQUE INDEX IX_Notes_Daily ON Notes(DailyDate) WHERE DailyDate IS NOT NULL;
        CREATE INDEX IX_Notes_Trashed ON Notes(TrashedAtUtc);
        CREATE TABLE Tags(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE);
        CREATE TABLE NoteTags(NoteId TEXT NOT NULL, TagId INTEGER NOT NULL, PRIMARY KEY(NoteId, TagId)) WITHOUT ROWID;
        CREATE INDEX IX_NoteTags_Tag ON NoteTags(TagId);
        CREATE TABLE Labels(Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Color TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL);
        CREATE TABLE NoteLabels(NoteId TEXT NOT NULL, LabelId TEXT NOT NULL, PRIMARY KEY(NoteId, LabelId)) WITHOUT ROWID;
        CREATE INDEX IX_NoteLabels_Label ON NoteLabels(LabelId);
        CREATE TABLE Attachments(Id TEXT PRIMARY KEY, NoteId TEXT NULL, FileName TEXT NOT NULL, ContentType TEXT NOT NULL,
          SizeBytes INTEGER NOT NULL, StorageKey TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL);
        CREATE INDEX IX_Attachments_Note ON Attachments(NoteId, CreatedAtUtc);
        """;

    /// <summary>plain: no key. sqlcipher: SQLCipher v4 format (AES-256-CBC + HMAC-SHA512). chacha20: ChaCha20-Poly1305
    /// (SQLite3MC's default). aegis: AEGIS-256 (AES-based AEAD, fast with hardware AES).</summary>
    public static string[] Ciphers = ["plain", "sqlcipher", "chacha20", "aegis"];

    public static string ConnectionString(string path, string cipher)
    {
        var builder = new SqliteConnectionStringBuilder { Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
        if (cipher == "plain")
        {
            builder.DataSource = path;
        }
        else
        {
            builder.DataSource = cipher == "sqlcipher" ? $"file:{path}?cipher=sqlcipher&legacy=4" : $"file:{path}?cipher={cipher}";
            builder.Password = "x'" + Convert.ToHexString(Enumerable.Range(0, 32).Select(i => (byte)(i * 7 + 3)).ToArray()) + "'";
        }
        return builder.ToString();
    }

    public static SqliteConnection Open(string path, string cipher)
    {
        var connection = new SqliteConnection(ConnectionString(path, cipher));
        connection.Open();
        Exec(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA secure_delete=ON;");
        return connection;
    }

    static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    sealed class Writer : IDisposable
    {
        readonly SqliteCommand _note, _tag, _tagId, _noteTag, _label, _file;
        readonly Dictionary<string, long> _tags = [];
        public Writer(SqliteConnection c)
        {
            _note = Cmd(c, "INSERT INTO Notes VALUES($id,$kind,$daily,$pinned,$archived,$trashed,$created,$updated); INSERT INTO NoteBodies VALUES($id,$content)", "$id", "$content", "$kind", "$daily", "$pinned", "$archived", "$trashed", "$created", "$updated");
            _tag = Cmd(c, "INSERT OR IGNORE INTO Tags(Name) VALUES($name)", "$name");
            _tagId = Cmd(c, "SELECT Id FROM Tags WHERE Name=$name", "$name");
            _noteTag = Cmd(c, "INSERT OR IGNORE INTO NoteTags VALUES($note,$tag)", "$note", "$tag");
            _label = Cmd(c, "INSERT INTO NoteLabels VALUES($note,$label)", "$note", "$label");
            _file = Cmd(c, "INSERT INTO Attachments VALUES($id,$note,$name,$type,$size,$key,$created)", "$id", "$note", "$name", "$type", "$size", "$key", "$created");
        }

        static SqliteCommand Cmd(SqliteConnection c, string sql, params string[] names)
        {
            var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            foreach (var name in names) cmd.Parameters.Add(new SqliteParameter(name, null));
            return cmd;
        }

        public void Insert(GenNote n, SqliteTransaction tx)
        {
            foreach (var cmd in new[] { _note, _tag, _tagId, _noteTag, _label, _file }) cmd.Transaction = tx;
            var id = G(n.Id);
            Set(_note, id, n.Content, n.Kind, (object?)n.DailyDate ?? DBNull.Value, n.Pinned ? 1 : 0,
                n.Archived is { } a ? Ts(a) : DBNull.Value, n.Trashed is { } t ? Ts(t) : DBNull.Value, Ts(n.Created), Ts(n.Updated));
            _note.ExecuteNonQuery();
            foreach (var name in Tags(n.Content))
            {
                if (!_tags.TryGetValue(name, out var tagId))
                {
                    Set(_tag, name); _tag.ExecuteNonQuery();
                    Set(_tagId, name); tagId = (long)_tagId.ExecuteScalar()!;
                    _tags[name] = tagId;
                }
                Set(_noteTag, id, tagId); _noteTag.ExecuteNonQuery();
            }
            foreach (var label in n.Labels) { Set(_label, id, label); _label.ExecuteNonQuery(); }
            foreach (var f in n.Files)
            {
                var fid = G(f.Id);
                Set(_file, fid, id, f.Name, f.Type, f.Size, $"{fid[..2]}/{fid[2..4]}/{fid}.bin", Ts(f.Created));
                _file.ExecuteNonQuery();
            }
        }

        static void Set(SqliteCommand cmd, params object[] values)
        {
            for (var i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i];
        }

        public void Dispose()
        {
            foreach (var cmd in new[] { _note, _tag, _tagId, _noteTag, _label, _file }) cmd.Dispose();
        }
    }

    sealed record Row(string Id, string Content, long Kind, string Created);

    const string NoteColumns = "Id, (SELECT Content FROM NoteBodies b WHERE b.NoteId = Notes.Id), Kind, DailyDate, IsPinned, ArchivedAtUtc, TrashedAtUtc, CreatedAtUtc, UpdatedAtUtc";

    // One page of a list, the way the app shows it: the notes, then their tags, labels and attachments (split query).
    static int Page(SqliteConnection c, string where, (string, object)[] args, (string Created, string Id)? after = null, int limit = 21)
    {
        var rows = new List<Row>();
        using (var cmd = c.CreateCommand())
        {
            var cursor = after is null ? "" : " AND (CreatedAtUtc < $c OR (CreatedAtUtc = $c AND Id < $i))";
            cmd.CommandText = $"SELECT {NoteColumns} FROM Notes WHERE {where}{cursor} ORDER BY CreatedAtUtc DESC, Id DESC LIMIT {limit}";
            foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
            if (after is { } a) { cmd.Parameters.AddWithValue("$c", a.Created); cmd.Parameters.AddWithValue("$i", a.Id); }
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) rows.Add(new Row(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(7)));
        }
        if (rows.Count == 0) return 0;
        var ids = string.Join(",", rows.Select(r => $"'{r.Id}'"));
        var count = 0;
        foreach (var sql in new[]
        {
            $"SELECT nt.NoteId, t.Name FROM NoteTags nt JOIN Tags t ON t.Id = nt.TagId WHERE nt.NoteId IN ({ids})",
            $"SELECT NoteId, LabelId FROM NoteLabels WHERE NoteId IN ({ids})",
            $"SELECT Id, NoteId, FileName, ContentType, SizeBytes, CreatedAtUtc FROM Attachments WHERE NoteId IN ({ids}) ORDER BY CreatedAtUtc",
        })
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) count++;
        }
        return rows.Count + count;
    }

    const string Feed = "Kind = $k AND IsPinned = $p AND ArchivedAtUtc IS NULL AND TrashedAtUtc IS NULL";
    const string Active = "Kind IN (0, 1, 2) AND ArchivedAtUtc IS NULL AND TrashedAtUtc IS NULL";

    static void SqliteBench(string dir, List<GenNote> data, string cipher)
    {
        var variant = cipher == "plain" ? "sqlite" : $"sqlite-{cipher}";
        var n = data.Count;
        var path = Path.Combine(dir, "maple.db");

        // Restore-style bulk load: every note with its tags, labels and attachment rows, in one transaction.
        var seed = Time(() =>
        {
            using var c = Open(path, cipher);
            Exec(c, Schema);
            using var tx = c.BeginTransaction();
            for (var i = 0; i < 12; i++) Exec(c, $"INSERT INTO Labels VALUES('L{i}','Label {i}','Blue','{Ts(DateTime.UtcNow)}')", tx);
            using var writer = new Writer(c);
            foreach (var note in data) writer.Insert(note, tx);
            tx.Commit();
        });
        Report(new Result(variant, n, "bulk load (one transaction)", seed, seed, $"{seed / n * 1000:F0} µs/note"));

        // Cold: a new connection (key setup, schema read, empty page cache) and the first feed page.
        SqliteConnection.ClearAllPools();
        var cold = Time(() =>
        {
            using var c = Open(path, cipher);
            Page(c, Feed, [("$k", 0), ("$p", 0)]);
        });
        Report(new Result(variant, n, "open + first feed page (cold)", cold, cold));

        using var conn = Open(path, cipher);
        var size = new FileInfo(path).Length + (File.Exists(path + "-wal") ? new FileInfo(path + "-wal").Length : 0);
        var header = new byte[16];
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) file.ReadExactly(header);
        var readable = Encoding.ASCII.GetString(header).StartsWith("SQLite format 3", StringComparison.Ordinal);
        Report(new Result(variant, n, "database size", 0, 0, $"{size / 1024.0 / 1024.0:F1} MB, {(readable ? "readable header" : "encrypted")}"));

        Measure(variant, n, "feed: first page", () => Page(conn, Feed, [("$k", 0), ("$p", 0)]));
        (string, string) deep;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT CreatedAtUtc, Id FROM Notes WHERE Kind = 0 AND IsPinned = 0 AND ArchivedAtUtc IS NULL AND TrashedAtUtc IS NULL ORDER BY CreatedAtUtc DESC, Id DESC LIMIT 1 OFFSET {Math.Min(1000, n / 2)}";
            using var r = cmd.ExecuteReader();
            r.Read();
            deep = (r.GetString(0), r.GetString(1));
        }
        Measure(variant, n, "feed: page after scrolling 1,000 notes", () => Page(conn, Feed, [("$k", 0), ("$p", 0)], deep));
        Measure(variant, n, "pinned list", () => Page(conn, Feed, [("$k", 0), ("$p", 1)], null, 101));
        Measure(variant, n, "tag counts (side menu, Tags page)", () =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT t.Name, COUNT(*) FROM Notes n JOIN NoteTags nt ON nt.NoteId = n.Id JOIN Tags t ON t.Id = nt.TagId WHERE n.Kind IN (0,1,2) AND n.ArchivedAtUtc IS NULL AND n.TrashedAtUtc IS NULL GROUP BY t.Id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) { }
        });
        Measure(variant, n, "label counts", () =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT nl.LabelId, COUNT(*) FROM Notes n JOIN NoteLabels nl ON nl.NoteId = n.Id WHERE n.Kind IN (0,1,2) AND n.ArchivedAtUtc IS NULL AND n.TrashedAtUtc IS NULL GROUP BY nl.LabelId";
            using var r = cmd.ExecuteReader();
            while (r.Read()) { }
        });
        var monthStart = Ts(DateTime.UtcNow.AddDays(-45));
        var monthEnd = Ts(DateTime.UtcNow.AddDays(-14));
        Measure(variant, n, "calendar month counts", () =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT CreatedAtUtc FROM Notes WHERE {Active} AND CreatedAtUtc >= $s AND CreatedAtUtc < $e";
            cmd.Parameters.AddWithValue("$s", monthStart);
            cmd.Parameters.AddWithValue("$e", monthEnd);
            using var r = cmd.ExecuteReader();
            var days = new Dictionary<DateOnly, int>();
            while (r.Read())
            {
                var day = DateOnly.FromDateTime(DateTime.Parse(r.GetString(0), CultureInfo.InvariantCulture).ToLocalTime());
                days[day] = days.GetValueOrDefault(day) + 1;
            }
        });
        Measure(variant, n, "tag filter #work (nested): first page", () =>
            Page(conn, $"{Active} AND EXISTS (SELECT 1 FROM NoteTags nt JOIN Tags t ON t.Id = nt.TagId WHERE nt.NoteId = Notes.Id AND (t.Name = 'work' OR substr(t.Name, 1, 5) = 'work/'))", []));
        Measure(variant, n, "label filter: first page", () =>
            Page(conn, $"{Active} AND EXISTS (SELECT 1 FROM NoteLabels nl WHERE nl.NoteId = Notes.Id AND nl.LabelId = 'L3')", []));
        Measure(variant, n, "habits page (all habits)", () => Page(conn, "Kind = 3 AND ArchivedAtUtc IS NULL AND TrashedAtUtc IS NULL", [], null, 1000));
        Measure(variant, n, "search with no match (scans every note)", () =>
        {
            var matches = 0;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT n.Id, b.Content FROM Notes n JOIN NoteBodies b ON b.NoteId = n.Id WHERE n.Kind IN (0, 1, 2) AND n.ArchivedAtUtc IS NULL AND n.TrashedAtUtc IS NULL";
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (r.GetString(1).Contains("zebra-crossing", StringComparison.OrdinalIgnoreCase)) matches++;
        }, runs: 5);

        // Writes, each in its own transaction as the app makes them.
        var rng = new Random(7);
        var extra = Generate(60).Skip(12).Take(40).ToList();
        foreach (var note in extra) { note.Id = Guid.CreateVersion7(); note.DailyDate = null; }
        var postTimes = new List<double>();
        using (var writer = new Writer(conn))
        {
            foreach (var note in extra)
            {
                postTimes.Add(Time(() =>
                {
                    using var tx = conn.BeginTransaction();
                    writer.Insert(note, tx);
                    tx.Commit();
                }));
            }
        }
        postTimes.Sort();
        Report(new Result(variant, n, "post a note (insert + tags, own transaction)", postTimes[0], postTimes[postTimes.Count / 2]));

        var ids = data.Where((_, i) => i % Math.Max(1, n / 40) == 0).Select(d => G(d.Id)).Take(40).ToList();
        var pinTimes = ids.Select(id => Time(() =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Notes SET IsPinned = 1 - IsPinned WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        })).Order().ToList();
        Report(new Result(variant, n, "toggle pin", pinTimes[0], pinTimes[pinTimes.Count / 2]));

        var editTimes = ids.Select(id => Time(() =>
        {
            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE NoteBodies SET Content = Content || ' edited #home' WHERE NoteId = $id; UPDATE Notes SET UpdatedAtUtc = $u WHERE Id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$u", Ts(DateTime.UtcNow));
                cmd.ExecuteNonQuery();
                cmd.CommandText = "DELETE FROM NoteTags WHERE NoteId = $id; INSERT OR IGNORE INTO NoteTags SELECT $id, Id FROM Tags WHERE Name = 'home';";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        })).Order().ToList();
        Report(new Result(variant, n, "edit a note (update + re-tag)", editTimes[0], editTimes[editTimes.Count / 2]));
    }

    // ---------------------------------------------------------------- Markdown folder

    sealed class MdEntry
    {
        public required string Path;
        public Guid Id;
        public int Kind;
        public bool Pinned;
        public bool Archived, Trashed;
        public DateTime Created;
        public List<string> Tags = [];
        public List<string> Labels = [];
        public List<string> Files = [];
        public string Content = "";
    }

    static string MdText(GenNote n) => new StringBuilder()
        .Append("---\n")
        .Append("id: ").Append(n.Id).Append('\n')
        .Append("kind: ").Append(n.Kind switch { 1 => "todo", 2 => "quick", 3 => "habit", _ => "note" }).Append('\n')
        .Append(n.DailyDate is null ? "" : $"daily: {n.DailyDate}\n")
        .Append("created: ").Append(n.Created.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
        .Append("updated: ").Append(n.Updated.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
        .Append("pinned: ").Append(n.Pinned ? "true" : "false").Append('\n')
        .Append("archived: ").Append(n.Archived is null ? "false" : "true").Append('\n')
        .Append("trashed: ").Append(n.Trashed is null ? "false" : "true").Append('\n')
        .Append("labels: [").Append(string.Join(", ", n.Labels)).Append("]\n")
        .Append(string.Concat(n.Files.Select(f => $"attachment: {f.Name}\n")))
        .Append("---\n\n").Append(n.Content).Append('\n')
        .ToString();

    static void WriteAtomically(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    static MdEntry ParseMd(string path)
    {
        var text = File.ReadAllText(path);
        var end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
        var entry = new MdEntry { Path = path, Content = text[(end + 6)..] };
        foreach (var line in text[4..end].Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var (key, value) = (line[..colon], line[(colon + 2)..]);
            switch (key)
            {
                case "id": entry.Id = Guid.Parse(value); break;
                case "kind": entry.Kind = value switch { "todo" => 1, "quick" => 2, "habit" => 3, _ => 0 }; break;
                case "created": entry.Created = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
                case "pinned": entry.Pinned = value == "true"; break;
                case "archived": entry.Archived = value == "true"; break;
                case "trashed": entry.Trashed = value == "true"; break;
                case "labels": entry.Labels = value.Trim('[', ']').Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList(); break;
                case "attachment": entry.Files.Add(value); break;
            }
        }
        entry.Tags = Tags(entry.Content);
        return entry;
    }

    static void MdBench(string dir, List<GenNote> data)
    {
        const string variant = "md-folder";
        var n = data.Count;
        string NotePath(GenNote g)
        {
            var folder = Path.Combine(dir, g.Created.ToString("yyyy", CultureInfo.InvariantCulture), g.Created.ToString("MM", CultureInfo.InvariantCulture));
            return Path.Combine(folder, g.Id + ".md");
        }

        var write = Time(() =>
        {
            foreach (var g in data)
            {
                var path = NotePath(g);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                WriteAtomically(path, MdText(g));
            }
        });
        Report(new Result(variant, n, "bulk load (one file per note)", write, write, $"{write / n * 1000:F0} µs/note"));

        // Every app start: read every file to know the order of the feed, the tags, the pins, the counts.
        List<MdEntry> index = [];
        var before = GC.GetTotalMemory(true);
        var build = Time(() =>
        {
            index = Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories).Select(ParseMd)
                .OrderByDescending(e => e.Created).ThenByDescending(e => e.Id).ToList();
        });
        var memory = (GC.GetTotalMemory(true) - before) / 1024.0 / 1024.0;
        Report(new Result(variant, n, "open + first feed page (cold: index every file)", build, build, $"index holds {memory:F0} MB"));
        var size = Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        Report(new Result(variant, n, "database size", 0, 0, $"{size / 1024.0 / 1024.0:F1} MB in {n} files"));

        bool IsActive(MdEntry e) => e.Kind != 3 && !e.Archived && !e.Trashed;
        Measure(variant, n, "feed: first page", () => index.Where(e => e.Kind == 0 && !e.Pinned && !e.Archived && !e.Trashed).Take(20).ToList());
        Measure(variant, n, "feed: page after scrolling 1,000 notes", () => index.Where(e => e.Kind == 0 && !e.Pinned && !e.Archived && !e.Trashed).Skip(Math.Min(1000, n / 2)).Take(20).ToList());
        Measure(variant, n, "tag counts (side menu, Tags page)", () => index.Where(IsActive).SelectMany(e => e.Tags).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count()));
        Measure(variant, n, "search with no match (scans every note)", () => index.Where(IsActive).Count(e => e.Content.Contains("zebra-crossing", StringComparison.OrdinalIgnoreCase)), runs: 5);

        var targets = index.Where((_, i) => i % Math.Max(1, n / 40) == 0).Take(40).ToList();
        var pinTimes = targets.Select(e => Time(() =>
        {
            var text = File.ReadAllText(e.Path);
            e.Pinned = !e.Pinned;
            WriteAtomically(e.Path, text.Replace(e.Pinned ? "pinned: false" : "pinned: true", e.Pinned ? "pinned: true" : "pinned: false"));
        })).Order().ToList();
        Report(new Result(variant, n, "toggle pin (rewrite the file)", pinTimes[0], pinTimes[pinTimes.Count / 2]));

        var extra = Generate(60).Skip(12).Take(40).ToList();
        var postTimes = extra.Select(g => Time(() =>
        {
            g.Id = Guid.CreateVersion7();
            g.Created = DateTime.UtcNow;
            var path = NotePath(g);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomically(path, MdText(g));
            index.Insert(0, ParseMd(path));
        })).Order().ToList();
        Report(new Result(variant, n, "post a note (write a file)", postTimes[0], postTimes[postTimes.Count / 2]));
    }
}
