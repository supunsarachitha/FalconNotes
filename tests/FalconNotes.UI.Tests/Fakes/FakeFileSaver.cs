using FalconNotes.Core.Platform;

namespace FalconNotes.UI.Tests.Fakes;

/// <summary>Records what was asked to be saved, instead of showing a real "save as" dialog.</summary>
public sealed class FakeFileSaver : IFileSaver
{
    /// <summary>Each call's suggested name and the bytes read from the content, in order.</summary>
    public List<(string SuggestedName, byte[] Bytes)> Saved { get; } = [];

    /// <summary>Whether the next call succeeds; the user cancelling the dialog is false.</summary>
    public bool Succeeds { get; set; } = true;

    /// <inheritdoc />
    public async Task<bool> SaveAsync(string suggestedName, Stream content, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Saved.Add((suggestedName, buffer.ToArray()));
        return Succeeds;
    }
}
