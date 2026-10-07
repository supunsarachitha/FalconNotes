using FalconNotes.Core.Crypto;
using FalconNotes.Core.Maintenance;
using FalconNotes.Core.Startup;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Tests.Startup;

public class DatabaseStartupTests
{
    private static DatabaseStartup Create(TempDirectory dir, FakeSecretStore secrets, StorageContext? storage = null)
    {
        var directories = new FakeDirectories(dir.Path);
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        return new DatabaseStartup(new DeviceKeyStore(secrets), directories, new DatabaseBackups(directories, clock), storage ?? new StorageContext());
    }

    [Fact]
    public async Task First_run_creates_a_key_the_database_and_the_installation_id()
    {
        using var dir = new TempDirectory();
        var secrets = new FakeSecretStore();
        var storage = new StorageContext();
        var startup = Create(dir, secrets, storage);

        var (outcome, database) = await startup.RunAsync();

        Assert.Equal(StartupOutcome.Ready, outcome);
        Assert.NotNull(database);
        Assert.True(File.Exists(startup.DatabasePath));
        Assert.True(secrets.Values.ContainsKey(DeviceKeyStore.SecretName));
        Assert.True(storage.IsReady);
        Assert.NotEqual(Guid.Empty, storage.InstallationId);
        await using var connection = await database.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Migrations.Latest, Migrations.ReadVersion(connection));
        storage.Close();
    }

    [Fact]
    public async Task Later_runs_reuse_the_key_and_the_installation_id()
    {
        using var dir = new TempDirectory();
        var secrets = new FakeSecretStore();
        var first = new StorageContext();
        await Create(dir, secrets, first).RunAsync();
        var key = secrets.Values[DeviceKeyStore.SecretName];
        var second = new StorageContext();

        var (outcome, _) = await Create(dir, secrets, second).RunAsync();

        Assert.Equal(StartupOutcome.Ready, outcome);
        Assert.Equal(key, secrets.Values[DeviceKeyStore.SecretName]);
        Assert.Equal(first.InstallationId, second.InstallationId);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "data", "backups")));
    }

    [Fact]
    public async Task The_database_is_opened_with_a_key_derived_from_the_device_key()
    {
        using var dir = new TempDirectory();
        var secrets = new FakeSecretStore();
        var startup = Create(dir, secrets);
        await startup.RunAsync();
        SqliteConnection.ClearAllPools();
        var deviceKey = Convert.FromBase64String(secrets.Values[DeviceKeyStore.SecretName]);

        await Assert.ThrowsAsync<DatabaseKeyException>(() =>
            new Database(startup.DatabasePath, deviceKey, pooling: false).OpenAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_database_without_its_key_is_key_lost_and_untouched()
    {
        using var dir = new TempDirectory();
        var secrets = new FakeSecretStore();
        var startup = Create(dir, secrets);
        await startup.RunAsync();
        SqliteConnection.ClearAllPools();
        var before = await File.ReadAllBytesAsync(startup.DatabasePath, TestContext.Current.CancellationToken);
        await new DeviceKeyStore(secrets).ForgetAsync();

        var (outcome, database) = await Create(dir, secrets).RunAsync();

        Assert.Equal(StartupOutcome.KeyLost, outcome);
        Assert.Null(database);
        Assert.Empty(secrets.Values);
        Assert.Equal(before, await File.ReadAllBytesAsync(startup.DatabasePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_different_key_is_key_lost()
    {
        using var dir = new TempDirectory();
        var secrets = new FakeSecretStore();
        await Create(dir, secrets).RunAsync();
        await new DeviceKeyStore(secrets).CreateAsync();

        var (outcome, _) = await Create(dir, secrets).RunAsync();

        Assert.Equal(StartupOutcome.KeyLost, outcome);
    }

    [Fact]
    public async Task A_damaged_stored_key_reads_as_no_key()
    {
        var secrets = new FakeSecretStore();
        secrets.Values[DeviceKeyStore.SecretName] = "not base64!";

        Assert.Null(await new DeviceKeyStore(secrets).GetAsync());
    }

    [Fact]
    public void Database_copies_keep_the_newest_three()
    {
        using var dir = new TempDirectory();
        var directories = new FakeDirectories(dir.Path);
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        var backups = new DatabaseBackups(directories, clock);
        var database = dir.File("falcon.db");
        File.WriteAllText(database, "x");

        for (var i = 0; i < 5; i++)
        {
            backups.Copy(database, 1);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(
            ["falcon-20260928-120200-v1.db", "falcon-20260928-120300-v1.db", "falcon-20260928-120400-v1.db"],
            Directory.GetFiles(backups.Folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }
}
