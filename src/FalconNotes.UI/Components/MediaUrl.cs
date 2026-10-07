using FalconNotes.Core.Domain;

namespace FalconNotes.UI.Components;

/// <summary>Builds the URL the media handler answers (docs/02, Serving attachments to the WebView).</summary>
public static class MediaUrl
{
    /// <summary>The URL for an attachment's own file, relative to the page.</summary>
    /// <param name="attachment">The attachment.</param>
    /// <returns>The URL; the revision keeps a replaced file from being served from a cached response.</returns>
    public static string For(Attachment attachment) => $"/_media/{attachment.Id:D}?v={attachment.Revision}";
}
