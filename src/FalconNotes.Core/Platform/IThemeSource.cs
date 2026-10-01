namespace FalconNotes.Core.Platform;

/// <summary>
/// The device's light or dark setting, and the native parts of the window that follow the app's theme (docs/06, Light
/// and dark): Android's status and navigation bars, the page background, desktop title bars.
/// </summary>
public interface IThemeSource
{
    /// <summary>Whether the device is set to dark.</summary>
    bool DeviceIsDark { get; }

    /// <summary>Raised when the device's setting changes; may run on any thread.</summary>
    event Action? DeviceThemeChanged;

    /// <summary>Makes the native chrome light or dark, to match the page.</summary>
    /// <param name="dark">Whether the app shows dark.</param>
    void ApplyChrome(bool dark);
}
