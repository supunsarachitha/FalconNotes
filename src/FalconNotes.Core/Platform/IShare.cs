namespace FalconNotes.Core.Platform;

/// <summary>
/// The system share sheet, for the image viewer's Share button on Android (docs/07, Attachments; docs/12, Share).
/// </summary>
public interface IShare
{
    /// <summary>Hands a decrypted copy of a file to the system share sheet.</summary>
    /// <param name="path">The copy's full path.</param>
    /// <param name="contentType">The file's type.</param>
    /// <returns>A task that completes once the share sheet has the file (not once the user has finished with it).</returns>
    Task ShareFileAsync(string path, string contentType);
}
