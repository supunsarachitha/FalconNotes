using FalconNotes.Core.Platform;

namespace FalconNotes.UI.Tests.Fakes;

/// <summary>Records calls to share a file, instead of showing the real system share sheet.</summary>
public sealed class FakeShare : IShare
{
    /// <summary>Each call's path and content type, in order.</summary>
    public List<(string Path, string ContentType)> Shared { get; } = [];

    /// <inheritdoc />
    public Task ShareFileAsync(string path, string contentType)
    {
        Shared.Add((path, contentType));
        return Task.CompletedTask;
    }
}
