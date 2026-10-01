using FalconNotes.Core.Crypto;
using FalconNotes.Core.Startup;
using FalconNotes.Core.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Tests.Startup;

public class DatabaseStartupTests
{
    [Fact]
    public async Task First_run_creates_a_key_and_the_database()
    {
        using var dir = new TempDirectory();
        var secrets = new FakeSecretStore();
        var startup = new DatabaseStartup(new DeviceKeyStore(secrets), new FakeDirectories(dir.Path));

        var (outcome, database) = await startup.RunAsync();

        Assert.Equal(StartupOutcome.Ready, outcome);
        Assert.NotNull(database);
        Assert.True(File.Exists(startup.DatabasePath));
        Assert.True(secrets.Values.ContainsKey(DeviceKeyStore.SecretName));
    }

    [Fact]
    public async Task Later_runs_reuse_the_key()
    {
        using var dir = new TempDirectory();
        var secrets = new FakeSecretStore();
        var startup = new DatabaseStartup(new DeviceKeyStore(secrets), new FakeDirectories(dir.Path));
        await startup.RunAsync();
        var key = secrets.Values[DeviceKeyStore.SecretName];

        var (outcome, _) = await startup.RunAsync();

        Assert.Equal(StartupOutcome.Ready, outcome);
        Assert.Equal(key, secrets.Values[DeviceKeyStore.SecretName]);
    }

    [Fact]
    public async Task A_database_without_its_key_is_key_lost_and_untouched()
    {
        using var dir = new TempDirectory();
        var secrets = new FakeSecretStore();
        var keys = new DeviceKeyStore(secrets);
        var startup = new DatabaseStartup(keys, new FakeDirectories(dir.Path));
        await startup.RunAsync();
        SqliteConnection.ClearAllPools();
        var before = await File.ReadAllBytesAsync(startup.DatabasePath, TestContext.Current.CancellationToken);
        await keys.ForgetAsync();

        var (outcome, database) = await startup.RunAsync();

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
        var keys = new DeviceKeyStore(secrets);
        var startup = new DatabaseStartup(keys, new FakeDirectories(dir.Path));
        await startup.RunAsync();
        await keys.CreateAsync();

        var (outcome, _) = await startup.RunAsync();

        Assert.Equal(StartupOutcome.KeyLost, outcome);
    }

    [Fact]
    public async Task A_damaged_stored_key_reads_as_no_key()
    {
        var secrets = new FakeSecretStore();
        secrets.Values[DeviceKeyStore.SecretName] = "not base64!";

        Assert.Null(await new DeviceKeyStore(secrets).GetAsync());
    }
}
