using FalconNotes.Core.Crypto;
using FalconNotes.Core.Startup;

namespace FalconNotes.Core.Tests.Startup;

/// <summary>New tests (docs/11): Key lost's way out moves the unreadable data aside and forgets the key, deleting
/// nothing (docs/03, Lost key; docs/07, Key lost).</summary>
public class KeyLostRecoveryTests
{
    [Fact]
    public async Task Moves_the_database_attachments_and_backups_aside_and_forgets_the_key()
    {
        using var app = await TestApp.StartAsync();
        await app.AddFileAsync(); // an attachments folder to move
        app.Storage.Close();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var dbPath = Path.Combine(app.Directories.DataDirectory, DatabaseStartup.DatabaseFileName);
        Assert.True(File.Exists(dbPath));
        var attachmentsPath = Path.Combine(app.Directories.DataDirectory, "attachments");
        Assert.True(Directory.Exists(attachmentsPath));

        var recovery = new KeyLostRecovery(new DeviceKeyStore(app.Secrets), app.Directories, app.Clock);
        var folder = await recovery.MoveAsideAsync();

        Assert.False(File.Exists(dbPath));
        Assert.False(Directory.Exists(attachmentsPath));
        Assert.True(File.Exists(Path.Combine(folder, DatabaseStartup.DatabaseFileName)));
        Assert.True(Directory.Exists(Path.Combine(folder, "attachments")));
        Assert.Null(await app.Secrets.GetAsync(DeviceKeyStore.SecretName));
    }

    [Fact]
    public async Task Leaves_nothing_behind_when_there_is_no_database_yet()
    {
        using var directory = new TempDirectory();
        var directories = new FalconNotes.Core.Tests.Fakes.FakeDirectories(directory.Path);
        var secrets = new FalconNotes.Core.Tests.Fakes.FakeSecretStore();
        await secrets.SetAsync(DeviceKeyStore.SecretName, Convert.ToBase64String(new byte[32]));
        var recovery = new KeyLostRecovery(new DeviceKeyStore(secrets), directories, new FalconNotes.Core.Tests.Fakes.FakeClock(DateTimeOffset.UtcNow));

        var folder = await recovery.MoveAsideAsync();

        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.Null(await secrets.GetAsync(DeviceKeyStore.SecretName));
    }
}
