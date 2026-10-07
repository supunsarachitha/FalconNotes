using FalconNotes.Core.Platform;

namespace FalconNotes.UI.Tests.Fakes;

/// <summary>Records what was asked to be opened, instead of handing it to a real system app.</summary>
public sealed class FakeFileOpener : IFileOpener
{
    /// <summary>Each call's path and content type, in order.</summary>
    public List<(string Path, string ContentType)> Opened { get; } = [];

    /// <summary>Whether the next call succeeds; an app refusing the file (no viewer installed) is false.</summary>
    public bool Succeeds { get; set; } = true;

    /// <inheritdoc />
    public Task<bool> OpenAsync(string path, string contentType)
    {
        Opened.Add((path, contentType));
        return Task.FromResult(Succeeds);
    }
}
