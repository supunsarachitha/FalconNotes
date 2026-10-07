using FalconNotes.Core.Platform;

namespace FalconNotes.UI.Tests.Fakes;

/// <summary>Records what was copied, instead of touching the real system clipboard.</summary>
public sealed class FakeClipboard : IClipboard
{
    /// <summary>Each call's text, in order.</summary>
    public List<string> Copied { get; } = [];

    /// <summary>Whether the next call succeeds; a context that disallows clipboard access is false.</summary>
    public bool Succeeds { get; set; } = true;

    /// <inheritdoc />
    public Task<bool> SetTextAsync(string text)
    {
        if (Succeeds)
        {
            Copied.Add(text);
        }

        return Task.FromResult(Succeeds);
    }
}
