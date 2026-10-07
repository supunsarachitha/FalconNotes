namespace FalconNotes.App.Services;

/// <summary><see cref="Core.Platform.IClipboard"/> on MAUI's <c>Clipboard</c> (docs/02: same on every platform).</summary>
public sealed class SystemClipboard : Core.Platform.IClipboard
{
    /// <inheritdoc />
    public async Task<bool> SetTextAsync(string text)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
