using System.Globalization;
using FalconNotes.Core.Platform;

namespace FalconNotes.Core.Maintenance;

/// <summary>
/// Copies the database file before migrations change it, to <c>backups/falcon-{utc}-v{old}.db</c>, keeping the newest
/// three (docs/02, Start-up sequence). The copies are as encrypted as the database.
/// </summary>
/// <param name="directories">Where the app keeps its data.</param>
/// <param name="time">The clock.</param>
public sealed class DatabaseBackups(IAppDirectories directories, TimeProvider time)
{
    /// <summary>How many copies are kept.</summary>
    public const int Keep = 3;

    /// <summary>The folder that holds the copies.</summary>
    public string Folder => Path.Combine(directories.DataDirectory, "backups");

    /// <summary>
    /// Copies a database file that no connection has open (its write-ahead log checkpointed), then deletes the oldest
    /// copies beyond <see cref="Keep"/>.
    /// </summary>
    /// <param name="databasePath">The database file.</param>
    /// <param name="version">Its schema version before the migrations.</param>
    /// <returns>The copy's path.</returns>
    public string Copy(string databasePath, int version)
    {
        Directory.CreateDirectory(Folder);
        var stamp = time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(Folder, $"falcon-{stamp}-v{version}.db");
        File.Copy(databasePath, path, overwrite: true);
        foreach (var old in Directory.GetFiles(Folder, "falcon-*.db").OrderDescending(StringComparer.Ordinal).Skip(Keep))
        {
            File.Delete(old);
        }

        return path;
    }
}
