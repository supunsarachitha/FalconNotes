using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Backup.Restore;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Backup;

// Ported from web/import/import.test.ts, plus running the restore into a database (docs/11, Backup conformance).
public class RestoreConformanceTests
{
    /// <summary>A ZIP of a vector export's entries, with a real export time, as the web app's test builds it.</summary>
    internal static byte[] Archive(JsonElement entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries.EnumerateObject())
            {
                var text = entry.Value.GetString()!;
                if (entry.Name == "manifest.json")
                {
                    text = text.Replace("EXPORTED_AT", "2025-09-01T10:00:00+02:00", StringComparison.Ordinal);
                }

                var bytes = text.StartsWith("base64:", StringComparison.Ordinal) ? Convert.FromBase64String(text[7..]) : Encoding.UTF8.GetBytes(text);
                using var stream = zip.CreateEntry(entry.Name).Open();
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    internal static RestoreSource Source(string name, byte[] bytes, DateTime? modified = null) =>
        new(name, () => Task.FromResult<Stream>(new MemoryStream(bytes)), modified);

    internal static RestoreReader NewReader(TestApp app) => new(app.Directories, app.Clock);

    internal static RestoreRunner NewRunner(TestApp app) => new(app.Storage, app.Attachments, app.Feed, app.Clock);

    private static DateTime Seconds(string iso)
    {
        var time = DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        return new DateTime(time.Ticks - (time.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }

    [Theory]
    [MemberData(nameof(ExportVectors.Indexes), MemberType = typeof(ExportVectors))]
    public async Task Every_vector_export_reads_back_into_the_original_notes(int index)
    {
        using var app = await TestApp.StartAsync();
        var vector = ExportVectors.Export(index);
        var query = vector.GetProperty("query").GetString()!;
        var format = ExportVectors.Options(query).Format;
        var withFiles = !query.Contains("includeAttachments=false", StringComparison.Ordinal);
        var entries = vector.GetProperty("entries");
        using var manifest = JsonDocument.Parse(entries.GetProperty("manifest.json").GetString()!);
        var notes = ExportVectors.Notes().ToDictionary(n => n.GetProperty("id").GetGuid());

        using var plan = await NewReader(app).ReadAsync([Source("maple-notes.zip", Archive(entries))]);

        Assert.Empty(plan.Problems);
        Assert.Equal(manifest.RootElement.GetProperty("notes").EnumerateArray().Select(n => n.GetProperty("id").GetGuid()), plan.Items.Select(i => i.Id!.Value));
        foreach (var item in plan.Items)
        {
            var note = notes[item.Id!.Value];
            var content = note.GetProperty("content").GetString()!;
            var created = note.GetProperty("createdAtUtc").GetString()!;
            var updated = note.GetProperty("updatedAtUtc").GetString()!;
            var edited = Seconds(updated) - Seconds(created) > TimeSpan.FromMinutes(1);
            Assert.Equal(format == ExportFormat.Json ? content : content.TrimEnd(), item.Content);
            Assert.Equal(Seconds(created), item.CreatedAt.UtcDateTime);
            Assert.Equal(format == ExportFormat.Txt && !edited ? Seconds(created) : Seconds(updated), item.UpdatedAt.UtcDateTime);
            Assert.Equal(note.GetProperty("isPinned").GetBoolean(), item.Pinned);
            Assert.Equal(note.GetProperty("isArchived").GetBoolean(), item.Archived);
            Assert.Equal(Enum.Parse<NoteKind>(note.GetProperty("kind").GetString()!), item.Kind);
            var daily = note.GetProperty("dailyDate");
            Assert.Equal(daily.ValueKind == JsonValueKind.Null ? null : daily.GetString(), item.DailyDate);
            Assert.Empty(item.Missing);

            var expected = withFiles ? note.GetProperty("attachments").EnumerateArray().ToList() : [];
            Assert.Equal(
                expected.Select(a => format == ExportFormat.Txt ? ExportNaming.SafeFileName(a.GetProperty("fileName").GetString()!) : a.GetProperty("fileName").GetString()),
                item.Attachments.Select(a => a.Name));
            if (format == ExportFormat.Json)
            {
                Assert.Equal(expected.Select(a => a.GetProperty("contentType").GetString()), item.Attachments.Select(a => a.ContentType));
            }

            foreach (var (attachment, want) in item.Attachments.Zip(expected))
            {
                using var stream = attachment.Open();
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                Assert.Equal(ExportVectors.File(want.GetProperty("id").GetGuid()), bytes.ToArray());
            }
        }
    }

    [Theory]
    [MemberData(nameof(ExportVectors.Indexes), MemberType = typeof(ExportVectors))]
    public async Task Every_vector_export_restores_into_an_empty_database_and_restoring_twice_changes_nothing(int index)
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        app.Clock.Step = TimeSpan.FromTicks(1); // a note's files are stored one after another and are listed oldest first
        var entries = ExportVectors.Export(index).GetProperty("entries");
        var archive = Archive(entries);
        using var plan = await NewReader(app).ReadAsync([Source("maple-notes.zip", archive)]);

        var result = await NewRunner(app).RunAsync(plan.Items);

        Assert.Empty(result.Failed);
        Assert.Equal(plan.Items.Count, result.Restored);
        Assert.Equal(plan.FileCount, result.Files);
        foreach (var item in plan.Items)
        {
            var note = (await app.Notes.GetAsync(item.Id!.Value))!;
            Assert.Equal(item.Content, note.Content);
            Assert.Equal(item.CreatedAt.UtcDateTime, note.CreatedAtUtc);
            Assert.Equal(item.UpdatedAt.UtcDateTime, note.UpdatedAtUtc);
            Assert.Equal((item.Pinned, item.Archived, item.Kind, item.DailyDate), (note.IsPinned, note.IsArchived, note.Kind, note.DailyDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            Assert.Equal(item.Attachments.Select(a => a.Name), note.Attachments.Select(a => a.FileName));
            Assert.Equal(TagParser.Extract(item.Content).Order(StringComparer.Ordinal), note.Tags);
        }

        using var again = await NewReader(app).ReadAsync([Source("maple-notes.zip", archive)]);
        var second = await NewRunner(app).RunAsync(again.Items);
        Assert.Equal((0, plan.Items.Count, 0), (second.Restored, second.Skipped, second.Files));
    }

    [Fact]
    public async Task Reads_single_note_files_and_archives_without_a_manifest()
    {
        using var app = await TestApp.StartAsync();
        var folder = Zip(("notes/a.md", "A"), ("notes/b.txt", "B"), ("notes/photo.png", "\u0001"), ("__MACOSX/notes/._a.md", "x"));

        using var plan = await NewReader(app).ReadAsync(
        [
            Source("ideas.md", Encoding.UTF8.GetBytes("﻿# Plain notes\r\n\r\nFrom another app"), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            Source("todo.txt", Encoding.UTF8.GetBytes("just text"), new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
            Source("folder.zip", folder),
            Source("data.json", Encoding.UTF8.GetBytes("{}")),
            Source("movie.mp4", [1]),
            Source("broken.zip", Encoding.UTF8.GetBytes("not a zip")),
        ]);

        Assert.Equal(
            [("ideas.md", "# Plain notes\n\nFrom another app", (Guid?)null, NoteKind.Note), ("todo.txt", "just text", null, NoteKind.Note),
             ("notes/a.md", "A", null, NoteKind.Note), ("notes/b.txt", "B", null, NoteKind.Note)],
            plan.Items.Select(i => (i.Source, i.Content, i.Id, i.Kind)));
        Assert.Equal(new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc), plan.Items[0].CreatedAt.UtcDateTime);
        Assert.Equal(
            ["data.json: This JSON file is not a Falcon Notes or Maple Notes note.", "movie.mp4: choose .zip, .md, .txt or .json files.",
             "broken.zip: This is not a ZIP archive."],
            plan.Problems);
    }

    [Fact]
    public async Task Problems_in_an_archive_are_reported_and_the_rest_is_read()
    {
        using var app = await TestApp.StartAsync();
        var manifest = """{"application":"Maple Notes","notes":[{"id":"0192f3a2-0000-7000-8000-000000000001","path":"gone.md"},{"path":"a.md"}]}""";
        var zip = Zip(("manifest.json", manifest), ("a.md", "---\nid: x\ncreated: 2025-01-01T10:00:00+01:00\nattachments:\n  - \"attachments/12345678_lost.png\"\n---\n\nhello\n\n## Attachments\n\n- ![lost.png](attachments/12345678_lost.png)\n"));
        using var plan = await NewReader(app).ReadAsync([Source("backup.zip", zip), Source("bad.zip", Zip(("manifest.json", "{not json"), ("n.md", "x")))]);

        // Without a usable manifest every note file is read, the manifest included, as the web app does.
        Assert.Equal(
            ["backup.zip: gone.md is listed but missing.", "bad.zip: manifest.json could not be read; its notes are read without it.",
             "bad.zip: manifest.json: This JSON file is not a Falcon Notes or Maple Notes note."],
            plan.Problems);
        Assert.Equal(["lost.png"], plan.Items[0].Missing);
        Assert.Equal("hello", plan.Items[0].Content);
        Assert.Equal(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc), plan.Items[0].CreatedAt.UtcDateTime);
        Assert.Equal("x", plan.Items[1].Content); // read without the manifest

        var result = await NewRunner(app).RunAsync(plan.Items);
        Assert.Equal([("a.md", "Restored without lost.png, which the archive does not contain.")], result.Failed);
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return buffer.ToArray();
    }
}
