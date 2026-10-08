using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Maintenance;
using FalconNotes.Core.Startup;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace FalconNotes.Core.Tests.Backup;

/// <summary>Automatic backups to a folder the user chose (docs/05, Automatic backups). New: the web app has none.</summary>
public class AutoExportServiceTests
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    [Fact]
    public async Task Nothing_is_written_until_it_is_turned_on()
    {
        using var app = await TestApp.StartAsync();
        var folders = new FakeBackupFolders();
        var service = New(app, folders);

        Assert.Equal(AutoExportSettings.Default, await service.GetAsync());
        Assert.Null(await service.RunIfDueAsync());
        Assert.Null(await service.RunNowAsync());
        Assert.Empty(folders.Files);
    }

    [Fact]
    public async Task A_cancelled_folder_picker_leaves_it_off()
    {
        using var app = await TestApp.StartAsync();
        var service = New(app, new FakeBackupFolders { Next = null });

        Assert.Null(await service.ChooseFolderAsync());
        Assert.False((await service.GetAsync()).Enabled);
    }

    [Fact]
    public async Task The_first_backup_is_due_at_once_and_is_the_whole_ordinary_export()
    {
        using var app = await TestApp.StartAsync();
        await ExportVectors.SeedAsync(app);
        app.Clock.Zone = Paris;
        var folders = new FakeBackupFolders();
        var service = New(app, folders);

        var chosen = await service.ChooseFolderAsync();
        var outcome = await service.RunIfDueAsync();

        Assert.Equal((true, "tree:backups", "Backups", 7, 3), (chosen!.Enabled, chosen.Folder, chosen.FolderName, chosen.EveryDays, chosen.Keep));
        Assert.Null(outcome!.Error);
        Assert.Equal((13, 5), (outcome.Result!.Notes, outcome.Result.Files));
        Assert.Equal(["falcon-notes-auto-2026-09-28_1400.zip"], folders.Files.Keys); // 12:00 UTC, in Paris

        // Markdown, by month, with files and archived notes: the same archive as that export by hand (docs/05, Proof).
        var expected = ExportVectors.Root.GetProperty("exports").EnumerateArray()
            .Single(e => e.GetProperty("query").GetString() == "format=md&layout=month&includeArchived=true&timeZone=Europe%2FParis")
            .GetProperty("entries").EnumerateObject().Select(e => (e.Name, e.Value.GetString()!)).ToList();
        Assert.Equal(expected, ExportVectors.Entries(new MemoryStream(folders.Files.Values.Single())));

        var settings = await service.GetAsync();
        Assert.Equal((app.Clock.Now, null), (settings.LastRunAt, settings.LastError));
        Assert.Equal(app.Clock.Now.UtcDateTime, await LastExportAsync(app)); // "Last export: …" counts it too
        Assert.Empty(Directory.GetFiles(Path.Combine(app.Directories.CacheDirectory, "export")));
    }

    [Fact]
    public async Task The_next_backup_waits_for_the_chosen_number_of_days()
    {
        using var app = await TestApp.StartAsync();
        var folders = new FakeBackupFolders();
        var service = New(app, folders);
        await service.ChooseFolderAsync();
        await service.SetEveryDaysAsync(7);
        Assert.NotNull(await service.RunIfDueAsync());

        app.Clock.Advance(TimeSpan.FromDays(6));
        Assert.Null(await service.RunIfDueAsync());
        Assert.Single(folders.Files);

        app.Clock.Advance(TimeSpan.FromDays(1));
        Assert.NotNull(await service.RunIfDueAsync());
        Assert.Equal(2, folders.Files.Count);
        Assert.Null(await service.RunIfDueAsync()); // and not twice
    }

    [Theory]
    [InlineData("2026-09-28T07:00:00Z", 1, false)] // the same day in Paris
    [InlineData("2026-09-28T21:59:00Z", 1, false)] // 23:59 in Paris: still the same day
    [InlineData("2026-09-28T22:00:00Z", 1, true)] // midnight in Paris: the next day, though only 16 hours on
    [InlineData("2026-10-04T23:00:00Z", 7, true)] // a week of days later
    [InlineData("2026-10-04T21:00:00Z", 7, false)] // six days later
    [InlineData("2026-10-28T12:00:00Z", 30, true)]
    [InlineData("2026-10-27T12:00:00Z", 30, false)]
    [InlineData("2026-09-27T12:00:00Z", 30, true)] // the clock was set back: back up, do not wait for it to catch up
    public void A_backup_is_due_by_calendar_days_in_the_devices_time_zone(string now, int everyDays, bool due)
    {
        var last = DateTimeOffset.Parse("2026-09-28T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var settings = AutoExportSettings.Default with { Enabled = true, Folder = "tree:backups", EveryDays = everyDays, LastRunAt = last };

        Assert.Equal(due, AutoExportService.IsDue(settings, DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture), Paris));
    }

    [Fact]
    public void Nothing_is_due_while_it_is_off_or_has_no_folder()
    {
        var now = DateTimeOffset.UnixEpoch;

        Assert.False(AutoExportService.IsDue(AutoExportSettings.Default, now, Paris));
        Assert.False(AutoExportService.IsDue(AutoExportSettings.Default with { Enabled = true }, now, Paris));
        Assert.True(AutoExportService.IsDue(AutoExportSettings.Default with { Enabled = true, Folder = "tree:backups" }, now, Paris));
    }

    [Fact]
    public async Task Only_the_newest_automatic_backups_are_kept_and_no_other_file_is_touched()
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Zone = TimeZoneInfo.Utc;
        var folders = new FakeBackupFolders();
        string[] others =
        [
            "falcon-notes-2026-01-01.zip", // an export saved by hand
            "falcon-notes-auto-2026-01-01_0900 (1).zip", "falcon-notes-auto-2026-01-01_0900.zip.bak", "maple-notes-2026-01-01.zip", "holiday.jpg",
        ];
        string[] old =
        [
            "falcon-notes-auto-2026-09-01_0900.zip", "falcon-notes-auto-2026-09-08_0900.zip", "falcon-notes-auto-2026-09-15_0900.zip",
            "falcon-notes-auto-2026-09-22_0900.zip",
        ];
        foreach (var name in others.Concat(old))
        {
            folders.Files[name] = [1];
        }

        var service = New(app, folders);
        await service.ChooseFolderAsync();
        await service.SetKeepAsync(3);
        await service.RunNowAsync();

        Assert.Equal(
            ["falcon-notes-auto-2026-09-15_0900.zip", "falcon-notes-auto-2026-09-22_0900.zip", "falcon-notes-auto-2026-09-28_1200.zip"],
            folders.Backups);
        Assert.All(others, name => Assert.Contains(name, folders.Files.Keys));
    }

    [Fact]
    public async Task A_second_backup_in_the_same_minute_replaces_the_first()
    {
        using var app = await TestApp.StartAsync();
        var folders = new FakeBackupFolders();
        var service = New(app, folders);
        await service.ChooseFolderAsync();

        await service.RunNowAsync();
        await app.Notes.CreateAsync("One more");
        var second = await service.RunNowAsync();

        Assert.Equal(1, second!.Result!.Notes);
        Assert.Single(folders.Files);
    }

    [Fact]
    public async Task An_older_backup_that_cannot_be_deleted_does_not_fail_the_new_one()
    {
        using var app = await TestApp.StartAsync();
        var folders = new FakeBackupFolders();
        foreach (var day in new[] { "01", "02", "03" })
        {
            folders.Files[$"falcon-notes-auto-2026-09-{day}_0900.zip"] = [1];
        }

        folders.Undeletable.Add("falcon-notes-auto-2026-09-01_0900.zip");
        var service = New(app, folders);
        await service.ChooseFolderAsync();

        var outcome = await service.RunNowAsync();

        Assert.Null(outcome!.Error);
        Assert.Equal(4, folders.Backups.Count);
        Assert.NotNull((await service.GetAsync()).LastRunAt);
    }

    [Fact]
    public async Task A_folder_that_is_gone_is_reported_kept_and_tried_again()
    {
        using var app = await TestApp.StartAsync();
        var folders = new FakeBackupFolders();
        var service = New(app, folders);
        await service.ChooseFolderAsync();
        await service.RunIfDueAsync();
        var first = (await service.GetAsync()).LastRunAt;
        app.Clock.Advance(TimeSpan.FromDays(7));

        folders.Unavailable = true;
        var failed = await service.RunIfDueAsync();

        Assert.Equal((null, AutoExportService.FolderUnavailable), (failed!.Result, failed.Error));
        var settings = await service.GetAsync();
        Assert.Equal((first, AutoExportService.FolderUnavailable, true), (settings.LastRunAt, settings.LastError, settings.Enabled));
        Assert.Empty(Directory.GetFiles(Path.Combine(app.Directories.CacheDirectory, "export")));

        folders.Unavailable = false;
        var retried = await service.RunIfDueAsync(); // still due: the next pass tries again

        Assert.Null(retried!.Error);
        Assert.Null((await service.GetAsync()).LastError);
        Assert.Equal(2, folders.Files.Count);
    }

    [Fact]
    public async Task A_failed_write_deletes_no_older_backup_and_says_why()
    {
        using var app = await TestApp.StartAsync();
        var folders = new FakeBackupFolders();
        foreach (var day in new[] { "01", "02", "03", "04" })
        {
            folders.Files[$"falcon-notes-auto-2026-09-{day}_0900.zip"] = [1];
        }

        var service = New(app, folders);
        await service.ChooseFolderAsync();

        folders.WriteFails = new IOException("No space left on device") { HResult = 28 };
        Assert.Equal(AutoExportService.NotEnoughSpace, (await service.RunNowAsync())!.Error);

        folders.WriteFails = new IOException("The pipe broke");
        Assert.Equal(AutoExportService.CouldNotSave, (await service.RunNowAsync())!.Error);

        Assert.Equal(4, folders.Backups.Count);
        Assert.Null((await service.GetAsync()).LastRunAt);
        Assert.Null(await LastExportAsync(app));
    }

    [Fact]
    public async Task Turning_it_off_gives_up_the_folder_keeps_its_backups_and_the_choices()
    {
        using var app = await TestApp.StartAsync();
        var folders = new FakeBackupFolders();
        var service = New(app, folders);
        await service.ChooseFolderAsync();
        await service.SetEveryDaysAsync(30);
        await service.SetKeepAsync(10);
        await service.RunNowAsync();

        var off = await service.TurnOffAsync();

        Assert.Equal(AutoExportSettings.Default with { EveryDays = 30, Keep = 10 }, off);
        Assert.Equal(["tree:backups"], folders.Released);
        Assert.Single(folders.Files);
        app.Clock.Advance(TimeSpan.FromDays(60));
        Assert.Null(await service.RunIfDueAsync());
    }

    [Fact]
    public async Task Another_folder_gives_up_the_first_and_gets_a_backup_at_once()
    {
        using var app = await TestApp.StartAsync();
        var folders = new FakeBackupFolders();
        var service = New(app, folders);
        await service.ChooseFolderAsync();
        folders.Unavailable = true;
        await service.RunIfDueAsync();
        folders.Unavailable = false;

        folders.Next = new("tree:card", "Card");
        var settings = await service.ChooseFolderAsync();

        Assert.Equal(("tree:card", "Card", null, null), (settings!.Folder, settings.FolderName, settings.LastRunAt, settings.LastError));
        Assert.Equal(["tree:backups"], folders.Released);
        Assert.NotNull(await service.RunIfDueAsync());
        Assert.Equal(["tree:card"], folders.WrittenTo);

        await service.ChooseFolderAsync(); // the same folder again: its access is kept
        Assert.Equal(["tree:backups"], folders.Released);
    }

    [Fact]
    public async Task Choices_that_are_not_offered_fall_back_to_the_defaults()
    {
        using var app = await TestApp.StartAsync();
        var service = New(app, new FakeBackupFolders());
        await service.ChooseFolderAsync();

        await service.SetEveryDaysAsync(0);
        var settings = await service.SetKeepAsync(1000);

        Assert.Equal((7, 3), (settings.EveryDays, settings.Keep));
    }

    [Fact]
    public async Task Changes_and_backups_are_announced()
    {
        using var app = await TestApp.StartAsync();
        var service = New(app, new FakeBackupFolders());
        var running = new List<bool>();
        service.Changed += () => running.Add(service.IsRunning);

        await service.ChooseFolderAsync();
        await service.RunNowAsync();

        Assert.Equal([false, true, false], running); // the folder chosen, the backup started, the backup ended
    }

    [Fact]
    public async Task A_platform_without_folder_access_offers_and_does_nothing()
    {
        using var app = await TestApp.StartAsync();
        var service = New(app, folders: null);

        Assert.False(service.IsAvailable);
        Assert.Null(await service.ChooseFolderAsync());
        Assert.Null(await service.RunIfDueAsync());
    }

    [Fact]
    public async Task Maintenance_makes_the_backup_that_is_due_at_start()
    {
        using var app = await TestApp.StartAsync();
        await app.Notes.CreateAsync("Remember me");
        var folders = new FakeBackupFolders();
        var service = New(app, folders);
        await service.ChooseFolderAsync();
        var ended = new TaskCompletionSource();
        service.Changed += () =>
        {
            if (!service.IsRunning)
            {
                ended.TrySetResult();
            }
        };
        var tasks = new StartupTasks(app.Notes, app.Cleanup, app.Directories, NullLogger<StartupTasks>.Instance, service);
        using var stop = new CancellationTokenSource();

        var loop = tasks.RunPeriodicallyAsync(TimeProvider.System, stop.Token);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await loop;

        Assert.Single(folders.Backups);
    }

    [Fact]
    public async Task Erasing_everything_gives_up_the_folder_and_leaves_its_backups()
    {
        using var app = await TestApp.StartAsync();
        var folders = new FakeBackupFolders();
        var service = New(app, folders);
        await service.ChooseFolderAsync();
        await service.RunNowAsync();

        await new EraseAllData(new DeviceKeyStore(app.Secrets), app.Storage, app.Directories, service).RunAsync();

        Assert.Equal(["tree:backups"], folders.Released);
        Assert.Single(folders.Files);
        Assert.Null(await service.RunIfDueAsync()); // the database is closed: no backup, and no error
    }

    [Theory]
    [InlineData("falcon-notes-auto-2026-09-28_1430.zip", true)]
    [InlineData("falcon-notes-2026-09-28.zip", false)] // an export saved by hand
    [InlineData("falcon-notes-auto-2026-09-28_1430 (1).zip", false)]
    [InlineData("falcon-notes-auto-2026-09-28_1430.zip.part", false)]
    [InlineData("my-falcon-notes-auto-2026-09-28_1430.zip", false)]
    [InlineData("falcon-notes-auto-2026-09-28_1430.zip\n", false)]
    public void Only_names_the_app_gives_count_as_its_own(string name, bool own) => Assert.Equal(own, AutoExportService.IsOwnFile(name));

    private static AutoExportService New(TestApp app, FakeBackupFolders? folders) =>
        new(ExportConformanceTests.NewExporter(app), app.Directories, app.Storage, app.Clock, NullLogger<AutoExportService>.Instance, folders);

    private static Task<DateTime?> LastExportAsync(TestApp app) =>
        app.Storage.Database.ReadAsync(connection => SettingsStore.Get<DateTime?>(connection, "lastExportAt"));
}
