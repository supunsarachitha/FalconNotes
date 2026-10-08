using System.Text.Json;
using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace FalconNotes.Core.Tests.Backup;

// The backup contract (docs/05, docs/11 Backup conformance): every entry of the 13 shared export vectors, in order and
// byte for byte.
public class ExportConformanceTests
{
    [Theory]
    [MemberData(nameof(ExportVectors.Indexes), MemberType = typeof(ExportVectors))]
    public async Task Exports_match_the_shared_vectors_entry_for_entry(int index)
    {
        using var app = await TestApp.StartAsync();
        await ExportVectors.SeedAsync(app);
        var vector = ExportVectors.Export(index);
        var options = ExportVectors.Options(vector.GetProperty("query").GetString()!);

        using var zip = new MemoryStream();
        await NewExporter(app).WriteAsync(options, zip);
        zip.Position = 0;
        var entries = ExportVectors.Entries(zip);

        var expected = vector.GetProperty("entries").EnumerateObject().Select(e => (e.Name, e.Value.GetString()!)).ToList();
        Assert.Equal(expected.Select(e => e.Name), entries.Select(e => e.Name));
        foreach (var ((name, want), (_, got)) in expected.Zip(entries))
        {
            Assert.True(want == got, $"{name} differs.\n--- expected\n{want}\n--- actual\n{got}");
        }
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_left_out_and_listed_as_a_problem()
    {
        using var app = await TestApp.StartAsync();
        await ExportVectors.SeedAsync(app);
        var damaged = Guid.Parse("0192f3a3-0000-7000-8000-000000000013"); // con.txt
        var key = (await app.Storage.Database.ReadAsync(c =>
        {
            using var command = Sql.Command(c, "SELECT StorageKey FROM Attachments WHERE Id = $id").With("$id", Sql.Id(damaged));
            return (string)command.ExecuteScalar()!;
        }));
        app.Store.Delete(key);

        using var zip = new MemoryStream();
        var result = await NewExporter(app).WriteAsync(ExportVectors.Options("format=md&layout=flat&includeArchived=true"), zip);

        Assert.Equal(["attachments/00000013__con.txt: the stored file could not be read, so it was left out."], result.Problems);
        Assert.Equal(4, result.Files);
        zip.Position = 0;
        var manifest = ExportVectors.Entries(zip).Single(e => e.Name == "manifest.json").Content;
        Assert.Contains("\"attachmentCount\": 4", manifest, StringComparison.Ordinal);
        Assert.Contains("attachments/00000013__con.txt: the stored file could not be read, so it was left out.", manifest, StringComparison.Ordinal);
    }

    // Ported from the server's ExportTests.cs (Maple Notes 1.9.0).
    [Fact]
    public async Task Labels_are_exported_by_name_with_their_colours_in_the_manifest()
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Now = new DateTimeOffset(2026, 9, 3, 7, 0, 0, TimeSpan.Zero);
        var work = await app.Labels.CreateAsync("Work", LabelColor.Blue);
        var trip = await app.Labels.CreateAsync("Road trip, 2026", LabelColor.Teal);
        await app.Labels.CreateAsync("Unused", LabelColor.Grey);
        var note = await app.Notes.CreateAsync("Pack the car");
        await app.Notes.PatchAsync(note.Id, new NotePatch(LabelIds: [work.Id, trip.Id]));
        app.Clock.Now = new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.Zero);
        await app.Notes.CreateAsync("Late night thought");

        async Task<Dictionary<string, string>> ExportAsync(string query)
        {
            using var zip = new MemoryStream();
            await NewExporter(app).WriteAsync(ExportVectors.Options(query), zip);
            zip.Position = 0;
            return ExportVectors.Entries(zip).ToDictionary(e => e.Name, e => e.Content);
        }

        var markdown = await ExportAsync("format=md&layout=flat");
        var text = await ExportAsync("format=txt&layout=flat");
        var json = await ExportAsync("format=json&layout=flat");

        Assert.Contains("\nlabels: [\"Road trip, 2026\", \"Work\"]\n", markdown["2026-09-03_0700_pack-the-car.md"], StringComparison.Ordinal);
        Assert.Contains("\nlabels: []\n", markdown["2026-09-30_2330_late-night-thought.md"], StringComparison.Ordinal);
        Assert.Contains("\nLabels: [\"Road trip, 2026\", \"Work\"]\n", text["2026-09-03_0700_pack-the-car.txt"], StringComparison.Ordinal);
        Assert.DoesNotContain("Labels:", text["2026-09-30_2330_late-night-thought.txt"], StringComparison.Ordinal);
        using var exported = JsonDocument.Parse(json["2026-09-03_0700_pack-the-car.json"]);
        Assert.Equal(["Road trip, 2026", "Work"], exported.RootElement.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
        using var manifest = JsonDocument.Parse(markdown["manifest.json"]);
        Assert.Equal(3, manifest.RootElement.GetProperty("manifestVersion").GetInt32());
        Assert.Equal( // only the labels the exported notes carry
            [("Road trip, 2026", "Teal"), ("Work", "Blue")],
            manifest.RootElement.GetProperty("labels").EnumerateArray().Select(l => (l.GetProperty("name").GetString(), l.GetProperty("color").GetString())));
        var listed = manifest.RootElement.GetProperty("notes").EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == note.Id);
        Assert.Equal(["Road trip, 2026", "Work"], listed.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task The_start_date_must_not_be_after_the_end_date()
    {
        using var app = await TestApp.StartAsync();

        var error = await Assert.ThrowsAsync<Domain.UserFacingException>(() =>
            NewExporter(app).WriteAsync(ExportVectors.Options("from=2025-02-01&to=2025-01-01"), new MemoryStream()));

        Assert.Equal("The start date must not be after the end date.", error.Message);
    }

    [Fact]
    public async Task The_file_is_named_after_the_export_date_in_the_time_zone()
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Now = new DateTimeOffset(2026, 9, 28, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal("falcon-notes-2026-09-29.zip", NewExporter(app).FileName(ExportVectors.Options("timeZone=Europe%2FParis")));
    }

    [Theory]
    [InlineData("a.b/c", "")]
    [InlineData("photo.jpeg", ".jpeg")]
    [InlineData("dir/archive.tar.gz", ".gz")]
    [InlineData("trailing.", "")]
    [InlineData(@"odd\name.txt", ".txt")]
    [InlineData("c:drive", "")]
    public void Extensions_ignore_backslashes_and_colons(string path, string extension) =>
        Assert.Equal(extension, ExportNaming.Extension(path));

    internal static NoteExporter NewExporter(TestApp app) =>
        new(app.Storage, app.Attachments, app.Profile, app.Clock, NullLogger<NoteExporter>.Instance);
}
