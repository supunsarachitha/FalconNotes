namespace FalconNotes.Core.Platform;

/// <summary>The system clipboard (docs/02, Platform services), for "Copy text" on a note.</summary>
public interface IClipboard
{
    /// <summary>Copies text to the clipboard.</summary>
    /// <param name="text">The text.</param>
    /// <returns>True when it was copied; false when copying is not allowed here.</returns>
    Task<bool> SetTextAsync(string text);
}
