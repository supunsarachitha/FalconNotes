using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Platform;
using FalconNotes.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Startup;

/// <summary>
/// Erases everything Falcon Notes keeps on this device (docs/04, Starting over; docs/07, Settings → Profile): the
/// database, attachments, database copies, cache and the device key, so the next start-up creates a fresh key and an
/// empty database. Unlike <see cref="KeyLostRecovery"/>, nothing is kept aside: the confirmation dialog warns that
/// this cannot be undone. Automatic backups are turned off first, which gives up the access to their folder; the
/// backups already in that folder are the user's and stay.
/// </summary>
/// <param name="keys">The device key store.</param>
/// <param name="storage">The open database, closed before its file is deleted.</param>
/// <param name="directories">Where the data and cache are.</param>
/// <param name="autoExport">Automatic backups; null where they are not set up.</param>
public sealed class EraseAllData(DeviceKeyStore keys, StorageContext storage, IAppDirectories directories, AutoExportService? autoExport = null)
{
    /// <summary>Deletes every file Falcon Notes keeps on this device and forgets the device key.</summary>
    /// <returns>A task that completes when everything is gone.</returns>
    public async Task RunAsync()
    {
        if (autoExport is not null && storage.IsReady)
        {
            try
            {
                await autoExport.TurnOffAsync();
            }
            catch (Exception e) when (e is SqliteException or IOException or InvalidOperationException)
            {
                // The settings are deleted with the database either way; nothing may stop the erase.
            }
        }

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
