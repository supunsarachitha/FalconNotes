namespace FalconNotes.Core.Platform;

/// <summary>Opens a file in the app the system uses for its type (docs/07, Attachments: Open).</summary>
public interface IFileOpener
{
    /// <summary>Opens a decrypted copy kept in the cache (<c>cache/open/</c>).</summary>
    /// <param name="path">The copy's full path.</param>
    /// <param name="contentType">The file's type.</param>
    /// <returns>True when an app opened it; false when no app can.</returns>
    Task<bool> OpenAsync(string path, string contentType);
}
