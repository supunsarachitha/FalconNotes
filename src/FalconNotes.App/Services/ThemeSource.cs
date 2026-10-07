using FalconNotes.Core.Platform;

namespace FalconNotes.App.Services;

/// <summary>
/// <see cref="IThemeSource"/> on MAUI's <c>Application.Current</c> theme (docs/06, Light and dark). Native chrome is
/// set per platform; only Android is implemented so far (docs/10, Phase 3 is Android first).
/// </summary>
public sealed class ThemeSource : IThemeSource, IDisposable
{
    /// <summary>Subscribes to the device's theme changes.</summary>
    public ThemeSource()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }
    }

    /// <inheritdoc />
    public bool DeviceIsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    /// <inheritdoc />
    public event Action? DeviceThemeChanged;

    /// <inheritdoc />
    public void ApplyChrome(bool dark)
    {
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window is { } window)
        {
            var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
            if (controller is not null)
            {
                controller.AppearanceLightStatusBars = !dark;
                controller.AppearanceLightNavigationBars = !dark;
            }
        }
#endif
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) => DeviceThemeChanged?.Invoke();

    /// <inheritdoc />
    public void Dispose()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
        }
    }
}
