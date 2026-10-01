using System.Security.Cryptography;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Maintenance;
using FalconNotes.Core.Platform;
using FalconNotes.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Startup;

/// <summary>What the start-up check found (docs/02, Start-up sequence).</summary>
public enum StartupOutcome
{
    /// <summary>The database is open, migrated and ready.</summary>
    Ready,

    /// <summary>A database exists, but the device key is missing or does not open it: show Key lost, delete nothing.</summary>
    KeyLost,
}

/// <summary>
/// Steps 2.1 to 2.3 of the start-up sequence (docs/02): find or create the device key, open the database with the key
/// derived from it, copy the file before migrating an older schema, migrate, and make sure the installation has its
/// ID. A database without its key is never touched.
/// </summary>
/// <param name="keys">The device key store.</param>
/// <param name="directories">Where the database lives.</param>
/// <param name="backups">Copies the database before migrations.</param>
/// <param name="storage">Receives the open database.</param>
public sealed class DatabaseStartup(DeviceKeyStore keys, IAppDirectories directories, DatabaseBackups backups, StorageContext storage)
{
    /// <summary>The database file's name in the data directory.</summary>
    public const string DatabaseFileName = "falcon.db";

    /// <summary>The database file's full path.</summary>
    public string DatabasePath => Path.Combine(directories.DataDirectory, DatabaseFileName);

    /// <summary>Runs the check and, when the key opens the database, prepares it for use.</summary>
    /// <returns>The outcome, and the database when it is <see cref="StartupOutcome.Ready"/>.</returns>
    public async Task<(StartupOutcome Outcome, Database? Database)> RunAsync()
    {
        var deviceKey = await keys.GetAsync();
        if (deviceKey is null)
        {
            if (File.Exists(DatabasePath))
            {
                return (StartupOutcome.KeyLost, null);
            }

            Directory.CreateDirectory(directories.DataDirectory);
            deviceKey = await keys.CreateAsync();
        }

        var keyMaterial = new KeyMaterial(deviceKey);
        CryptographicOperations.ZeroMemory(deviceKey);
        var database = new Database(DatabasePath, keyMaterial.DatabaseKey);
        try
        {
            var installationId = await Task.Run(() => Prepare(database));
            storage.Open(database, keyMaterial, installationId);
            return (StartupOutcome.Ready, database);
        }
        catch (DatabaseKeyException)
        {
            keyMaterial.Dispose();
            return (StartupOutcome.KeyLost, null);
        }
    }

    private Guid Prepare(Database database)
    {
        // Check the key and the schema version with a connection outside the pool, so the file can be copied after.
        int version;
        using (var probe = database.OpenAsync().GetAwaiter().GetResult())
        {
            version = Migrations.ReadVersion(probe);
            if (version > 0 && version < Migrations.Latest)
            {
                using var checkpoint = Sql.Command(probe, "PRAGMA wal_checkpoint(TRUNCATE)");
                checkpoint.ExecuteNonQuery();
            }
        }

        if (version > 0 && version < Migrations.Latest)
        {
            SqliteConnection.ClearAllPools();
            backups.Copy(DatabasePath, version);
        }

        using var connection = database.OpenAsync().GetAwaiter().GetResult();
        Migrations.Apply(connection);
        var installationId = SettingsStore.Get<Guid?>(connection, "installationId");
        if (installationId is null)
        {
            installationId = Guid.NewGuid();
            SettingsStore.Set(connection, "installationId", installationId.Value);
        }

        return installationId.Value;
    }
}
