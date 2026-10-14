using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FalconNotes.Core.Backup.Restore;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;

namespace FalconNotes.Core.Tests.Backup;

/// <summary>
/// New tests (docs/11): a note's mood in backups (docs/05, Moods in backups). The mood is this app's own; the shared
/// vectors hold no note with one, so they still prove that every other note is written as the web app writes it.
/// </summary>
public class MoodBackupTests
{
    private static async Task<Dictionary<string, string>> ExportAsync(TestApp app, string query)
    {
        using var zip = new MemoryStream();
        await ExportConformanceTests.NewExporter(app).WriteAsync(ExportVectors.Options(query), zip);
        zip.Position = 0;
        return ExportVectors.Entries(zip).ToDictionary(e => e.Name, e => e.Content);
    }

    private static async Task<byte[]> ExportZipAsync(TestApp app, string query)
    {
        using var zip = new MemoryStream();
        await ExportConformanceTests.NewExporter(app).WriteAsync(ExportVectors.Options(query), zip);
        return zip.ToArray();
    }

    [Theory]
    [InlineData("md", "mood: great\n")]
    [InlineData("txt", "Mood: great\n")]
    [InlineData("json", "  \"mood\": \"great\",\n")]
    public async Task A_mood_is_one_more_line_in_the_note_file_and_nothing_else_changes(string format, string line)
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Now = new DateTimeOffset(2026, 10, 13, 7, 0, 0, TimeSpan.Zero);
        var note = await app.Notes.CreateAsync("Passed the exam #school");
        await app.Notes.PatchAsync(note.Id, new NotePatch(IsPinned: true));
        var name = $"2026-10-13_0700_passed-the-exam.{format}";
        var without = await ExportAsync(app, $"format={format}&layout=flat");

        await app.Notes.PatchAsync(note.Id, new NotePatch(Mood: NoteMood.Great));
        var with = await ExportAsync(app, $"format={format}&layout=flat");

        Assert.DoesNotContain("mood", without[name], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(line, with[name], StringComparison.Ordinal);
        Assert.Equal(without[name], with[name].Replace(line, "", StringComparison.Ordinal));

        // The manifest says nothing of moods: the note file is where one is read from.
        Assert.Equal(without["manifest.json"], with["manifest.json"]);
    }

    [Fact]
    public async Task The_mood_line_stands_after_the_state_and_before_the_files()
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Now = new DateTimeOffset(2026, 10, 13, 7, 0, 0, TimeSpan.Zero);
        var photo = await app.AddFileAsync("rain.png");
        var note = await app.Notes.CreateAsync("Rained all day", attachmentIds: [photo.Id], mood: NoteMood.Bad);
        await app.Notes.PatchAsync(note.Id, new NotePatch(IsPinned: true));

        var markdown = (await ExportAsync(app, "format=md&layout=flat"))["2026-10-13_0700_rained-all-day.md"];
        var text = (await ExportAsync(app, "format=txt&layout=flat"))["2026-10-13_0700_rained-all-day.txt"];
        var json = (await ExportAsync(app, "format=json&layout=flat"))["2026-10-13_0700_rained-all-day.json"];

        Assert.Contains("\npinned: true\narchived: false\nmood: bad\nattachments:\n", markdown, StringComparison.Ordinal);
        Assert.Contains("\nState: pinned\nMood: bad\nAttachment: attachments/", text, StringComparison.Ordinal);
        Assert.Contains("  \"archived\": false,\n  \"mood\": \"bad\",\n  \"content\": \"Rained all day\",\n", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("bad", document.RootElement.GetProperty("mood").GetString());
    }

    [Theory]
    [InlineData("md")]
    [InlineData("txt")]
    [InlineData("json")]
    public async Task A_backup_restored_into_an_empty_app_brings_each_mood_back(string format)
    {
        byte[] archive;
        using (var source = await TestApp.StartAsync())
        {
            await source.Notes.CreateAsync("a note with no mood");
            source.Clock.Advance(TimeSpan.FromMinutes(1));
            await source.Notes.CreateAsync("the best day", mood: NoteMood.Great);
            source.Clock.Advance(TimeSpan.FromMinutes(1));
            await source.Notes.CreateAsync("a quick grumble", NoteKind.Quick, mood: NoteMood.Awful);
            source.Clock.Advance(TimeSpan.FromMinutes(1));
            await source.Notes.CreateAsync("# Monday\n\nso-so", dailyDate: new DateOnly(2026, 9, 28), mood: NoteMood.Okay);
            archive = await ExportZipAsync(source, $"format={format}");
        }

        using var app = await TestApp.StartAsync();
        using var plan = await RestoreConformanceTests.NewReader(app).ReadAsync([RestoreConformanceTests.Source("falcon-notes.zip", archive)]);
        var result = await RestoreConformanceTests.NewRunner(app).RunAsync(plan.Items);

        Assert.Empty(plan.Problems);
        Assert.Equal(4, result.Restored);
        var notes = (await app.Notes.ListAsync(new NoteQuery(NoteState.Active, [NoteKind.Note, NoteKind.Quick]))).Items;
        Assert.Equal(
            [("# Monday\n\nso-so", NoteMood.Okay), ("a quick grumble", NoteMood.Awful), ("the best day", NoteMood.Great), ("a note with no mood", NoteMood.None)],
            notes.Select(n => (n.Content, n.Mood)));
    }

    [Fact]
    public async Task A_mood_is_read_in_any_case_and_one_the_app_does_not_know_is_no_mood()
    {
        using var app = await TestApp.StartAsync();

        using var plan = await RestoreConformanceTests.NewReader(app).ReadAsync(
        [
            Source("a.md", "---\ncreated: 2025-01-01T10:00:00+01:00\nmood: Good\n---\n\nA\n"),
            Source("b.txt", "Created: 2025-01-01T10:00:00+01:00\nMood: AWFUL\n\nB\n"),
            Source("c.json", """{"content":"C","createdAt":"2025-01-01T10:00:00+01:00","mood":"okay"}"""),
            Source("d.md", "---\ncreated: 2025-01-01T10:00:00+01:00\nmood: ecstatic\n---\n\nD\n"),
            Source("e.json", """{"content":"E","createdAt":"2025-01-01T10:00:00+01:00","mood":5}"""),
            Source("f.md", "---\ncreated: 2025-01-01T10:00:00+01:00\n---\n\nmood: great\n"),
            Source("g.md", "mood: great"),
        ]);

        Assert.Empty(plan.Problems);
        Assert.Equal(
            [("a.md", NoteMood.Good), ("b.txt", NoteMood.Awful), ("c.json", NoteMood.Okay), ("d.md", NoteMood.None), ("e.json", NoteMood.None),
             ("f.md", NoteMood.None), ("g.md", NoteMood.None)],
            plan.Items.Select(i => (i.Source, i.Mood)));
    }

    [Fact]
    public async Task A_todo_list_or_a_habit_that_names_a_mood_arrives_without_it()
    {
        using var app = await TestApp.StartAsync();
        var zip = Zip(
            ("todo.md", "---\nkind: todo\ncreated: 2025-01-01T10:00:00+01:00\nmood: good\n---\n\n# Groceries\n\n- [ ] oats\n"),
            ("habit.md", "---\nkind: habit\ncreated: 2025-01-02T10:00:00+01:00\nmood: good\n---\n\n# Stretch\n"),
            ("quick.md", "---\nkind: quick\ncreated: 2025-01-03T10:00:00+01:00\nmood: good\n---\n\ncall back\n"));

        using var plan = await RestoreConformanceTests.NewReader(app).ReadAsync([RestoreConformanceTests.Source("backup.zip", zip)]);
        var result = await RestoreConformanceTests.NewRunner(app).RunAsync(plan.Items);

        Assert.Equal(3, result.Restored);
        Assert.Equal(NoteMood.None, (await app.Notes.ListAsync(new NoteQuery(NoteState.Feed, [NoteKind.Todo]))).Items.Single().Mood);
        Assert.Equal(NoteMood.None, (await app.Notes.ListHabitsAsync()).Single().Mood);
        Assert.Equal(NoteMood.Good, (await app.Notes.ListAsync(new NoteQuery(NoteState.Feed, [NoteKind.Quick]))).Items.Single().Mood);
    }

    private static RestoreSource Source(string name, string text) =>
        RestoreConformanceTests.Source(name, Encoding.UTF8.GetBytes(text), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));

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
