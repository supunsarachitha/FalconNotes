using FalconNotes.Core.Platform;

namespace FalconNotes.UI.Tests.Fakes;

/// <summary>A device theme a test sets, recording what the native chrome was told.</summary>
public sealed class FakeThemeSource : IThemeSource
{
    /// <inheritdoc />
    public bool DeviceIsDark { get; set; }

    /// <summary>Each call to <see cref="ApplyChrome"/>, in order.</summary>
    public List<bool> Chrome { get; } = [];

    /// <inheritdoc />
    public event Action? DeviceThemeChanged;

    /// <inheritdoc />
    public void ApplyChrome(bool dark) => Chrome.Add(dark);

    /// <summary>Changes the device's setting, as the system does.</summary>
    /// <param name="dark">Whether the device is now dark.</param>
    public void SetDevice(bool dark)
    {
        DeviceIsDark = dark;
        DeviceThemeChanged?.Invoke();
    }
}
