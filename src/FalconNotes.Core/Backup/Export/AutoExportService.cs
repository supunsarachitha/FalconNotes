using System.Globalization;
using System.Text.RegularExpressions;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Platform;
using FalconNotes.Core.Storage;
using Microsoft.Extensions.Logging;

namespace FalconNotes.Core.Backup.Export;

/// <summary>
/// Automatic backups (docs/05, Automatic backups): when one is due, a full export is written into the folder the user
/// chose, and older automatic backups there are deleted beyond the number to keep. The app cannot run while it is
/// closed, so "due" is checked when it starts and with each hourly maintenance pass.
/// </summary>
/// <remarks>
/// The archive is the ordinary export (the same exporter, so the same format): Markdown, by month, with files and
/// archived notes, so that one backup restores everything. It is not encrypted, which is why this is off by default.
/// </remarks>
/// <param name="exporter">Writes the archive.</param>
/// <param name="directories">Where the cache is.</param>
/// <param name="storage">The open database, for the settings.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Logs counts, never names.</param>
/// <param name="folders">The platform's folder access; null on a platform that has none yet.</param>
public sealed partial class AutoExportService(
    NoteExporter exporter, IAppDirectories directories, StorageContext storage, TimeProvider time, ILogger<AutoExportService> logger,
    IBackupFolders? folders = null)
{
    /// <summary>Shown when the folder was deleted or moved, or the access to it was taken back.</summary>
    public const string FolderUnavailable = "Falcon Notes can no longer reach the folder. Choose it again.";

    /// <summary>Shown when the device or the folder's drive is full.</summary>
    public const string NotEnoughSpace = "There was not enough space.";

    /// <summary>Shown for any other failure.</summary>
    public const string CouldNotSave = "The backup could not be saved.";

    private const string Key = "autoExport";

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Raised when the settings change and when a backup starts or ends; may run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Whether this platform can keep a folder to write to. Without it, Settings does not offer the feature.</summary>
    public bool IsAvailable => folders is not null;

    /// <summary>Whether a backup is being written now.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// An automatic backup's file name, e.g. <c>falcon-notes-auto-2026-09-28_1430.zip</c>. It differs from a manual
    /// export's (<c>falcon-notes-2026-09-28.zip</c>) so that tidying the folder never deletes a file the user saved.
    /// </summary>
    /// <param name="now">The time of the backup.</param>
    /// <param name="zone">The device's time zone.</param>
    /// <returns>The file name.</returns>
    public static string FileName(DateTimeOffset now, TimeZoneInfo zone) =>
        $"falcon-notes-auto-{TimeZoneInfo.ConvertTime(now, zone).ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture)}.zip";

    /// <summary>Whether a file in the folder is one of the automatic backups, by its name.</summary>
    /// <param name="fileName">A file's name.</param>
    /// <returns>True for a name <see cref="FileName"/> makes, and nothing else.</returns>
    public static bool IsOwnFile(string fileName) => OwnFile().IsMatch(fileName);

    /// <summary>
    /// Whether a backup is due: none has been made, or the last one was made the chosen number of calendar days ago
    /// or more. Days, not hours, so that someone who opens the app each morning gets a backup each morning.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The device's time zone.</param>
    /// <returns>Whether to back up now.</returns>
    public static bool IsDue(AutoExportSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!settings.Enabled || settings.Folder is null)
        {
            return false;
        }

        if (settings.LastRunAt is not { } last || last > now)
        {
            return true; // never, or the clock was set back: a backup too many is better than one too few
        }

        return Day(now, zone).DayNumber - Day(last, zone).DayNumber >= settings.EveryDays;
    }

    /// <summary>Reads the settings.</summary>
    /// <returns>The settings; the defaults when none are stored.</returns>
    public Task<AutoExportSettings> GetAsync() => storage.Database.ReadAsync(connection => Read(connection));

    /// <summary>
    /// Lets the user choose the folder, and turns automatic backups on with it. The access to a folder used before is
    /// given up, and the new folder has no backup yet, so one is due at once.
    /// </summary>
    /// <returns>The new settings, or null when the user cancelled or the platform has no folder access.</returns>
    public async Task<AutoExportSettings?> ChooseFolderAsync()
    {
        if (folders is null || await folders.ChooseAsync() is not { } chosen)
        {
            return null;
        }

        string? before = null;
        var settings = await UpdateAsync(s =>
        {
            before = s.Folder;
            return s with { Enabled = true, Folder = chosen.Reference, FolderName = chosen.Name, LastRunAt = null, LastError = null };
        });
        if (before is not null && before != chosen.Reference)
        {
            folders.Release(before);
        }

        return settings;
    }

    /// <summary>Turns automatic backups off and gives up the access to the folder. The backups in it stay.</summary>
    /// <returns>The new settings.</returns>
    public async Task<AutoExportSettings> TurnOffAsync()
    {
        string? before = null;
        var settings = await UpdateAsync(s =>
        {
            before = s.Folder;
            return AutoExportSettings.Default with { EveryDays = s.EveryDays, Keep = s.Keep };
        });
        if (before is not null)
        {
            folders?.Release(before);
        }

        return settings;
    }

    /// <summary>Changes how many days apart the backups are.</summary>
    /// <param name="days">One of <see cref="AutoExportSettings.EveryDaysChoices"/>.</param>
    /// <returns>The new settings.</returns>
    public Task<AutoExportSettings> SetEveryDaysAsync(int days) => UpdateAsync(s => s with { EveryDays = days });

    /// <summary>Changes how many automatic backups are kept. Extra ones are deleted when the next backup is made.</summary>
    /// <param name="keep">One of <see cref="AutoExportSettings.KeepChoices"/>.</param>
    /// <returns>The new settings.</returns>
    public Task<AutoExportSettings> SetKeepAsync(int keep) => UpdateAsync(s => s with { Keep = keep });

    /// <summary>Makes a backup now, due or not ("Back up now"). Waits for one already being written.</summary>
    /// <param name="cancellationToken">Cancels the backup.</param>
    /// <returns>How it ended, or null when automatic backups are off.</returns>
    public async Task<AutoExportOutcome?> RunNowAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await RunAsync(onlyWhenDue: false, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Makes a backup when one is due (at start and with each maintenance pass). Never throws: a failure is kept in
    /// the settings for Settings and Home to show, and tried again at the next pass.
    /// </summary>
    /// <param name="cancellationToken">Cancels the backup.</param>
    /// <returns>How it ended, or null when none was due or one is already being written.</returns>
    public async Task<AutoExportOutcome?> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (folders is null || !storage.IsReady || !await _gate.WaitAsync(0, CancellationToken.None))
        {
            return null;
        }

        try
        {
            return await RunAsync(onlyWhenDue: true, cancellationToken);
        }
        catch (Exception e)
        {
            // The database closed under it (Erase all data), or the app is closing.
            logger.LogWarning("The automatic backup check stopped: {Kind}.", e.GetType().Name);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AutoExportOutcome?> RunAsync(bool onlyWhenDue, CancellationToken cancellationToken)
    {
        var settings = await GetAsync();
        if (folders is null || !settings.Enabled || settings.Folder is not { } folder
            || (onlyWhenDue && !IsDue(settings, time.GetUtcNow(), time.LocalTimeZone)))
        {
            return null;
        }

        IsRunning = true;
        Changed?.Invoke();
        try
        {
            return await WriteAsync(folders, folder, settings.Keep, cancellationToken);
        }
        finally
        {
            IsRunning = false;
            Changed?.Invoke();
        }
    }

    private async Task<AutoExportOutcome> WriteAsync(IBackupFolders target, string folder, int keep, CancellationToken cancellationToken)
    {
        var cache = Path.Combine(directories.CacheDirectory, "export");
        Directory.CreateDirectory(cache);
        var path = Path.Combine(cache, $"{Guid.NewGuid():N}.zip");
        try
        {
            // Listing first finds a folder that is gone before any note is read.
            var existing = await target.ListAsync(folder, cancellationToken);
            var options = new ExportOptions(
                ExportFormat.Md, ExportLayout.Month, IncludeArchived: true, IncludeAttachments: true, From: null, To: null, time.LocalTimeZone);
            ExportResult result;
            await using (var file = File.Create(path))
            {
                result = await Task.Run(() => exporter.WriteAsync(options, file, null, cancellationToken), cancellationToken);
            }

            var now = time.GetUtcNow();
            var name = FileName(now, time.LocalTimeZone);
            await using (var saved = File.OpenRead(path))
            {
                await target.WriteAsync(folder, name, saved, cancellationToken);
            }

            var pruned = await PruneAsync(target, folder, existing, name, keep, cancellationToken);
            await FinishAsync(folder, now, error: null);
            logger.LogInformation(
                "Automatic backup: {Notes} note(s) and {Files} file(s) written, {Pruned} older backup(s) deleted.", result.Notes, result.Files, pruned);
            return new AutoExportOutcome(result, null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The type only: a platform's message may name the folder or a file.
            logger.LogError("Automatic backup failed: {Kind}.", e.GetType().Name);
            var error = e switch
            {
                BackupFolderUnavailableException => FolderUnavailable,
                NotEnoughSpaceException => NotEnoughSpace,
                IOException io when AttachmentService.IsDiskFull(io) => NotEnoughSpace,
                _ => CouldNotSave,
            };
            await FinishAsync(folder, at: null, error);
            return new AutoExportOutcome(null, error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Deletes the automatic backups beyond the number to keep, oldest first by name, which is by time. Only files
    /// named as <see cref="FileName"/> names them are ever deleted, and never the one just written. A file that cannot
    /// be deleted is left for the next time: the backup itself is safe.
    /// </summary>
    private async Task<int> PruneAsync(
        IBackupFolders target, string folder, IReadOnlyList<string> existing, string written, int keep, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var extra = existing.Where(n => IsOwnFile(n) && n != written)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .Skip(keep - 1);
        foreach (var name in extra)
        {
            try
            {
                await target.DeleteAsync(folder, name, cancellationToken);
                deleted++;
            }
            catch (IOException e)
            {
                logger.LogWarning("An older automatic backup could not be deleted: {Kind}.", e.GetType().Name);
            }
        }

        return deleted;
    }

    /// <summary>
    /// Records how a backup ended, unless the folder was changed or the feature turned off while it was being written:
    /// then the result belongs to settings that are gone.
    /// </summary>
    private async Task FinishAsync(string folder, DateTimeOffset? at, string? error)
    {
        await storage.Database.InTransactionAsync((connection, transaction) =>
        {
            var settings = Read(connection, transaction);
            if (settings.Folder != folder)
            {
                return false;
            }

            SettingsStore.Set(connection, Key, settings with { LastRunAt = at ?? settings.LastRunAt, LastError = error }, transaction);
            if (at is { } saved)
            {
                SettingsStore.Set(connection, ExportService.LastExportKey, saved.UtcDateTime, transaction);
            }

            return true;
        }, CancellationToken.None);
    }

    private async Task<AutoExportSettings> UpdateAsync(Func<AutoExportSettings, AutoExportSettings> change)
    {
        var settings = await storage.Database.InTransactionAsync((connection, transaction) =>
        {
            var next = change(Read(connection, transaction)).Normalised();
            SettingsStore.Set(connection, Key, next, transaction);
            return next;
        }, CancellationToken.None);
        Changed?.Invoke();
        return settings;
    }

    private static AutoExportSettings Read(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction = null) =>
        (SettingsStore.Get<AutoExportSettings>(connection, Key, transaction) ?? AutoExportSettings.Default).Normalised();

    private static DateOnly Day(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    [GeneratedRegex(@"\Afalcon-notes-auto-\d{4}-\d{2}-\d{2}_\d{4}\.zip\z", RegexOptions.CultureInvariant)]
    private static partial Regex OwnFile();
}
