using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Backup;

/// <summary>
/// The shared export vectors (fixtures/export-vectors.json; docs/05, Proof): the dataset the server exported and its
/// 13 archives, entry by entry.
/// </summary>
public static partial class ExportVectors
{
    private static readonly Lazy<JsonDocument> Document = new(() =>
        JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "export-vectors.json"))));

    public static JsonElement Root => Document.Value.RootElement;

    /// <summary>The 13 exports, by index, for theories.</summary>
    public static TheoryData<int> Indexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Root.GetProperty("exports").GetArrayLength(); i++)
        {
            data.Add(i);
        }

        return data;
    }

    public static JsonElement Export(int index) => Root.GetProperty("exports")[index];

    public static IEnumerable<JsonElement> Notes() =>
        Root.GetProperty("active").GetProperty("items").EnumerateArray().Concat(Root.GetProperty("archived").GetProperty("items").EnumerateArray());

    public static byte[] File(Guid id) => Convert.FromBase64String(Root.GetProperty("files").GetProperty(id.ToString()).GetString()!);

    /// <summary>Writes the vector dataset into an empty database: IDs, text, kinds, states, times and files exactly.</summary>
    public static async Task SeedAsync(TestApp app)
    {
        await app.Profile.CreateAsync(Root.GetProperty("account").GetString());
        foreach (var note in Notes())
        {
            var id = note.GetProperty("id").GetGuid();
            var content = note.GetProperty("content").GetString()!;
            var created = Utc(note.GetProperty("createdAtUtc"));
            Assert.Equal(note.GetProperty("tags").EnumerateArray().Select(t => t.GetString()), TagParser.Extract(content).Order(StringComparer.Ordinal));
            var files = new List<(Attachment Attachment, string Key)>();
            foreach (var file in note.GetProperty("attachments").EnumerateArray())
            {
                var attachment = new Attachment(file.GetProperty("id").GetGuid(), id, file.GetProperty("fileName").GetString()!,
                    file.GetProperty("contentType").GetString()!, file.GetProperty("sizeBytes").GetInt64(), Utc(file.GetProperty("createdAtUtc")), 0);
                var key = AttachmentStore.CreateStorageKey(attachment.Id);
                using var attachmentKey = app.Storage.Keys.CreateAttachmentKey();
                await app.Store.WriteAsync(key, (stream, ct) =>
                    AttachmentCipher.EncryptAsync(new MemoryStream(File(attachment.Id)), stream, attachmentKey, app.Storage.InstallationId, attachment.Id, ct));
                files.Add((attachment, key));
            }

            await app.Storage.Database.InTransactionAsync((connection, transaction) =>
            {
                var daily = note.GetProperty("dailyDate");
                using var insert = Sql.Command(connection, """
                    INSERT INTO Notes (Id, Kind, DailyDate, IsPinned, ArchivedAt, CreatedAt, UpdatedAt, ContentBytes)
                    VALUES ($id, $kind, $daily, $pinned, $archived, $created, $updated, 0);
                    INSERT INTO NoteBodies (NoteId, Content) VALUES ($id, $content);
                    """, transaction);
                insert.With("$id", Sql.Id(id)).With("$kind", (int)Enum.Parse<NoteKind>(note.GetProperty("kind").GetString()!))
                    .With("$daily", daily.ValueKind == JsonValueKind.Null ? null : daily.GetString())
                    .With("$pinned", note.GetProperty("isPinned").GetBoolean() ? 1 : 0)
                    .With("$archived", note.GetProperty("isArchived").GetBoolean() ? Sql.Time(created) : null)
                    .With("$created", Sql.Time(created)).With("$updated", Sql.Time(Utc(note.GetProperty("updatedAtUtc"))))
                    .With("$content", content).ExecuteNonQuery();
                NoteRepository.SetTags(connection, transaction, id, TagParser.Extract(content));
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

    /// <summary>The options an export's query string asks for (defaults as the server's ExportRequest).</summary>
    public static ExportOptions Options(string query)
    {
        var values = query.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);
        DateOnly? Date(string key) => values.TryGetValue(key, out var v) ? DateOnly.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        return new ExportOptions(
            Enum.Parse<ExportFormat>(Get("format", "md"), ignoreCase: true),
            Enum.Parse<ExportLayout>(Get("layout", "month"), ignoreCase: true),
            bool.Parse(Get("includeArchived", "false")),
            bool.Parse(Get("includeAttachments", "true")),
            Date("from"),
            Date("to"),
            values.TryGetValue("timeZone", out var zone) ? TimeZoneInfo.FindSystemTimeZoneById(zone) : TimeZoneInfo.Utc);
    }

    /// <summary>
    /// An archive's entries as the vectors record them: in order, text for notes and the manifest (with the export time
    /// replaced), <c>base64:</c> for everything else.
    /// </summary>
    public static List<(string Name, string Content)> Entries(Stream zip)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        var entries = new List<(string, string)>();
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var extension = ExportNaming.Extension(entry.FullName);
            var text = extension is ".md" or ".json" || (extension == ".txt" && !entry.FullName.StartsWith("attachments/", StringComparison.Ordinal))
                ? Encoding.UTF8.GetString(bytes)
                : "base64:" + Convert.ToBase64String(bytes);
            entries.Add((entry.FullName, entry.FullName == "manifest.json" ? ExportTime().Replace(text, "\"exportedAt\": \"EXPORTED_AT\"") : text));
        }

        return entries;
    }

    private static DateTime Utc(JsonElement value) =>
        DateTime.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    [GeneratedRegex("\"exportedAt\": \"[^\"]+\"")]
    private static partial Regex ExportTime();
}
