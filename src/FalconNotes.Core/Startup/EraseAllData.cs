using FalconNotes.Core.Crypto;
using FalconNotes.Core.Platform;
using FalconNotes.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Startup;

/// <summary>
/// Erases everything Falcon Notes keeps on this device (docs/04, Starting over; docs/07, Settings → Profile): the
/// database, attachments, database copies, cache and the device key, so the next start-up creates a fresh key and an
/// empty database. Unlike <see cref="KeyLostRecovery"/>, nothing is kept aside: the confirmation dialog warns that
/// this cannot be undone.
/// </summary>
/// <param name="keys">The device key store.</param>
/// <param name="storage">The open database, closed before its file is deleted.</param>
/// <param name="directories">Where the data and cache are.</param>
public sealed class EraseAllData(DeviceKeyStore keys, StorageContext storage, IAppDirectories directories)
{
    /// <summary>Deletes every file Falcon Notes keeps on this device and forgets the device key.</summary>
    /// <returns>A task that completes when everything is gone.</returns>
    public async Task RunAsync()
    {
        storage.Close();
        await Task.Run(() =>
        {
            SqliteConnection.ClearAllPools();
            foreach (var name in new[]
                     {
                         DatabaseStartup.DatabaseFileName, DatabaseStartup.DatabaseFileName + "-wal", DatabaseStartup.DatabaseFileName + "-shm",
                     })
            {
                var path = Path.Combine(directories.DataDirectory, name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            foreach (var name in new[] { "attachments", "backups" })
            {
                var path = Path.Combine(directories.DataDirectory, name);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }

            if (Directory.Exists(directories.CacheDirectory))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directories.CacheDirectory))
                {
                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }
            }
        });
        await keys.ForgetAsync();
    }
}
