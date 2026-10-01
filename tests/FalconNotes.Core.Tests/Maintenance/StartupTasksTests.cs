using FalconNotes.Core.Maintenance;
using FalconNotes.Core.Notes;
using Microsoft.Extensions.Logging.Abstractions;

namespace FalconNotes.Core.Tests.Maintenance;

public class StartupTasksTests
{
    [Fact]
    public async Task A_pass_purges_the_trash_cleans_attachments_and_deletes_temporary_copies()
    {
        using var app = await TestApp.StartAsync();
        var trashed = await app.Notes.CreateAsync("old");
        await app.Notes.PatchAsync(trashed.Id, new NotePatch(IsTrashed: true));
        app.Clock.Advance(TimeSpan.FromDays(31));
        foreach (var name in StartupTasks.TemporaryFolders)
        {
            var folder = Path.Combine(app.Directories.CacheDirectory, name, "abc");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "plain.txt"), "secret");
        }

        var tasks = new StartupTasks(app.Notes, app.Cleanup, app.Directories, NullLogger<StartupTasks>.Instance);
        var result = await tasks.RunAsync(includeTemporaryFolders: true);

        Assert.Equal(1, result!.Trash.Notes);
        Assert.Equal(3, result.TemporaryFolders);
        Assert.Empty(Directory.GetDirectories(app.Directories.CacheDirectory));
        Assert.Null(await app.Notes.GetAsync(trashed.Id));
    }

    [Fact]
    public async Task Hourly_passes_leave_temporary_copies_that_may_be_in_use()
    {
        using var app = await TestApp.StartAsync();
        var folder = Path.Combine(app.Directories.CacheDirectory, "open", "abc");
        Directory.CreateDirectory(folder);

        var result = await new StartupTasks(app.Notes, app.Cleanup, app.Directories, NullLogger<StartupTasks>.Instance)
            .RunAsync(includeTemporaryFolders: false);

        Assert.Equal(0, result!.TemporaryFolders);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task A_failing_pass_is_reported_not_thrown()
    {
        using var app = await TestApp.StartAsync();
        app.Storage.Close();

        Assert.Null(await new StartupTasks(app.Notes, app.Cleanup, app.Directories, NullLogger<StartupTasks>.Instance).RunAsync(true));
    }
}
