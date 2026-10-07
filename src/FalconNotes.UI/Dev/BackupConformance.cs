using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Backup.Restore;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Events;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Platform;
using FalconNotes.Core.Settings;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace FalconNotes.UI.Dev;

/// <summary>
/// Debug builds only (Phase 2 acceptance): the backup conformance checks of docs/11 run inside the app on a device, so
/// its ZIP, ICU and time zone data are the ones tested. Each check uses its own throwaway encrypted database in the
/// cache. The full checks are the Core tests; this is their on-device run.
/// </summary>
public static partial class BackupConformance
{
    /// <summary>Runs the checks against the vectors and demo backups in <paramref name="fixtures"/>.</summary>
    /// <param name="fixtures">A folder with export-vectors.json and the demo backups.</param>
    /// <param name="scratch">A folder for the throwaway databases; deleted afterwards.</param>
    /// <param name="log">Receives one line per check.</param>
    /// <returns>Whether every check passed.</returns>
    public static async Task<bool> RunAsync(string fixtures, string scratch, Action<string> log)
    {
        var passed = true;
        using var vectors = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, "export-vectors.json")));
        var exports = vectors.RootElement.GetProperty("exports");
        for (var i = 0; i < exports.GetArrayLength(); i++)
        {
            var query = exports[i].GetProperty("query").GetString()!;
            using var app = await Throwaway.CreateAsync(scratch);
            await SeedAsync(app, vectors.RootElement);
            using var zip = new MemoryStream();
            await app.Exporter.WriteAsync(Options(query), zip);
            zip.Position = 0;
            var got = Entries(zip);
            var want = exports[i].GetProperty("entries").EnumerateObject().Select(e => (e.Name, e.Value.GetString()!)).ToList();
            var same = got.SequenceEqual(want);
            passed &= same;
            log($"export {i + 1} ({query}): {(same ? "matches" : $"DIFFERS at {got.Zip(want).Select((p, n) => (p, n)).FirstOrDefault(x => x.p.First != x.p.Second).p.First.Item1 ?? "the entry count"}")}");

            using var restored = await Throwaway.CreateAsync(scratch);
            using var plan = await restored.Reader.ReadAsync([new RestoreSource("v.zip", () => Task.FromResult<Stream>(new MemoryStream(Archive(exports[i].GetProperty("entries")))))]);
            var result = await restored.Runner.RunAsync(plan.Items);
            var ok = plan.Problems.Count == 0 && result.Failed.Count == 0 && result.Restored == plan.Items.Count;
            passed &= ok;
            log($"restore {i + 1}: {result.Restored} notes, {result.Files} files, {plan.Problems.Count + result.Failed.Count} problems{(ok ? "" : " FAILED")}");
        }

        foreach (var demo in new[] { "maple-notes-demo-json.zip", "maple-notes-demo-markdown.zip" })
        {
            using var app = await Throwaway.CreateAsync(scratch);
            app.Clock.Now = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var path = Path.Combine(fixtures, demo);
            using var plan = await app.Reader.ReadAsync([new RestoreSource(demo, () => Task.FromResult<Stream>(File.OpenRead(path)))]);
            var result = await app.Runner.RunAsync(plan.Items);
            var ok = plan.Problems.Count == 0 && result.Failed.Count == 0 && result.Restored == 35 && result.Files == 8;
            passed &= ok;
            log($"{demo}: {RestoreRunner.Summary(result)}{(ok ? "" : " FAILED")}");
        }

        try
        {
            Directory.Delete(scratch, recursive: true);
        }
        catch (IOException)
        {
            // Left for the next start's clean-up of the cache.
        }

        log(passed ? "ALL PASSED" : "SOME FAILED");
        return passed;
    }

    private static async Task SeedAsync(Throwaway app, JsonElement root)
    {
        await app.Profile.CreateAsync(root.GetProperty("account").GetString());
        var notes = root.GetProperty("active").GetProperty("items").EnumerateArray().Concat(root.GetProperty("archived").GetProperty("items").EnumerateArray());
        foreach (var note in notes)
        {
            var id = note.GetProperty("id").GetGuid();
            var content = note.GetProperty("content").GetString()!;
            var created = Utc(note.GetProperty("createdAtUtc"));
            var files = new List<(Attachment, string)>();
            foreach (var file in note.GetProperty("attachments").EnumerateArray())
            {
                var attachmentId = file.GetProperty("id").GetGuid();
                var key = AttachmentStore.CreateStorageKey(attachmentId);
                var bytes = Convert.FromBase64String(root.GetProperty("files").GetProperty(attachmentId.ToString()).GetString()!);
                using var attachmentKey = app.Storage.Keys.CreateAttachmentKey();
                await app.Store.WriteAsync(key, (s, ct) => AttachmentCipher.EncryptAsync(new MemoryStream(bytes), s, attachmentKey, app.Storage.InstallationId, attachmentId, ct));
                files.Add((new Attachment(attachmentId, id, file.GetProperty("fileName").GetString()!, file.GetProperty("contentType").GetString()!,
                    file.GetProperty("sizeBytes").GetInt64(), Utc(file.GetProperty("createdAtUtc")), 0), key));
            }

            await app.Storage.Database.InTransactionAsync((connection, transaction) =>
            {
                var daily = note.GetProperty("dailyDate");
                using var insert = Sql.Command(connection, """
                    INSERT INTO Notes (Id, Kind, DailyDate, IsPinned, ArchivedAt, CreatedAt, UpdatedAt, ContentBytes)
                    VALUES ($id, $kind, $daily, $pinned, $archived, $created, $updated, 0);
                    INSERT INTO NoteBodies (NoteId, Content) VALUES ($id, $content);
                    INSERT INTO Tags (Name) SELECT value FROM json_each($tags) WHERE true ON CONFLICT (Name) DO NOTHING;
                    INSERT INTO NoteTags (NoteId, TagId) SELECT $id, Id FROM Tags WHERE Name IN (SELECT value FROM json_each($tags));
                    """, transaction);
                insert.With("$id", Sql.Id(id)).With("$kind", (int)Enum.Parse<NoteKind>(note.GetProperty("kind").GetString()!))
                    .With("$daily", daily.ValueKind == JsonValueKind.Null ? null : daily.GetString())
                    .With("$pinned", note.GetProperty("isPinned").GetBoolean() ? 1 : 0)
                    .With("$archived", note.GetProperty("isArchived").GetBoolean() ? Sql.Time(created) : null)
                    .With("$created", Sql.Time(created)).With("$updated", Sql.Time(Utc(note.GetProperty("updatedAtUtc"))))
                    .With("$content", content).With("$tags", JsonSerializer.Serialize(TagParser.Extract(content))).ExecuteNonQuery();
                foreach (var (attachment, key) in files)
                {
                    using var row = Sql.Command(connection, """
                        INSERT INTO Attachments (Id, NoteId, FileName, ContentType, SizeBytes, StorageKey, CreatedAt)
                        VALUES ($id, $note, $name, $type, $size, $key, $created)
                        """, transaction);
                    row.With("$id", Sql.Id(attachment.Id)).With("$note", Sql.Id(id)).With("$name", attachment.FileName)
                        .With("$type", attachment.ContentType).With("$size", attachment.SizeBytes).With("$key", key)
                        .With("$created", Sql.Time(attachment.CreatedAtUtc)).ExecuteNonQuery();
                }

                return true;
            });
        }
    }

    private static ExportOptions Options(string query)
    {
        var values = query.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        DateOnly? Date(string key) => values.TryGetValue(key, out var v) ? DateOnly.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        return new ExportOptions(
            Enum.Parse<ExportFormat>(values.GetValueOrDefault("format", "md"), true),
            Enum.Parse<ExportLayout>(values.GetValueOrDefault("layout", "month"), true),
            bool.Parse(values.GetValueOrDefault("includeArchived", "false")),
            bool.Parse(values.GetValueOrDefault("includeAttachments", "true")),
            Date("from"), Date("to"),
            values.TryGetValue("timeZone", out var zone) ? TimeZoneInfo.FindSystemTimeZoneById(zone) : TimeZoneInfo.Utc);
    }

    private static List<(string, string)> Entries(Stream zip)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.Select(entry =>
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var extension = ExportNaming.Extension(entry.FullName);
            var text = extension is ".md" or ".json" || (extension == ".txt" && !entry.FullName.StartsWith("attachments/", StringComparison.Ordinal))
                ? Encoding.UTF8.GetString(bytes)
                : "base64:" + Convert.ToBase64String(bytes);
            return (entry.FullName, entry.FullName == "manifest.json" ? ExportTime().Replace(text, "\"exportedAt\": \"EXPORTED_AT\"") : text);
        }).ToList();
    }

    private static byte[] Archive(JsonElement entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries.EnumerateObject())
            {
                var text = entry.Value.GetString()!.Replace("EXPORTED_AT", "2025-09-01T10:00:00+02:00", StringComparison.Ordinal);
                var bytes = text.StartsWith("base64:", StringComparison.Ordinal) ? Convert.FromBase64String(text[7..]) : Encoding.UTF8.GetBytes(text);
                using var stream = zip.CreateEntry(entry.Name).Open();
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    private static DateTime Utc(JsonElement value) =>
        DateTime.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    [GeneratedRegex("\"exportedAt\": \"[^\"]+\"")]
    private static partial Regex ExportTime();

    /// <summary>Core's services over a throwaway encrypted database.</summary>
    private sealed class Throwaway : IDisposable, IAppDirectories
    {
        private Throwaway(string root) => Root = root;

        public string Root { get; }

        public string DataDirectory => Path.Combine(Root, "data");

        public string CacheDirectory => Path.Combine(Root, "cache");

        public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));

        public StorageContext Storage { get; } = new();

        public AttachmentStore Store { get; private set; } = null!;

        public ProfileService Profile { get; private set; } = null!;

        public NoteExporter Exporter { get; private set; } = null!;

        public RestoreReader Reader { get; private set; } = null!;

        public RestoreRunner Runner { get; private set; } = null!;

        public static async Task<Throwaway> CreateAsync(string scratch)
        {
            var app = new Throwaway(Path.Combine(scratch, Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(app.DataDirectory);
            var key = RandomNumberGenerator.GetBytes(32);
            var keys = new KeyMaterial(key);
            var database = new Database(Path.Combine(app.DataDirectory, "falcon.db"), keys.DatabaseKey, pooling: false);
            await using (var connection = await database.OpenAsync())
            {
                Migrations.Apply(connection);
            }

            app.Storage.Open(database, keys, Guid.NewGuid());
            var feed = new ChangeFeed();
            app.Store = new AttachmentStore(app);
            var attachments = new AttachmentService(app.Storage, app.Store, app.Clock);
            app.Profile = new ProfileService(app.Storage, feed, app.Clock);
            app.Exporter = new NoteExporter(app.Storage, attachments, app.Profile, app.Clock, NullLogger<NoteExporter>.Instance);
            app.Reader = new RestoreReader(app, app.Clock);
            app.Runner = new RestoreRunner(app.Storage, attachments, feed, app.Clock);
            return app;
        }

        public void Dispose() => Storage.Close();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
