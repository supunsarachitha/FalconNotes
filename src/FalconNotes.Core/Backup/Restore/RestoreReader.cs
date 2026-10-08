using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Labels;
using FalconNotes.Core.Platform;

namespace FalconNotes.Core.Backup.Restore;

/// <summary>
/// Reads what a restore brings in: export archives (every format and folder layout, with their files, from this app,
/// the Maple Notes web app or a server) and single Markdown, text or JSON files. Port of <c>web/import/parse.ts</c>
/// (docs/05, Reading); ZIPs are read with <see cref="ZipArchive"/>.
/// </summary>
/// <param name="directories">Where <c>cache/restore/</c> is.</param>
/// <param name="time">The clock, for files with no date.</param>
public sealed partial class RestoreReader(IAppDirectories directories, TimeProvider time)
{
    /// <summary>The file types a restore accepts, for the picker.</summary>
    public static readonly IReadOnlyList<string> Accept = [".zip", ".md", ".markdown", ".txt", ".json"];

    /// <summary>The largest entry a restore reads: 2 GiB.</summary>
    public const long MaxEntryBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// The largest note file read from an archive: a note is at most 100,000 characters, which is at most 400 KB of
    /// UTF-8 plus its header (as the web app since 1.11).
    /// </summary>
    public const long MaxNoteFileBytes = 4L * 1024 * 1024;

    /// <summary>The largest manifest read: a few hundred bytes for each note of even a very large backup.</summary>
    public const long MaxManifestBytes = 64L * 1024 * 1024;

    private static readonly Dictionary<string, NoteKind> Kinds = new(StringComparer.Ordinal)
    {
        ["note"] = NoteKind.Note, ["todo"] = NoteKind.Todo, ["quick"] = NoteKind.Quick, ["habit"] = NoteKind.Habit,
    };

    /// <summary>Reads the chosen files. Dispose the plan when the restore is done.</summary>
    /// <param name="files">The files.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The notes found and the problems met.</returns>
    public async Task<RestorePlan> ReadAsync(IEnumerable<RestoreSource> files, CancellationToken cancellationToken = default)
    {
        var plan = new RestorePlan();
        foreach (var file in files)
        {
            var modified = file.ModifiedUtc is { } m ? new DateTimeOffset(DateTime.SpecifyKind(m, DateTimeKind.Utc)) : time.GetUtcNow();
            try
            {
                if (ExtensionOf(file.Name) == "zip")
                {
                    await ReadArchiveAsync(file, modified, plan, cancellationToken);
                }
                else if (IsNoteFile(file.Name))
                {
                    await using var stream = await file.OpenReadAsync();
                    using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
                    var text = await reader.ReadToEndAsync(cancellationToken);
                    plan.Items.Add(ToItem(file.Name, ParseNote(file.Name, text, modified), modified, null, null));
                }
                else
                {
                    plan.Problems.Add($"{file.Name}: choose .zip, .md, .txt or .json files.");
                }
            }
            catch (Exception e) when (e is UserFacingException or IOException or InvalidDataException)
            {
                plan.Problems.Add($"{file.Name}: {e.Message}");
            }
        }

        return plan;
    }

    private async Task ReadArchiveAsync(RestoreSource file, DateTimeOffset modified, RestorePlan plan, CancellationToken cancellationToken)
    {
        // Android's content streams cannot seek, and ZipArchive needs to: work from a copy in the cache (docs/05).
        var folder = Path.Combine(directories.CacheDirectory, "restore");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, $"{Guid.NewGuid():N}.zip");
        await using (var source = await file.OpenReadAsync())
        await using (var target = File.Create(copy))
        {
            await source.CopyToAsync(target, 1024 * 1024, cancellationToken);
        }

        ZipArchive zip;
        var stream = File.OpenRead(copy);
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            stream.Dispose(); // Windows cannot delete a file that is still open
            File.Delete(copy);
            throw new UserFacingException("This is not a ZIP archive.");
        }

        plan.Keep(zip, copy);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            entries[entry.FullName] = entry;
        }

        List<JsonElement>? listed = null;
        if (entries.TryGetValue("manifest.json", out var manifestEntry))
        {
            try
            {
                using var manifest = JsonDocument.Parse(ReadText(manifestEntry, MaxManifestBytes));
                var root = manifest.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("application", out var application) && application.ValueKind == JsonValueKind.String
                    && application.GetString() == "Maple Notes"
                    && root.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array)
                {
                    listed = notes.EnumerateArray().Select(n => n.Clone()).ToList();
                }

                // Manifest version 3: the colours of the labels the notes carry. Read whatever the application is,
                // as the web app does.
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
                {
                    foreach (var label in labels.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.Object))
                    {
                        if (label.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                            && label.TryGetProperty("color", out var color) && color.ValueKind == JsonValueKind.String
                            && ColorOf(color.GetString()!) is { } known)
                        {
                            plan.LabelColors[LabelRules.NameKey(name.GetString()!)] = known;
                        }
                    }
                }
            }
            catch (Exception e) when (e is JsonException or UserFacingException)
            {
                plan.Problems.Add($"{file.Name}: manifest.json could not be read; its notes are read without it.");
            }
        }

        // An export: exactly the notes its manifest lists. Any other archive: every note file in it.
        var toRead = listed is not null
            ? listed.Where(n => n.ValueKind == JsonValueKind.Object && n.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                .Select(n => (Path: n.GetProperty("path").GetString()!, Manifest: (JsonElement?)n))
                .ToList()
            : entries.Keys.Where(name => IsNoteFile(name) && !name.StartsWith("attachments/", StringComparison.Ordinal)
                    && !name.StartsWith("__MACOSX/", StringComparison.Ordinal))
                .Select(name => (Path: name, Manifest: (JsonElement?)null))
                .ToList();

        foreach (var (path, manifestNote) in toRead)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(path, out var entry))
            {
                plan.Problems.Add($"{file.Name}: {path} is listed but missing.");
                continue;
            }

            try
            {
                plan.Items.Add(ToItem(path, ParseNote(path, ReadText(entry, MaxNoteFileBytes), modified), modified, entries, manifestNote));
            }
            catch (Exception e) when (e is UserFacingException or IOException or InvalidDataException)
            {
                plan.Problems.Add($"{file.Name}: {path}: {e.Message}");
            }
        }
    }

    /// <summary>Reads a text entry, refusing one larger than <paramref name="max"/> before anything is extracted.</summary>
    private static string ReadText(ZipArchiveEntry entry, long max)
    {
        if (entry.Length > max)
        {
            throw new UserFacingException("This file is too large to be a Falcon Notes or Maple Notes note or manifest.");
        }

        using var stream = OpenEntry(entry);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }

    private static Stream OpenEntry(ZipArchiveEntry entry) =>
        entry.Length > MaxEntryBytes ? throw new UserFacingException($"\"{entry.FullName}\" is too large to restore.") : entry.Open();

    /// <summary>What a note file says about itself, before its files are found.</summary>
    private sealed record ParsedNote(
        Guid? Id, string Content, DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt, bool Pinned, bool Archived,
        NoteKind Kind, string? DailyDate, IReadOnlyList<string> Labels, IReadOnlyList<(string Path, string? Name, string? Type)> Attachments);

    /// <summary>Reads a note file in any export format; anything else becomes a note with the file's text.</summary>
    private static ParsedNote ParseNote(string name, string text, DateTimeOffset modified)
    {
        var clean = (text.StartsWith('﻿') ? text[1..] : text).Replace("\r\n", "\n", StringComparison.Ordinal);
        var extension = ExtensionOf(name);
        var parsed = extension switch
        {
            "json" => ParseJson(clean),
            "txt" => ParsePlainText(clean),
            _ => ParseMarkdown(clean),
        };
        if (parsed is not null)
        {
            return parsed;
        }

        if (extension == "json")
        {
            throw new UserFacingException("This JSON file is not a Falcon Notes or Maple Notes note.");
        }

        return new ParsedNote(null, clean, modified, modified, false, false, NoteKind.Note, null, [], []);
    }

    /// <summary>Markdown with the export's front matter; null if the file has none.</summary>
    private static ParsedNote? ParseMarkdown(string text)
    {
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
        {
            return null;
        }

        var end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = new List<string>();
        var inAttachments = false;
        foreach (var line in text[4..end].Split('\n'))
        {
            var item = FrontMatterItem().Match(line);
            if (inAttachments && item.Success)
            {
                if (JsonString(item.Groups[1].Value) is { } path)
                {
                    paths.Add(path);
                }

                continue;
            }

            var field = FrontMatterField().Match(line);
            if (!field.Success)
            {
                continue;
            }

            inAttachments = field.Groups[1].Value == "attachments";
            fields[field.Groups[1].Value] = field.Groups[2].Value;
        }

        if (!fields.ContainsKey("created"))
        {
            return null;
        }

        var body = text[(end + 5)..];
        if (body.StartsWith('\n'))
        {
            body = body[1..];
        }

        var content = body.EndsWith('\n') ? body[..^1] : body;
        var names = new List<string>();
        var section = body.LastIndexOf("\n## Attachments\n\n", StringComparison.Ordinal);
        if (paths.Count > 0 && section >= 0 && (section == 0 || body[section - 1] == '\n'))
        {
            content = body[..Math.Max(0, section - 1)];
            foreach (var line in body[(section + 17)..].Split('\n'))
            {
                var link = AttachmentLink().Match(line);
                if (link.Success)
                {
                    names.Add(Unescape().Replace(link.Groups[1].Value, "$1"));
                }
            }
        }

        return new ParsedNote(
            UuidOrNull(fields.GetValueOrDefault("id")),
            content,
            Date(fields.GetValueOrDefault("created")),
            Date(fields.GetValueOrDefault("updated")),
            fields.GetValueOrDefault("pinned") == "true",
            fields.GetValueOrDefault("archived") == "true",
            KindOf(fields.GetValueOrDefault("kind")),
            Day(fields.GetValueOrDefault("daily")),
            JsonNames(fields.GetValueOrDefault("labels")),
            paths.Select((path, i) => (path, names.Count == paths.Count ? names[i] : null, (string?)null)).ToList());
    }

    /// <summary>Plain text with the export's header lines; null if the file does not start with one.</summary>
    private static ParsedNote? ParsePlainText(string text)
    {
        if (!text.StartsWith("Created: ", StringComparison.Ordinal))
        {
            return null;
        }

        var split = text.IndexOf("\n\n", StringComparison.Ordinal);
        var header = split < 0 ? text : text[..split];
        var body = split < 0 ? "" : text[(split + 2)..];
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = new List<string>();
        foreach (var line in header.Split('\n'))
        {
            var field = PlainTextField().Match(line);
            if (!field.Success)
            {
                continue;
            }

            if (field.Groups[1].Value == "Attachment")
            {
                paths.Add(field.Groups[2].Value);
            }
            else
            {
                fields[field.Groups[1].Value] = field.Groups[2].Value;
            }
        }

        var state = fields.GetValueOrDefault("State") ?? "";
        var created = Date(fields.GetValueOrDefault("Created"));
        return new ParsedNote(
            null,
            body.EndsWith('\n') ? body[..^1] : body,
            created,
            Date(fields.GetValueOrDefault("Updated")) ?? created,
            state.Contains("pinned", StringComparison.Ordinal),
            state.Contains("archived", StringComparison.Ordinal),
            KindOf(fields.GetValueOrDefault("Kind")),
            Day(fields.GetValueOrDefault("Daily")),
            JsonNames(fields.GetValueOrDefault("Labels")),
            paths.Select(path => (path, (string?)null, (string?)null)).ToList());
    }

    /// <summary>One note as JSON, as the export writes it; null for any other JSON.</summary>
    private static ParsedNote? ParseJson(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var note = document.RootElement;
            if (note.ValueKind != JsonValueKind.Object || !note.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? Text(string property) => note.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            bool True(string property) => note.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;
            var attachments = new List<(string, string?, string?)>();
            if (note.TryGetProperty("attachments", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in list.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string? Field(string property) => a.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    attachments.Add((path.GetString()!, Field("fileName"), Field("contentType")));
                }
            }

            return new ParsedNote(
                UuidOrNull(Text("id")), content.GetString()!, Date(Text("createdAt")), Date(Text("updatedAt")),
                True("pinned"), True("archived"), KindOf(Text("kind")), Day(Text("dailyDate")),
                note.TryGetProperty("labels", out var labels) ? Names(labels) : [], attachments);
        }
    }

    private static RestoreItem ToItem(
        string source, ParsedNote parsed, DateTimeOffset fallbackTime, Dictionary<string, ZipArchiveEntry>? entries, JsonElement? manifest)
    {
        var attachments = new List<RestoreAttachment>();
        var missing = new List<string>();
        foreach (var (relative, recordedName, recordedType) in parsed.Attachments)
        {
            var path = Resolve(source, relative);
            if (entries is null || !entries.TryGetValue(path, out var entry))
            {
                missing.Add(recordedName ?? OriginalName(path));
                continue;
            }

            var name = recordedName ?? OriginalName(path);
            attachments.Add(new RestoreAttachment(name, recordedType ?? UploadPolicy.GuessFromName(name), entry.Length, () => OpenEntry(entry)));
        }

        string? Field(string property) => manifest is { } m && m.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var created = parsed.CreatedAt ?? Date(Field("createdAt")) ?? fallbackTime;
        return new RestoreItem(
            source,
            UuidOrNull(Field("id")) ?? parsed.Id,
            parsed.Content,
            created,
            parsed.UpdatedAt ?? created,
            parsed.Pinned,
            parsed.Archived || (manifest is { } mf && mf.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True),
            Field("kind") is { } kind ? KindOf(kind) : parsed.Kind,
            parsed.DailyDate ?? Day(Field("dailyDate")),
            parsed.Labels.Count == 0 && manifest is { } ml && ml.TryGetProperty("labels", out var listedLabels) ? Names(listedLabels) : parsed.Labels,
            attachments,
            missing);
    }

    /// <summary>Resolves a path relative to a file inside the archive, e.g. <c>../attachments/x</c> from <c>2026-09/note.md</c>.</summary>
    private static string Resolve(string fromFile, string relative)
    {
        var parts = fromFile.Split('/').SkipLast(1).ToList();
        foreach (var part in relative.Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (part is not ("." or ""))
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }

    /// <summary>An exported file's original name: exports put 8 characters of its ID in front.</summary>
    private static string OriginalName(string path) => IdPrefix().Replace(path[(path.LastIndexOf('/') + 1)..], "", 1);

    private static string ExtensionOf(string name) => name[(name.LastIndexOf('.') + 1)..].ToLowerInvariant();

    private static bool IsNoteFile(string name) => ExtensionOf(name) is "md" or "markdown" or "txt" or "json";

    private static Guid? UuidOrNull(string? value) =>
        value is not null && Uuid().IsMatch(value) ? Guid.ParseExact(value, "D") : null;

    private static NoteKind KindOf(string? value) =>
        Kinds.TryGetValue(value?.ToLowerInvariant() ?? "", out var kind) ? kind : NoteKind.Note;

    private static string? Day(string? value) => value is not null && DayPattern().IsMatch(value) ? value : null;

    /// <summary>An ISO 8601 date and time with its offset, as exports write them.</summary>
    private static DateTimeOffset? Date(string? value) =>
        value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    /// <summary>The strings of a JSON array; anything else in it, or anything but an array, is left out.</summary>
    private static List<string> Names(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(name => name.ValueKind == JsonValueKind.String).Select(name => name.GetString()!).ToList()
            : [];

    /// <summary>A list of names written as a JSON array, as the exports write labels.</summary>
    private static List<string> JsonNames(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return Names(document.RootElement);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>A label colour exactly as the exports name it ("Blue"); null for anything else.</summary>
    private static LabelColor? ColorOf(string value) =>
        Enum.GetValues<LabelColor>().Cast<LabelColor?>().FirstOrDefault(color => color!.Value.ToString() == value);

    private static string? JsonString(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.String ? document.RootElement.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^ {2}- (.*)$")]
    private static partial Regex FrontMatterItem();

    [GeneratedRegex(@"^([a-z]+):\s?(.*)$")]
    private static partial Regex FrontMatterField();

    [GeneratedRegex(@"^([A-Za-z]+): (.*)$")]
    private static partial Regex PlainTextField();

    [GeneratedRegex(@"^- !?\[((?:\\.|[^\]\\])*)\]\(")]
    private static partial Regex AttachmentLink();

    [GeneratedRegex(@"\\([\\\[\]])")]
    private static partial Regex Unescape();

    [GeneratedRegex("^[0-9a-f]{8}_")]
    private static partial Regex IdPrefix();

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex Uuid();

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex DayPattern();
}
