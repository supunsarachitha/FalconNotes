using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;

namespace FalconNotes.Core.Tests.Notes;

// The rules of the server's Auth/DeleteContentTests.cs and Storage/StorageTests.cs.
public class DeleteAllAndStorageTests
{
    [Fact]
    public async Task Everything_written_goes_and_the_profile_and_preferences_stay()
    {
        using var app = await TestApp.StartAsync();
        await app.Profile.CreateAsync("Sam");
        await app.Preferences.SaveAsync(new Preferences { Labels = true });
        var label = await app.Labels.CreateAsync("Work");
        var file = await app.AddFileAsync();
        var note = await app.Notes.CreateAsync("#tag", attachmentIds: [file.Id]);
        await app.Notes.PatchAsync(note.Id, new NotePatch(LabelIds: [label.Id]));
        await app.Notes.CreateAsync("# List", NoteKind.Todo);
        var trashed = await app.Notes.CreateAsync("trashed");
        await app.Notes.PatchAsync(trashed.Id, new NotePatch(IsTrashed: true));
        await app.AddFileAsync("unattached.png");

        Assert.Equal(new DeletedCount(3, 2), await app.Notes.DeleteAllAsync());

        Assert.Empty((await app.Notes.ListAsync(new NoteQuery(NoteState.Trash, [.. NoteKinds.All]))).Items);
        Assert.Empty((await app.Notes.ListAsync(new NoteQuery(NoteState.Active, [.. NoteKinds.All]))).Items);
        Assert.Empty(await app.Labels.ListAsync([NoteKind.Note]));
        Assert.Empty(await app.Notes.TagCountsAsync([NoteKind.Note]));
        Assert.Empty(app.Store.EnumerateStorageKeys());
        Assert.Equal("Sam", (await app.Profile.GetAsync())!.DisplayName);
        Assert.True((await app.Preferences.GetAsync()).Labels);
    }

    [Fact]
    public async Task Storage_use_counts_every_note_and_file_including_the_archive_and_trash()
    {
        using var app = await TestApp.StartAsync();
        await app.Notes.CreateAsync("héllo"); // 6 bytes in UTF-8
        var archived = await app.Notes.CreateAsync("abc");
        await app.Notes.PatchAsync(archived.Id, new NotePatch(IsArchived: true));
        var trashed = await app.Notes.CreateAsync("de");
        await app.Notes.PatchAsync(trashed.Id, new NotePatch(IsTrashed: true));
        await app.AddFileAsync(size: 1000);
        await app.AddFileAsync(size: 24);

        var usage = await app.Notes.StorageUsageAsync();

        Assert.Equal(new StorageUsage(11, 3, 1024, 2), usage);
        Assert.Equal(1035, usage.TotalBytes);
    }
}
