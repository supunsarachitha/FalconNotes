using System.Text;
using System.Text.Json;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;

namespace FalconNotes.Core.Tests.Backup;

/// <summary>
/// New tests (docs/11): stickers in backups (docs/05, Stickers in backups). A sticker is an ordinary picture among the
/// note's files, listed once more as a sticker; the shared vectors hold no sticker, so they still prove that every
/// other note is written as the web app writes it.
/// </summary>
public class StickerBackupTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static async Task<Dictionary<string, string>> ExportAsync(TestApp app, string query)
    {
        using var zip = new MemoryStream();
        await ExportConformanceTests.NewExporter(app).WriteAsync(ExportVectors.Options(query), zip);
        zip.Position = 0;
        return ExportVectors.Entries(zip).ToDictionary(e => e.Name, e => e.Content);
    }

    private static async Task<Attachment> AddStickerAsync(TestApp app, string name)
    {
        var added = await app.Attachments.AddAsync(new MemoryStream(Png), name, "image/png", sticker: true);
        app.Clock.Advance(TimeSpan.FromTicks(1));
        return added;
    }

    private static async Task<Note> NoteWithStickerAsync(TestApp app)
    {
        app.Clock.Now = new DateTimeOffset(2026, 10, 14, 7, 0, 0, TimeSpan.Zero);
        var photo = await app.AddFileAsync("beach.png");
        var sticker = await AddStickerAsync(app, "Waving cat.png");
        return await app.Notes.CreateAsync("At the beach", attachmentIds: [photo.Id, sticker.Id]);
    }

    [Fact]
    public async Task A_sticker_is_listed_once_more_after_the_files_in_each_format()
    {
        using var app = await TestApp.StartAsync();
        var note = await NoteWithStickerAsync(app);
        var sticker = note.Attachments.Single(a => a.IsSticker);
        var path = $"attachments/{sticker.Id.ToString("N")[^8..]}_Waving-cat.png";

        var markdown = (await ExportAsync(app, "format=md&layout=flat"))["2026-10-14_0700_at-the-beach.md"];
        var text = (await ExportAsync(app, "format=txt&layout=flat"))["2026-10-14_0700_at-the-beach.txt"];
        var json = (await ExportAsync(app, "format=json&layout=flat"))["2026-10-14_0700_at-the-beach.json"];

        Assert.Contains($"\"\nstickers:\n  - \"{path}\"\n---\n\nAt the beach\n", markdown, StringComparison.Ordinal);
        Assert.Contains("\n## Attachments\n\n- ![beach.png](", markdown, StringComparison.Ordinal);
        Assert.Contains($"\nAttachment: {path}\nSticker: {path}\n\nAt the beach\n", text, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(text, "Attachment: "));
        using var document = JsonDocument.Parse(json);
        Assert.Equal([path], document.RootElement.GetProperty("stickers").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(2, document.RootElement.GetProperty("attachments").GetArrayLength());
    }

    [Theory]
    [InlineData("md")]
    [InlineData("txt")]
    [InlineData("json")]
    public async Task A_note_without_stickers_is_written_as_before(string format)
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Now = new DateTimeOffset(2026, 10, 14, 7, 0, 0, TimeSpan.Zero);
        var photo = await app.AddFileAsync("beach.png");
        await app.Notes.CreateAsync("At the beach", attachmentIds: [photo.Id]);

        var file = (await ExportAsync(app, $"format={format}&layout=flat"))[$"2026-10-14_0700_at-the-beach.{format}"];

        Assert.DoesNotContain("sticker", file, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("md")]
    [InlineData("txt")]
    [InlineData("json")]
    public async Task A_backup_restored_into_an_empty_app_brings_each_sticker_back_as_a_sticker(string format)
    {
        byte[] archive;
        using (var source = await TestApp.StartAsync())
        {
            await NoteWithStickerAsync(source);
            using var zip = new MemoryStream();
            await ExportConformanceTests.NewExporter(source).WriteAsync(ExportVectors.Options($"format={format}"), zip);
            archive = zip.ToArray();
        }

        using var app = await TestApp.StartAsync();
        app.Clock.Now = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        using var plan = await RestoreConformanceTests.NewReader(app).ReadAsync([RestoreConformanceTests.Source("falcon-notes.zip", archive)]);
        var result = await RestoreConformanceTests.NewRunner(app).RunAsync(plan.Items);

        Assert.Empty(plan.Problems);
        Assert.Equal(1, result.Restored);
        var note = (await app.Notes.ListAsync(new NoteQuery(NoteState.Active, [NoteKind.Note]))).Items.Single();
        // Plain text keeps only the path, so the name is the one the export gave the file.
        var stickerName = format == "txt" ? "Waving-cat.png" : "Waving cat.png";
        Assert.Equal([("beach.png", false), (stickerName, true)], note.Attachments.Select(a => (a.FileName, a.IsSticker)));
    }

    [Fact]
    public async Task A_sticker_line_is_read_only_for_the_notes_own_pictures()
    {
        using var app = await TestApp.StartAsync();
        var zip = Zip(
            ("a.md", """
                ---
                created: 2025-01-01T10:00:00+01:00
                attachments:
                  - "attachments/1_cat.png"
                  - "attachments/2_notes.pdf"
                stickers:
                  - "attachments/2_notes.pdf"
                  - "attachments/3_not-attached.png"
                  - "attachments/1_cat.png"
                ---

                A

                """.Replace("\r\n", "\n", StringComparison.Ordinal)),
            ("attachments/1_cat.png", Encoding.Latin1.GetString(Png)),
            ("attachments/2_notes.pdf", "%PDF"),
            ("attachments/3_not-attached.png", Encoding.Latin1.GetString(Png)));

        using var plan = await RestoreConformanceTests.NewReader(app).ReadAsync([RestoreConformanceTests.Source("backup.zip", zip)]);
        await RestoreConformanceTests.NewRunner(app).RunAsync(plan.Items);

        var note = (await app.Notes.ListAsync(new NoteQuery(NoteState.Active, [NoteKind.Note]))).Items.Single();
        Assert.Equal([("1_cat.png", true), ("2_notes.pdf", false)], note.Attachments.Select(a => (a.FileName, a.IsSticker)).Order());
    }

    private static int CountOf(string text, string part) => (text.Length - text.Replace(part, "", StringComparison.Ordinal).Length) / part.Length;

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(name.EndsWith(".png", StringComparison.Ordinal) ? Encoding.Latin1.GetBytes(text) : Encoding.UTF8.GetBytes(text));
            }
        }

        return buffer.ToArray();
    }
}
