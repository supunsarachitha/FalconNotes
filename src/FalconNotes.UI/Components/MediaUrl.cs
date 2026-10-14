using FalconNotes.Core.Domain;
using FalconNotes.Core.Stickers;

namespace FalconNotes.UI.Components;

/// <summary>Builds the URL the media handler answers (docs/02, Serving attachments to the WebView).</summary>
public static class MediaUrl
{
    /// <summary>The URL for an attachment's own file, relative to the page.</summary>
    /// <param name="attachment">The attachment.</param>
    /// <returns>The URL; the revision keeps a replaced file from being served from a cached response.</returns>
    public static string For(Attachment attachment) => $"/_media/{attachment.Id:D}?v={attachment.Revision}";

    /// <summary>The URL for a sticker in a pack on the device, for the sticker picker (docs/04, Stickers).</summary>
    /// <param name="pack">Its pack.</param>
    /// <param name="sticker">The sticker.</param>
    /// <returns>The URL; the time the pack was added keeps a replaced pack from being shown from a cached response.</returns>
    public static string For(StickerPack pack, Sticker sticker) => $"/_stickers/{pack.Id}/{sticker.File}?v={pack.Added}";
}
