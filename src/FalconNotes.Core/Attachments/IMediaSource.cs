namespace FalconNotes.Core.Attachments;

/// <summary>An attachment opened for the media handler: its decrypted content and type.</summary>
/// <param name="Content">The decrypted content; seekable, with a known length. The handler disposes it.</param>
/// <param name="ContentType">The attachment's type.</param>
public sealed record MediaFile(Stream Content, string ContentType);

/// <summary>Finds attachments for the media handler (<c>/_media/{id}</c>).</summary>
public interface IMediaSource
{
    /// <summary>Opens an attachment's decrypted content.</summary>
    /// <param name="id">The attachment's ID.</param>
    /// <returns>The file, or null when there is no such attachment.</returns>
    Task<MediaFile?> OpenAsync(Guid id);
}
