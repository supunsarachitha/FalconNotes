namespace FalconNotes.App.Services;

/// <summary><see cref="Core.Platform.IShare"/> on MAUI's <c>Share</c> (Android: through a <c>FileProvider</c> URI; docs/12, Share).</summary>
public sealed class AndroidShare : Core.Platform.IShare
{
    /// <inheritdoc />
    public Task ShareFileAsync(string path, string contentType) =>
        Share.Default.RequestAsync(new ShareFileRequest { File = new ShareFile(path, contentType) });
}
