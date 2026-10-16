using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Platform;

namespace FalconNotes.Core.Stickers;

/// <summary>One sticker in a pack on this device.</summary>
/// <param name="PackId">Its pack.</param>
/// <param name="File">Its file in the pack's folder, such as <c>007.png</c>.</param>
/// <param name="Name">What it shows, for its description and the file name it gets on a note.</param>
/// <param name="ContentType">Its picture type.</param>
public sealed record Sticker(string PackId, string File, string Name, string ContentType);

/// <summary>A sticker pack added on this device (docs/04, Stickers).</summary>
/// <param name="Id">Its ID: lower-case letters, digits and dashes. A pack added again with the same ID replaces it.</param>
/// <param name="Name">Its name.</param>
/// <param name="Author">Who made it, if the pack says.</param>
/// <param name="Licence">Its licence, if the pack says.</param>
/// <param name="Version">Its version, if the pack says.</param>
/// <param name="Added">When it was added, in Unix milliseconds; part of its stickers' URLs, so a replaced pack is not shown from a cache.</param>
/// <param name="Stickers">Its stickers, in the pack's order.</param>
public sealed record StickerPack(
    string Id, string Name, string? Author, string? Licence, string? Version, long Added, IReadOnlyList<Sticker> Stickers);

/// <summary>
/// The sticker packs on this device: added from a ZIP file the user chooses, listed, and removed (docs/04, Stickers).
/// The app never downloads anything: a pack is fetched outside the app, like a backup. Packs live under
/// <c>{data}/stickers/{id}/</c>, unencrypted, since they are not the user's writing; a sticker put on a note is copied
/// into the encrypted attachment store, so removing its pack leaves the note as it is.
/// </summary>
/// <remarks>
/// Nothing in a pack's ZIP names a path on the device: each sticker is written as a numbered file (<c>001.png</c>), and
/// the pack's description is written anew as <c>pack.json</c>. A pack is assembled in a hidden folder and moved into
/// place, so a pack is either all there or not there at all.
/// </remarks>
/// <param name="directories">Where the app keeps its data.</param>
/// <param name="time">The clock.</param>
public sealed partial class StickerPackService(IAppDirectories directories, TimeProvider time)
{
    /// <summary>The file that describes a pack, inside its ZIP. Optional: without it every picture is a sticker.</summary>
    public const string ManifestName = "sticker-pack.json";

    /// <summary>The most stickers a pack can have.</summary>
    public const int MaxStickers = 300;

    /// <summary>The largest sticker: 2 MiB.</summary>
    public const long MaxStickerBytes = 2L * 1024 * 1024;

    /// <summary>The largest ZIP file a pack can come in: 200 MiB.</summary>
    public const long MaxZipBytes = 200L * 1024 * 1024;

    private const long MaxManifestBytes = 64 * 1024;
    private const int MaxNameLength = 60;
    private const string StoredManifestName = "pack.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly Dictionary<string, string> TypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "image/png", ["jpg"] = "image/jpeg", ["jpeg"] = "image/jpeg", ["gif"] = "image/gif", ["webp"] = "image/webp",
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<StickerPack>? _packs;

    /// <summary>The folder that holds the packs.</summary>
    public string Root => Path.Combine(directories.DataDirectory, "stickers");

    /// <summary>
    /// Whether stickers are offered where a note of this kind is written: notes and quick notes, as with moods. A todo
    /// list and a habit are not written in the composer.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>Whether they are.</returns>
    public static bool Allowed(NoteKind kind) => kind is NoteKind.Note or NoteKind.Quick;

    /// <summary>The packs on this device, by name.</summary>
    /// <returns>The packs; empty when there are none.</returns>
    public async Task<IReadOnlyList<StickerPack>> ListAsync()
    {
        if (_packs is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync();
        try
        {
            return _packs ??= await Task.Run(ReadAll);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adds a pack from a ZIP file of pictures, replacing the pack with the same ID if there is one. The ZIP may hold a
    /// <c>sticker-pack.json</c> that names the pack and lists its stickers; without one, every PNG, JPEG, GIF and WebP
    /// picture in it is a sticker, in the order of their paths, and the pack is named after the file.
    /// </summary>
    /// <param name="content">The ZIP file, read to the end. It need not be seekable.</param>
    /// <param name="fileName">The file's name.</param>
    /// <param name="cancellationToken">Cancels; nothing is left behind.</param>
    /// <returns>The pack added.</returns>
    /// <exception cref="UserFacingException">The file is not a sticker pack the app can use, with the reason.</exception>
    /// <exception cref="NotEnoughSpaceException">The device is full.</exception>
    public async Task<StickerPack> AddAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => AddLockedAsync(content, fileName, cancellationToken), cancellationToken);
        }
        catch (IOException e) when (AttachmentService.IsDiskFull(e))
        {
            throw new NotEnoughSpaceException();
        }
        finally
        {
            _packs = null;
            _gate.Release();
        }
    }

    /// <summary>Removes a pack from this device. Stickers already on notes stay: they are the notes' own copies.</summary>
    /// <param name="id">The pack.</param>
    /// <returns>False when there is no such pack.</returns>
    public async Task<bool> RemoveAsync(string id)
    {
        if (!PackId().IsMatch(id))
        {
            return false;
        }

        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var folder = Path.Combine(Root, id);
                if (!Directory.Exists(folder))
                {
                    return false;
                }

                // Moved aside first, so the pack is gone at once even if deleting its files is slow or fails.
                var removed = Path.Combine(Root, $".removed-{Guid.NewGuid():N}");
                Directory.Move(folder, removed);
                TryDeleteDirectory(removed);
                return true;
            });
        }
        finally
        {
            _packs = null;
            _gate.Release();
        }
    }

    /// <summary>Opens a sticker's picture, for the page and for putting it on a note.</summary>
    /// <param name="packId">Its pack.</param>
    /// <param name="file">Its file, as <see cref="Sticker.File"/> names it.</param>
    /// <returns>The open file and its type, or null when there is no such sticker.</returns>
    public (Stream Content, string ContentType)? Open(string packId, string file)
    {
        var match = StoredFile().Match(file);
        if (!PackId().IsMatch(packId) || !match.Success)
        {
            return null;
        }

        var path = Path.Combine(Root, packId, file);
        try
        {
            return (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true),
                TypesByExtension[match.Groups[1].Value]);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private async Task<StickerPack> AddLockedAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        RemoveLeftovers();

        // Android's content streams cannot seek, and ZipArchive needs to: work from a copy in the cache, as restore does.
        var cache = Path.Combine(directories.CacheDirectory, "stickers");
        Directory.CreateDirectory(cache);
        var copy = Path.Combine(cache, $"{Guid.NewGuid():N}.zip");
        var staging = Path.Combine(Root, $".adding-{Guid.NewGuid():N}");
        try
        {
            await using (var target = File.Create(copy))
            {
                await new MeteredStream(content, MaxZipBytes, null).CopyToAsync(target, 1024 * 1024, cancellationToken);
            }

            await using var stream = File.OpenRead(copy);
            ZipArchive zip;
            try
            {
                zip = new ZipArchive(stream, ZipArchiveMode.Read);
            }
            catch (InvalidDataException)
            {
                throw new UserFacingException("This file is not a sticker pack. A sticker pack is a ZIP file of pictures.");
            }

            using (zip)
            {
                Directory.CreateDirectory(staging);
                var pack = await ReadPackAsync(zip, fileName, staging, cancellationToken);
                await File.WriteAllTextAsync(
                    Path.Combine(staging, StoredManifestName), JsonSerializer.Serialize(Stored.From(pack), JsonOptions), cancellationToken);
                MoveIntoPlace(staging, Path.Combine(Root, pack.Id));
                return pack;
            }
        }
        catch (FileTooLargeException)
        {
            throw new UserFacingException("This file is too large to be a sticker pack: a pack can be at most 200 MB.");
        }
        finally
        {
            TryDeleteFile(copy);
            TryDeleteDirectory(staging);
        }
    }

    private async Task<StickerPack> ReadPackAsync(ZipArchive zip, string fileName, string staging, CancellationToken cancellationToken)
    {
        var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/') && !Ignored(e.FullName)).ToList();
        var manifestEntry = entries
            .Where(e => string.Equals(Path.GetFileName(e.FullName), ManifestName, StringComparison.OrdinalIgnoreCase) && e.FullName.Count(c => c == '/') <= 1)
            .OrderBy(e => e.FullName.Count(c => c == '/'))
            .FirstOrDefault();
        var manifest = manifestEntry is null ? null : await ReadManifestAsync(manifestEntry, cancellationToken);
        var folder = manifestEntry is null ? "" : manifestEntry.FullName[..(manifestEntry.FullName.LastIndexOf('/') + 1)];

        List<(ZipArchiveEntry Entry, string? Name)> chosen;
        if (manifest?.Stickers is { } listed)
        {
            var byPath = entries.GroupBy(e => e.FullName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            chosen = [];
            foreach (var sticker in listed)
            {
                if (string.IsNullOrWhiteSpace(sticker.File) || !byPath.TryGetValue(folder + sticker.File.TrimStart('/'), out var entry))
                {
                    throw new UserFacingException($"This sticker pack lists \"{sticker.File}\", which is not in it.");
                }

                chosen.Add((entry, sticker.Name));
            }
        }
        else
        {
            chosen = entries
                .Where(e => TypesByExtension.ContainsKey(Extension(e.FullName)))
                .OrderBy(e => e.FullName, StringComparer.Ordinal)
                .Select(e => (e, (string?)null))
                .ToList();
        }

        if (chosen.Count == 0)
        {
            throw new UserFacingException("This sticker pack has no pictures in it.");
        }

        if (chosen.Count > MaxStickers)
        {
            throw new UserFacingException($"A sticker pack can have at most {MaxStickers} stickers; this one has {chosen.Count:N0}.");
        }

        var packName = Clean(manifest?.Name) ?? Clean(Path.GetFileNameWithoutExtension(UploadPolicy.SanitizeFileName(fileName))) ?? "Stickers";
        var id = Slug(manifest?.Id) ?? Slug(packName) ?? "stickers";
        var stickers = new List<Sticker>();
        foreach (var (entry, name) in chosen)
        {
            var shown = Clean(name) ?? Clean(Path.GetFileNameWithoutExtension(entry.FullName)) ?? "Sticker";
            if (entry.Length > MaxStickerBytes)
            {
                throw new UserFacingException($"\"{shown}\" is too large for a sticker: a sticker can be at most 2 MB.");
            }

            var bytes = new byte[entry.Length];
            await using (var source = entry.Open())
            {
                await source.ReadExactlyAsync(bytes, cancellationToken);
            }

            if (Sniff(bytes) is not { } extension)
            {
                throw new UserFacingException($"\"{shown}\" is not a PNG, JPEG, GIF or WebP picture, so it cannot be a sticker.");
            }

            var file = $"{stickers.Count + 1:000}.{extension}";
            await File.WriteAllBytesAsync(Path.Combine(staging, file), bytes, cancellationToken);
            stickers.Add(new Sticker(id, file, shown, TypesByExtension[extension]));
        }

        return new StickerPack(id, packName, Clean(manifest?.Author), Clean(manifest?.Licence ?? manifest?.License), Clean(manifest?.Version),
            time.GetUtcNow().ToUnixTimeMilliseconds(), stickers);
    }

    private static async Task<Manifest> ReadManifestAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            if (entry.Length > MaxManifestBytes)
            {
                throw new JsonException();
            }

            await using var stream = entry.Open();
            return await JsonSerializer.DeserializeAsync<Manifest>(stream, JsonOptions, cancellationToken) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new UserFacingException($"This sticker pack's {ManifestName} could not be read.");
        }
    }

    private IReadOnlyList<StickerPack> ReadAll()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        RemoveLeftovers();
        var packs = new List<StickerPack>();
        foreach (var folder in Directory.EnumerateDirectories(Root))
        {
            var id = Path.GetFileName(folder);
            if (!PackId().IsMatch(id))
            {
                continue;
            }

            try
            {
                var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(Path.Combine(folder, StoredManifestName)), JsonOptions);
                if (stored is not null)
                {
                    packs.Add(stored.ToPack(id));
                }
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                // A pack that cannot be read is left out; removing it is not this listing's business.
            }
        }

        return packs
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static void MoveIntoPlace(string staging, string target)
    {
        string? old = null;
        if (Directory.Exists(target))
        {
            old = Path.Combine(Path.GetDirectoryName(target)!, $".replaced-{Guid.NewGuid():N}");
            Directory.Move(target, old);
        }

        try
        {
            Directory.Move(staging, target);
        }
        catch
        {
            if (old is not null)
            {
                Directory.Move(old, target);
            }

            throw;
        }

        if (old is not null)
        {
            TryDeleteDirectory(old);
        }
    }

    /// <summary>Folders left by an add or a removal that was cut short.</summary>
    private void RemoveLeftovers()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(Root, ".*"))
        {
            TryDeleteDirectory(folder);
        }
    }

    /// <summary>The picture's type from its first bytes, as its extension; null when it is none the app shows.</summary>
    private static string? Sniff(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => "png",
        [0xFF, 0xD8, 0xFF, ..] => "jpg",
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7' or (byte)'9', (byte)'a', ..] => "gif",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "webp",
        _ => null,
    };

    /// <summary>Entries that are not part of a pack: macOS's resource forks and hidden files.</summary>
    private static bool Ignored(string path) =>
        path.StartsWith("__MACOSX/", StringComparison.Ordinal) || path.Split('/').Any(part => part.StartsWith('.'));

    private static string Extension(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot >= 0 && dot > path.LastIndexOf('/') ? path[(dot + 1)..] : "";
    }

    /// <summary>A name to show: trimmed, without control characters, at most 60 characters; null when nothing is left.</summary>
    private static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (text.Length > MaxNameLength)
        {
            text = text[..MaxNameLength].TrimEnd();
        }

        return text.Length == 0 ? null : text;
    }

    /// <summary>An ID made from a name: lower-case letters and digits, joined by dashes; null when nothing is left.</summary>
    private static string? Slug(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (var c in value.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                text.Append(c);
            }
            else if (char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark && text.Length > 0 && text[^1] != '-')
            {
                text.Append('-');
            }
        }

        var slug = text.ToString().Trim('-');
        if (slug.Length > 40)
        {
            slug = slug[..40].TrimEnd('-');
        }

        return slug.Length == 0 ? null : slug;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // removed at the next add, or with the cache
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a hidden folder: removed at the next listing or add
        }
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex PackId();

    [GeneratedRegex("^[0-9]{3}\\.(png|jpg|gif|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex StoredFile();

    /// <summary>A pack's <c>sticker-pack.json</c>, as its maker writes it. Every field is optional.</summary>
    private sealed record Manifest(
        string? Id, string? Name, string? Author, string? Licence, string? License, string? Version, IReadOnlyList<ManifestSticker>? Stickers);

    /// <summary>One sticker in a pack's <c>sticker-pack.json</c>: its file, relative to that file, and what it shows.</summary>
    private sealed record ManifestSticker(string? File, string? Name);

    /// <summary>A pack as this app writes it in the pack's folder, after checking every sticker.</summary>
    private sealed record Stored(string Name, string? Author, string? Licence, string? Version, long Added, IReadOnlyList<StoredSticker> Stickers)
    {
        public static Stored From(StickerPack pack) =>
            new(pack.Name, pack.Author, pack.Licence, pack.Version, pack.Added, pack.Stickers.Select(s => new StoredSticker(s.File, s.Name)).ToList());

        public StickerPack ToPack(string id) =>
            new(id, Name, Author, Licence, Version, Added, Stickers
                .Where(s => StoredFile().Match(s.File) is { Success: true })
                .Select(s => new Sticker(id, s.File, s.Name, TypesByExtension[StoredFile().Match(s.File).Groups[1].Value]))
                .ToList());
    }

    private sealed record StoredSticker(string File, string Name);
}
