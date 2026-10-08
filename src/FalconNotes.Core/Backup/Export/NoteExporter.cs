using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Settings;
using FalconNotes.Core.Storage;
using Microsoft.Extensions.Logging;

namespace FalconNotes.Core.Backup.Export;

/// <summary>
/// Writes the notes as the web app's export ZIP, manifest version 3, byte for byte (docs/05). Copied from the server's
/// <c>NoteExporter</c>; only the data access changed: notes come from this app's database (plain text in
/// <c>NoteBodies</c>), files through <see cref="AttachmentService"/>, and the account is the profile's display name.
/// </summary>
/// <remarks>
/// <para>Archive layout (with the monthly layout and Markdown):</para>
/// <code>
/// manifest.json                                   what was exported, with every note's path
/// 2026-09/2026-09-28_1430_buy-maple-syrup.md      one file per note
/// todo/2026-09/2026-09-28_1500_groceries.md       todo lists, and quick notes under quick-notes/, habits under habits/
/// attachments/5d1e0a7c_sunset.png                 decrypted attached files
/// </code>
/// <para>
/// Notes link their files by relative path (<c>../attachments/…</c>). Notes are read in batches, so memory does not grow
/// with the number of notes. A file that cannot be read is left out (or, damaged part-way, kept incomplete) and listed
/// under <c>problems</c>, instead of failing the export.
/// </para>
/// </remarks>
/// <param name="storage">The open database.</param>
/// <param name="attachments">Opens decrypted files.</param>
/// <param name="profile">The display name for the manifest.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Logs IDs, never names.</param>
public sealed class NoteExporter(
    StorageContext storage, AttachmentService attachments, ProfileService profile, TimeProvider time, ILogger<NoteExporter> logger)
{
    private const int BatchSize = 200;

    /// <summary>The suggested file name, e.g. <c>falcon-notes-2026-09-28.zip</c> (the web app writes <c>maple-notes-…</c>).</summary>
    /// <param name="options">The options (for the time zone).</param>
    /// <returns>The file name.</returns>
    public string FileName(ExportOptions options) =>
        $"falcon-notes-{TimeZoneInfo.ConvertTime(time.GetUtcNow(), options.TimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip";

    /// <summary>Writes the archive.</summary>
    /// <param name="options">The options; validated here.</param>
    /// <param name="output">Where to write; need not be seekable.</param>
    /// <param name="progress">Told how many notes have been read so far ("Reading your notes… {n}").</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>What was written.</returns>
    /// <exception cref="UserFacingException">The start date is after the end date.</exception>
    public async Task<ExportResult> WriteAsync(ExportOptions options, Stream output, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        options.Validate();
        var account = (await profile.GetAsync())?.DisplayName ?? "";
        var exportedAt = TimeZoneInfo.ConvertTime(time.GetUtcNow(), options.TimeZone);
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json" };
        var problems = new List<string>();
        var manifestNotes = new List<object>();
        var labelColors = new SortedDictionary<string, string>(StringComparer.Ordinal); // the labels the notes carry
        var attachmentCount = 0;
        var labels = await storage.Database.ReadAsync(LabelsById, cancellationToken);
        DateTime? fromUtc = options.From is { } from ? StartOfDayUtc(from, options.TimeZone) : null;
        DateTime? beforeUtc = options.To is { } to ? StartOfDayUtc(to.AddDays(1), options.TimeZone) : null;

        await using (var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
        {
            (DateTime Time, Guid Id)? after = null;
            while (true)
            {
                var position = after;
                var batch = await storage.Database.ReadAsync(connection =>
                    NoteRepository.Load(connection, NoteRepository.ExportRows(connection, options.IncludeArchived, fromUtc, beforeUtc, position, BatchSize)),
                    cancellationToken);
                foreach (var note in batch)
                {
                    var noteLabels = note.LabelIds.Where(labels.ContainsKey).Select(id => labels[id]).ToList();
                    var (path, exported) = await WriteNoteAsync(zip, note, noteLabels, options, usedPaths, problems, cancellationToken);
                    attachmentCount += exported.Attachments.Count;
                    foreach (var label in noteLabels)
                    {
                        labelColors[label.Name] = label.Color;
                    }

                    manifestNotes.Add(new
                    {
                        exported.Id,
                        Path = path,
                        Kind = exported.KindName,
                        DailyDate = exported.DailyDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        CreatedAt = exported.Created.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                        exported.Tags,
                        Labels = exported.LabelNames,
                        exported.Archived,
                        Attachments = exported.Attachments.Select(a => a.ArchivePath),
                    });
                }

                progress?.Report(manifestNotes.Count);
                if (batch.Count < BatchSize)
                {
                    break;
                }

                after = (batch[^1].CreatedAtUtc, batch[^1].Id);
            }

            var manifest = new
            {
                Application = "Maple Notes", // the format's name, which the web app's restore checks (D12): never this app's
                ManifestVersion = 3, // 2: notes record their kind and daily date; 3: and their labels
                ExportedAt = exportedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                Account = account,
                Options = new
                {
                    Format = options.Format.ToString().ToLowerInvariant(),
                    Layout = options.Layout.ToString().ToLowerInvariant(),
                    TimeZone = options.TimeZoneName,
                    options.IncludeArchived,
                    options.IncludeAttachments,
                    From = options.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    To = options.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                },
                NoteCount = manifestNotes.Count,
                AttachmentCount = attachmentCount,
                Problems = problems,
                Labels = labelColors.Select(l => new { Name = l.Key, Color = l.Value }),
                Notes = manifestNotes,
            };
            await WriteTextAsync(zip, "manifest.json", NoteFormatter.ManifestJson(manifest), exportedAt, cancellationToken);
        }

        await output.FlushAsync(cancellationToken);
        return new ExportResult(manifestNotes.Count, attachmentCount, problems);
    }

    /// <summary>When a day starts in a time zone, in UTC (at the first valid half hour if a daylight-saving change skips midnight).</summary>
    /// <param name="date">The day.</param>
    /// <param name="timeZone">The time zone.</param>
    /// <returns>The first instant of the day, in UTC.</returns>
    public static DateTime StartOfDayUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30); // midnight skipped by a daylight-saving change in this zone
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private async Task<(string Path, ExportedNote Note)> WriteNoteAsync(
        ZipArchive zip, Note note, IReadOnlyList<(string Name, string Color)> labels, ExportOptions options, ISet<string> usedPaths,
        List<string> problems, CancellationToken cancellationToken)
    {
        var content = note.Content;
        var created = TimeZoneInfo.ConvertTime(new DateTimeOffset(note.CreatedAtUtc), options.TimeZone);
        var updated = TimeZoneInfo.ConvertTime(new DateTimeOffset(note.UpdatedAtUtc), options.TimeZone);

        var folder = string.Join('/', new[] { KindFolder(note.Kind), ExportNaming.Folder(options.Layout, created) }.Where(f => f.Length > 0));
        var name = $"{created.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture)}_{ExportNaming.Slug(content)}.{NoteFormatter.Extension(options.Format)}";
        var notePath = ExportNaming.Unique(folder.Length == 0 ? name : $"{folder}/{name}", usedPaths);

        var exportedAttachments = new List<ExportedAttachment>();
        if (options.IncludeAttachments)
        {
            foreach (var attachment in note.Attachments.OrderBy(a => a.CreatedAtUtc))
            {
                var archivePath = ExportNaming.Unique(
                    $"attachments/{attachment.Id.ToString("N")[^8..]}_{ExportNaming.SafeFileName(attachment.FileName)}", usedPaths);
                if (await WriteAttachmentAsync(zip, attachment, archivePath, options.TimeZone, problems, cancellationToken))
                {
                    exportedAttachments.Add(new ExportedAttachment(
                        attachment.FileName, attachment.ContentType, attachment.SizeBytes, UploadPolicy.IsImage(attachment.ContentType),
                        archivePath, ExportNaming.RelativePath(notePath, archivePath)));
                }
            }
        }

        var exported = new ExportedNote(
            note.Id, content, created, updated, note.Tags, note.IsPinned, note.IsArchived, exportedAttachments, note.Kind, note.DailyDate,
            labels.Select(l => l.Name).Order(StringComparer.Ordinal).ToList());
        await WriteTextAsync(zip, notePath, NoteFormatter.Render(exported, options.Format), updated, cancellationToken);
        return (notePath, exported);
    }

    /// <summary>
    /// Every label's name and colour, by ID. The colour is written as the web app names it ("Blue"), which is how it
    /// is stored.
    /// </summary>
    private static Dictionary<Guid, (string Name, string Color)> LabelsById(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        using var command = Sql.Command(connection, "SELECT Id, Name, Color FROM Labels");
        var labels = new Dictionary<Guid, (string, string)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            labels[reader.GetId(0)] = (reader.GetString(1), reader.GetString(2));
        }

        return labels;
    }

    private async Task<bool> WriteAttachmentAsync(
        ZipArchive zip, Attachment attachment, string archivePath, TimeZoneInfo timeZone, List<string> problems, CancellationToken cancellationToken)
    {
        Stream source;
        try
        {
            source = (await attachments.OpenAsync(attachment.Id))?.Content ?? throw new FileNotFoundException("The attachment's row is gone.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.LogWarning(ex, "Attachment {AttachmentId} could not be read and was left out of an export.", attachment.Id);
            problems.Add($"{archivePath}: the stored file could not be read, so it was left out.");
            return false;
        }

        await using (source)
        {
            var entry = zip.CreateEntry(archivePath, CompressionFor(attachment.ContentType));
            entry.LastWriteTime = TimeZoneInfo.ConvertTime(new DateTimeOffset(attachment.CreatedAtUtc), timeZone);
            await using var target = await entry.OpenAsync(cancellationToken);
            try
            {
                await source.CopyToAsync(target, cancellationToken);
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "Attachment {AttachmentId} is damaged; its exported copy is incomplete.", attachment.Id);
                problems.Add($"{archivePath}: the stored file is damaged, so this copy is incomplete.");
            }
        }

        return true;
    }

    private static async Task WriteTextAsync(ZipArchive zip, string path, string text, DateTimeOffset lastWrite, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = lastWrite;
        await using var stream = await entry.OpenAsync(cancellationToken);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    /// <summary>Todo lists, quick notes and habits get their own top-level folders; timeline notes stay at the top.</summary>
    private static string KindFolder(NoteKind kind) => kind switch
    {
        NoteKind.Todo => "todo",
        NoteKind.Quick => "quick-notes",
        NoteKind.Habit => "habits",
        _ => string.Empty,
    };

    // Media and archives are already compressed; compressing them again only costs time.
    private static CompressionLevel CompressionFor(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && contentType != "image/svg+xml"
        || contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
        || contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
        || contentType is "application/zip" or "application/gzip" or "application/x-7z-compressed" or "application/pdf"
            ? CompressionLevel.NoCompression
            : CompressionLevel.Fastest;
}
