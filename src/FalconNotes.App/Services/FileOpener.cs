using FalconNotes.Core.Platform;

namespace FalconNotes.App.Services;

/// <summary><see cref="IFileOpener"/> on MAUI's <c>Launcher</c> (Android: through a <c>FileProvider</c> URI).</summary>
public sealed class FileOpener : IFileOpener
{
    /// <inheritdoc />
    public Task<bool> OpenAsync(string path, string contentType) =>
        Launcher.Default.OpenAsync(new OpenFileRequest(Path.GetFileName(path), new ReadOnlyFile(path, contentType)));
}
