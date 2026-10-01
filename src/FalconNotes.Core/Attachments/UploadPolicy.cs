using System.Net.Http.Headers;

namespace FalconNotes.Core.Attachments;

/// <summary>
/// Rules for accepting and showing files safely. Copied from the server's <c>UploadPolicy</c> (docs/04, Attachments),
/// with a table of extensions in place of ASP.NET Core's content-type provider.
/// </summary>
/// <remarks>
/// Files are untrusted. Only passive media (images, audio, video, plain text) are ever shown in the page; everything
/// else, including SVG and HTML, which can carry scripts, is only opened in another app or saved.
/// </remarks>
public static class UploadPolicy
{
    /// <summary>The type for files that are never shown in the page.</summary>
    public const string DownloadContentType = "application/octet-stream";

    private const int MaxFileNameLength = 200;

    private static readonly HashSet<string> InlineContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/avif", "image/bmp",
        "video/mp4", "video/webm",
        "audio/mpeg", "audio/mp4", "audio/ogg", "audio/wav", "audio/webm", "audio/flac",
        "text/plain",
    };

    /// <summary>Types by extension: <c>web/import/parse.ts</c>'s table plus common office and archive types.</summary>
    private static readonly Dictionary<string, string> TypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "image/png", ["jpg"] = "image/jpeg", ["jpeg"] = "image/jpeg", ["gif"] = "image/gif",
        ["webp"] = "image/webp", ["avif"] = "image/avif", ["bmp"] = "image/bmp", ["heic"] = "image/heic",
        ["heif"] = "image/heif", ["svg"] = "image/svg+xml", ["tif"] = "image/tiff", ["tiff"] = "image/tiff",
        ["ico"] = "image/x-icon",
        ["mp4"] = "video/mp4", ["m4v"] = "video/mp4", ["webm"] = "video/webm", ["mov"] = "video/quicktime",
        ["mkv"] = "video/x-matroska", ["avi"] = "video/x-msvideo", ["3gp"] = "video/3gpp",
        ["mp3"] = "audio/mpeg", ["m4a"] = "audio/mp4", ["aac"] = "audio/aac", ["ogg"] = "audio/ogg",
        ["oga"] = "audio/ogg", ["opus"] = "audio/ogg", ["wav"] = "audio/wav", ["flac"] = "audio/flac",
        ["pdf"] = "application/pdf", ["txt"] = "text/plain", ["md"] = "text/markdown", ["markdown"] = "text/markdown",
        ["json"] = "application/json", ["csv"] = "text/csv", ["html"] = "text/html", ["htm"] = "text/html",
        ["xml"] = "application/xml", ["rtf"] = "application/rtf",
        ["zip"] = "application/zip", ["gz"] = "application/gzip", ["7z"] = "application/x-7z-compressed",
        ["rar"] = "application/vnd.rar", ["tar"] = "application/x-tar",
        ["doc"] = "application/msword",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["xls"] = "application/vnd.ms-excel",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["ppt"] = "application/vnd.ms-powerpoint",
        ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ["odt"] = "application/vnd.oasis.opendocument.text",
        ["ods"] = "application/vnd.oasis.opendocument.spreadsheet",
        ["odp"] = "application/vnd.oasis.opendocument.presentation",
        ["epub"] = "application/epub+zip",
    };

    /// <summary>
    /// A safe file name: no folders, control characters or reserved characters, at most 200 characters, never empty.
    /// </summary>
    /// <param name="fileName">The name the file came with.</param>
    /// <returns>The sanitised name.</returns>
    public static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?' or '/')).ToArray());
        name = name.Trim().Trim('.').Trim();
        if (name.Length == 0)
        {
            return "file";
        }

        if (name.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(name);
            extension = extension.Length <= 20 ? extension : string.Empty;
            name = name[..(MaxFileNameLength - extension.Length)] + extension;
        }

        return name;
    }

    /// <summary>
    /// The type to record for a file: the reported type when it is meaningful, otherwise a guess from the extension.
    /// </summary>
    /// <param name="declared">The type the picker or file reported, if any.</param>
    /// <param name="fileName">The sanitised file name.</param>
    /// <returns>A lower-case media type without parameters.</returns>
    public static string ResolveContentType(string? declared, string fileName)
    {
        if (MediaTypeHeaderValue.TryParse(declared, out var parsed)
            && parsed.MediaType is { Length: > 0 and <= 100 } mediaType
            && !mediaType.Equals(DownloadContentType, StringComparison.OrdinalIgnoreCase))
        {
            return mediaType.ToLowerInvariant();
        }

        return GuessFromName(fileName);
    }

    /// <summary>A type guessed from a file's extension, or <see cref="DownloadContentType"/>.</summary>
    /// <param name="fileName">The file's name.</param>
    /// <returns>The type.</returns>
    public static string GuessFromName(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot >= 0 && TypesByExtension.TryGetValue(fileName[(dot + 1)..], out var type) ? type : DownloadContentType;
    }

    /// <summary>Whether a file of this type may be shown in the page.</summary>
    /// <param name="contentType">The recorded type.</param>
    /// <returns>True for passive media types.</returns>
    public static bool CanDisplayInline(string contentType) => InlineContentTypes.Contains(contentType);

    /// <summary>Whether the file is an image shown inline.</summary>
    /// <param name="contentType">The recorded type.</param>
    /// <returns>True for inline-safe image types.</returns>
    public static bool IsImage(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && CanDisplayInline(contentType);
}
