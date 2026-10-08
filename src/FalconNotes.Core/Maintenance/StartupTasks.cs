using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Platform;
using Microsoft.Extensions.Logging;

namespace FalconNotes.Core.Maintenance;

/// <summary>What one maintenance pass removed.</summary>
/// <param name="Trash">Notes and files purged from the trash.</param>
/// <param name="Attachments">Abandoned, orphan and temporary attachment files.</param>
/// <param name="TemporaryFolders">Folders of decrypted copies, exports and restores deleted.</param>
public sealed record MaintenanceResult(DeletedCount Trash, AttachmentCleanupResult Attachments, int TemporaryFolders);

/// <summary>
/// Step 2.4 of the start-up sequence (docs/02), repeated hourly while the app runs: purge the trash after 30 days,
/// remove abandoned and orphan attachment files, and delete the temporary folders in the cache (decrypted copies made
/// by Open, exports being written, restores being read). Never blocks the first screen. After each pass, an automatic
/// backup is made when one is due (docs/05, Automatic backups): after, so that the start's clean-up of the cache
/// never meets the archive being written there.
/// </summary>
/// <param name="notes">Purges the trash.</param>
/// <param name="cleanup">Removes attachment leftovers.</param>
/// <param name="directories">Where the cache is.</param>
/// <param name="logger">Logs counts, never names.</param>
/// <param name="autoExport">Automatic backups; null where they are not set up (tests of maintenance alone).</param>
public sealed class StartupTasks(
    NoteService notes, AttachmentCleanup cleanup, IAppDirectories directories, ILogger<StartupTasks> logger, AutoExportService? autoExport = null)
{
    /// <summary>How often maintenance runs while the app is open.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>The cache folders that only ever hold temporary, plain copies (docs/03, Where things are).</summary>
    public static readonly IReadOnlyList<string> TemporaryFolders = ["open", "export", "restore"];

    /// <summary>Runs one pass. Failures are logged and retried next time; they never stop the app.</summary>
    /// <param name="includeTemporaryFolders">
    /// Whether to delete the temporary folders: at start only, when no export, restore or opened copy is in use.
    /// </param>
    /// <returns>What was removed, or null when the pass failed.</returns>
    public async Task<MaintenanceResult?> RunAsync(bool includeTemporaryFolders)
    {
        try
        {
            var trash = await notes.PurgeExpiredTrashAsync();
            var attachments = await cleanup.RunAsync();
            var folders = includeTemporaryFolders ? await Task.Run(DeleteTemporaryFolders) : 0;
            logger.LogInformation(
                "Maintenance: {Notes} note(s) and {Files} file(s) purged from the trash, {Abandoned} abandoned and {Orphans} orphan file(s), {Folders} temporary folder(s).",
                trash.Notes, trash.Files, attachments.AbandonedUploads, attachments.OrphanFiles, folders);
            return new MaintenanceResult(trash, attachments, folders);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Maintenance failed; it runs again in an hour.");
            return null;
        }
    }

    /// <summary>Runs at start, then every hour until cancelled.</summary>
    /// <param name="time">The clock, for the timer.</param>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when cancelled.</returns>
    public async Task RunPeriodicallyAsync(TimeProvider time, CancellationToken cancellationToken)
    {
        await RunAsync(includeTemporaryFolders: true);
        await BackUpIfDueAsync(cancellationToken);
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RunAsync(includeTemporaryFolders: false);
                await BackUpIfDueAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
    }

    private Task BackUpIfDueAsync(CancellationToken cancellationToken) =>
        autoExport?.RunIfDueAsync(cancellationToken) ?? Task.CompletedTask;

    private int DeleteTemporaryFolders()
    {
        var deleted = 0;
        foreach (var name in TemporaryFolders)
        {
            var folder = Path.Combine(directories.CacheDirectory, name);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                Directory.Delete(folder, recursive: true);
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "A temporary folder could not be deleted; it is tried again at the next start.");
            }
        }

        return deleted;
    }
}
