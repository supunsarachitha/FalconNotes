using System.Globalization;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Platform;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Startup;

/// <summary>
/// The way out of Key lost (docs/03, Lost key; docs/07, Key lost): the unreadable database, attachments and database
/// copies are moved aside into <c>unreadable-{yyyyMMdd-HHmmss}/</c>, never deleted, and the old key is forgotten, so the
/// next start creates a new key and an empty database.
/// </summary>
/// <param name="keys">The device key store.</param>
/// <param name="directories">Where the data is.</param>
/// <param name="time">The clock, for the folder's name.</param>
public sealed class KeyLostRecovery(DeviceKeyStore keys, IAppDirectories directories, TimeProvider time)
{
    /// <summary>Moves the unreadable data aside and forgets the key.</summary>
    /// <returns>The folder the data went to.</returns>
    public async Task<string> MoveAsideAsync()
    {
        var stamp = time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var folder = Path.Combine(directories.DataDirectory, $"unreadable-{stamp}");
        await Task.Run(() =>
        {
            SqliteConnection.ClearAllPools();
            Directory.CreateDirectory(folder);
            foreach (var name in new[] { DatabaseStartup.DatabaseFileName, DatabaseStartup.DatabaseFileName + "-wal", DatabaseStartup.DatabaseFileName + "-shm" })
            {
                var path = Path.Combine(directories.DataDirectory, name);
                if (File.Exists(path))
                {
                    File.Move(path, Path.Combine(folder, name));
                }
            }

            foreach (var name in new[] { "attachments", "backups" })
            {
                var path = Path.Combine(directories.DataDirectory, name);
                if (Directory.Exists(path))
                {
                    Directory.Move(path, Path.Combine(folder, name));
                }
            }
        });
        await keys.ForgetAsync();
        return folder;
    }
}
