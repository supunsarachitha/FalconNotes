using System.Security.Cryptography;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Platform;
using FalconNotes.Core.Storage;

namespace FalconNotes.Core.Startup;

/// <summary>What the start-up check found (docs/02, Start-up sequence).</summary>
public enum StartupOutcome
{
    /// <summary>The database is open and ready.</summary>
    Ready,

    /// <summary>A database exists, but the device key is missing or does not open it: show Key lost, delete nothing.</summary>
    KeyLost,
}

/// <summary>
/// Steps 2.1 and 2.2 of the start-up sequence (docs/02): find or create the device key, then open the database with
/// it. A database without its key is never touched.
/// </summary>
/// <param name="keys">The device key store.</param>
/// <param name="directories">Where the database lives.</param>
public sealed class DatabaseStartup(DeviceKeyStore keys, IAppDirectories directories)
{
    /// <summary>The database file's name in the data directory.</summary>
    public const string DatabaseFileName = "falcon.db";

    /// <summary>The database file's full path.</summary>
    public string DatabasePath => Path.Combine(directories.DataDirectory, DatabaseFileName);

    /// <summary>Runs the check.</summary>
    /// <returns>The outcome, and the database when it is <see cref="StartupOutcome.Ready"/>.</returns>
    public async Task<(StartupOutcome Outcome, Database? Database)> RunAsync()
    {
        var key = await keys.GetAsync();
        if (key is null)
        {
            if (File.Exists(DatabasePath))
            {
                return (StartupOutcome.KeyLost, null);
            }

            Directory.CreateDirectory(directories.DataDirectory);
            key = await keys.CreateAsync();
        }

        using var keyMaterial = new KeyMaterial(key);
        CryptographicOperations.ZeroMemory(key);
        var database = new Database(DatabasePath, keyMaterial.DatabaseKey);
        try
        {
            await using var connection = await database.OpenAsync();
        }
        catch (DatabaseKeyException)
        {
            return (StartupOutcome.KeyLost, null);
        }

        return (StartupOutcome.Ready, database);
    }
}
