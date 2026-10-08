using System.Text.Json;
using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Backup.Restore;
using FalconNotes.Core.Domain;

namespace FalconNotes.Core.Tests.Backup;

// docs/11, Backup conformance: the demo backups restore whole, and export → restore → export gives the same archive.
public partial class DemoBackupTests
{
    public static TheoryData<string> Backups => ["maple-notes-demo-json.zip", "maple-notes-demo-markdown.zip"];

    private static byte[] Demo(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Theory]
    [MemberData(nameof(Backups))]
    public async Task A_demo_backup_restores_35_notes_and_8_files_with_no_problems(string name)
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Now = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using var plan = await RestoreConformanceTests.NewReader(app).ReadAsync([RestoreConformanceTests.Source(name, Demo(name))]);
        var result = await RestoreConformanceTests.NewRunner(app).RunAsync(plan.Items);

        Assert.Empty(plan.Problems);
        Assert.Empty(result.Failed);
        Assert.Equal((35, 8), (result.Restored, result.Files));
        Assert.Equal(
            [(NoteKind.Note, 22), (NoteKind.Todo, 4), (NoteKind.Quick, 5), (NoteKind.Habit, 4)],
            plan.Items.GroupBy(i => i.Kind).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())));
        Assert.Equal("Restored 35 notes and 8 files.", RestoreRunner.Summary(result));
    }

    [Theory]
    [MemberData(nameof(Backups))]
    public async Task Export_after_restore_gives_back_the_same_archive(string name)
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Now = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        app.Clock.Step = TimeSpan.FromTicks(1); // a note's files are stored one after another and are listed oldest first
        var original = Demo(name);
        using var plan = await RestoreConformanceTests.NewReader(app).ReadAsync([RestoreConformanceTests.Source(name, original)]);
        await RestoreConformanceTests.NewRunner(app).RunAsync(plan.Items);
        var before = Normalise(ExportVectors.Entries(new MemoryStream(original)));
        using var originalManifest = JsonDocument.Parse(before.Single(e => e.Name == "manifest.json").Content);
        var options = originalManifest.RootElement.GetProperty("options");
        var query = $"format={options.GetProperty("format").GetString()}&layout={options.GetProperty("layout").GetString()}&includeArchived=true&timeZone=UTC";

        using var zip = new MemoryStream();
        await ExportConformanceTests.NewExporter(app).WriteAsync(ExportVectors.Options(query), zip);
        zip.Position = 0;
        var after = Normalise(ExportVectors.Entries(zip)).Select(e => (e.Name, WithoutVersion3(e.Content))).ToList();

        Assert.Equal(before.Select(e => e.Name), after.Select(e => e.Name));
        foreach (var ((entry, want), (_, got)) in before.Zip(after))
        {
            if (entry == "manifest.json")
            {
                // Only the export time and the account may differ.
                Assert.Equal(WithoutAccount(want), WithoutAccount(got));
            }
            else
            {
                Assert.True(want == got, $"{entry} differs.\n--- before\n{want}\n--- after\n{got}");
            }
        }
    }

    /// <summary>
    /// Exports name files after the last 8 hex digits of their ID, but do not record the IDs, so a restore gives files
    /// new ones (as the web app's does). Those 8 digits are the only other difference, in names, links and the manifest.
    /// </summary>
    private static List<(string Name, string Content)> Normalise(List<(string Name, string Content)> entries) =>
        entries.Select(e => (FileIds().Replace(e.Name, "attachments/xxxxxxxx_"), FileIds().Replace(e.Content, "attachments/xxxxxxxx_"))).ToList();

    [System.Text.RegularExpressions.GeneratedRegex("attachments/[0-9a-f]{8}_")]
    private static partial System.Text.RegularExpressions.Regex FileIds();

    /// <summary>
    /// The demo backups are manifest version 2, from before exports listed labels (the web app still ships them as
    /// they are). What is exported now is version 3: the same, plus the empty lists of labels.
    /// </summary>
    private static string WithoutVersion3(string content) =>
        System.Text.RegularExpressions.Regex.Replace(content, "^ *\"?labels\"?: \\[\\],?\n", "", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Replace("\"manifestVersion\": 3", "\"manifestVersion\": 2", StringComparison.Ordinal);

    private static string WithoutAccount(string manifest) =>
        System.Text.RegularExpressions.Regex.Replace(manifest, "\"account\": \"[^\"]*\"", "\"account\": \"\"");
}
