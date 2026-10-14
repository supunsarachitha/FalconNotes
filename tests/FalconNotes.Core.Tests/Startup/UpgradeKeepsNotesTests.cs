using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Startup;
using FalconNotes.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Tests.Startup;

/// <summary>
/// An update must never cost a note (docs/11, Updates keep every note): a database as every earlier version of the
/// app left it, holding one of everything, is started by this version and compared row by row and file by file.
/// </summary>
public class UpgradeKeepsNotesTests
{
    public static TheoryData<int> EarlierVersions => [.. Enumerable.Range(1, Migrations.Latest - 1)];

    [Fact]
    public void Every_migration_has_its_undo_so_every_update_is_tested()
    {
        // Added a migration? Add what undoes it to SchemaHistory.Undo: the test below then covers the update to it.
        Assert.Equal(Migrations.Latest - 1, SchemaHistory.Undo.Length);
    }

    [Theory]
    [MemberData(nameof(EarlierVersions))]
    public async Task Updating_from_an_earlier_version_keeps_every_note_label_and_file(int version)
    {
        using var app = await TestApp.StartAsync();
        var label = await app.Labels.CreateAsync("Work", LabelColor.Blue);
        var photo = await app.AddFileAsync("photo.png");
        var noteWithFile = await app.Notes.CreateAsync("# Trip\n\nPack the bags #travel/packing", attachmentIds: [photo.Id]);
        await app.Notes.PatchAsync(noteWithFile.Id, new NotePatch(IsPinned: true, LabelIds: [label.Id]));
        await app.Notes.CreateAsync("# Groceries\n\n- [ ] oats\n- [x] tea", NoteKind.Todo);
        await app.Notes.CreateAsync("call back at five", NoteKind.Quick);
        await app.Notes.CreateAsync("Stretch\n2026-09-27\n2026-09-28", NoteKind.Habit);
        await app.Notes.CreateAsync("# Monday\n\nfirst words", dailyDate: new DateOnly(2026, 9, 28));
        var archived = await app.Notes.CreateAsync("kept for later");
        await app.Notes.PatchAsync(archived.Id, new NotePatch(IsArchived: true));
        var trashed = await app.Notes.CreateAsync("thrown away, still restorable");
        await app.Notes.PatchAsync(trashed.Id, new NotePatch(IsTrashed: true));

        Dictionary<string, string[]> tables;
        List<string> before;
        await using (var connection = await app.Storage.Database.OpenAsync(TestContext.Current.CancellationToken))
        {
            SchemaHistory.RewindTo(connection, version);
            tables = SchemaHistory.Tables(connection);
            before = SchemaHistory.Rows(connection, tables);
        }

        var files = app.Store.EnumerateStorageKeys().Order(StringComparer.Ordinal).ToList();
        app.Storage.Close();
        SqliteConnection.ClearAllPools();

        var (outcome, _) = await app.Startup.RunAsync(); // the update's first start

        Assert.Equal(StartupOutcome.Ready, outcome);
        await using (var connection = await app.Storage.Database.OpenAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(Migrations.Latest, Migrations.ReadVersion(connection));
            Assert.Equal(before, SchemaHistory.Rows(connection, tables));
        }

        Assert.Equal(7, before.Count(row => row.StartsWith("Notes: ", StringComparison.Ordinal)));
        Assert.Equal(files, app.Store.EnumerateStorageKeys().Order(StringComparer.Ordinal));

        // And as the screens read them: the text, the state, the tags, the label and the file.
        var read = (await app.Notes.GetAsync(noteWithFile.Id))!;
        Assert.Equal("# Trip\n\nPack the bags #travel/packing", read.Content);
        Assert.True(read.IsPinned);
        Assert.False(read.IsLocked);
        Assert.Equal(NoteMood.None, read.Mood);
        Assert.Equal(["travel/packing"], read.Tags);
        Assert.Equal([label.Id], read.LabelIds);
        await using var file = (await app.Attachments.OpenAsync(read.Attachments.Single().Id))!.Content;
        Assert.Equal(100, file.Length);
        Assert.True((await app.Notes.GetAsync(archived.Id))!.IsArchived);
        Assert.True((await app.Notes.GetAsync(trashed.Id))!.IsTrashed);
        Assert.Single(await app.Notes.ListHabitsAsync());
        Assert.NotNull(await app.Notes.GetDailyAsync(new DateOnly(2026, 9, 28)));
        Assert.Single(Directory.GetFiles(Path.Combine(app.Directories.DataDirectory, "backups"))); // the copy kept before the update
    }
}
