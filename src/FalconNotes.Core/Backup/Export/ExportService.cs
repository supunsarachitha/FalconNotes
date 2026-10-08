using FalconNotes.Core.Platform;
using FalconNotes.Core.Storage;

namespace FalconNotes.Core.Backup.Export;

/// <summary>
/// Exports to a file the user chooses (docs/05, Writing the file): the archive is written into
/// <c>cache/export/{random}.zip</c>, handed to the system's save dialog with the suggested name, and deleted whatever
/// happens. A saved export is remembered as the last one. <see cref="AutoExportService"/> writes the automatic ones.
/// </summary>
/// <param name="exporter">Writes the archive.</param>
/// <param name="saver">The save dialog.</param>
/// <param name="directories">Where the cache is.</param>
/// <param name="storage">The open database, for <c>lastExportAt</c>.</param>
/// <param name="time">The clock.</param>
public sealed class ExportService(NoteExporter exporter, IFileSaver saver, IAppDirectories directories, StorageContext storage, TimeProvider time)
{
    /// <summary>The setting that holds when the last export was saved, by hand or automatically.</summary>
    internal const string LastExportKey = "lastExportAt";

    /// <summary>Writes the export and lets the user save it.</summary>
    /// <param name="options">The options.</param>
    /// <param name="progress">Told how many notes have been read.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>What was exported, or null when the user cancelled the save dialog.</returns>
    public async Task<ExportResult?> ExportAsync(ExportOptions options, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(directories.CacheDirectory, "export");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.zip");
        try
        {
            ExportResult result;
            await using (var file = File.Create(path))
            {
                result = await Task.Run(() => exporter.WriteAsync(options, file, progress, cancellationToken), cancellationToken);
            }

            await using var saved = File.OpenRead(path);
            if (!await saver.SaveAsync(exporter.FileName(options), saved, cancellationToken))
            {
                return null;
            }

            await storage.Database.InTransactionAsync((connection, transaction) =>
            {
                SettingsStore.Set(connection, LastExportKey, time.GetUtcNow().UtcDateTime, transaction);
                return true;
            }, CancellationToken.None);
            return result;
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>When the last export was saved, for "Last export: …" (docs/07, Backup &amp; data).</summary>
    /// <returns>The time, or null when there has been none.</returns>
    public Task<DateTime?> LastExportAsync() =>
        storage.Database.ReadAsync(connection => SettingsStore.Get<DateTime?>(connection, LastExportKey));

    /// <summary>The toast after an export: "Exported {n} notes and {m} files."</summary>
    /// <param name="result">What was exported.</param>
    /// <returns>The message.</returns>
    public static string Summary(ExportResult result) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Exported {result.Notes:N0} note{(result.Notes == 1 ? "" : "s")} and {result.Files:N0} file{(result.Files == 1 ? "" : "s")}.");
}
