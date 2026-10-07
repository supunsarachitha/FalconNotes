using FalconNotes.Core.Domain;
using FalconNotes.Core.Platform;
using Microsoft.JSInterop;

namespace FalconNotes.UI.State;

/// <summary>
/// Applies the theme and accent (docs/06, Light and dark): resolves System from the device, sets the page's classes
/// and accent through <c>appearance.js</c>, and makes the native chrome match. Port of <c>lib/appearance.ts</c>.
/// </summary>
/// <param name="theme">The device's theme and the native chrome.</param>
/// <param name="state">The preferences.</param>
public sealed class AppearanceService(IThemeSource theme, AppState state)
{
    private IJSObjectReference? _module;

    /// <summary>Whether the app shows dark now.</summary>
    public bool IsDark => state.Preferences.Theme switch
    {
        Theme.Dark => true,
        Theme.Light => false,
        _ => theme.DeviceIsDark,
    };

    /// <summary>Applies the appearance to the page and the native chrome.</summary>
    /// <param name="js">The page's JS runtime.</param>
    /// <returns>A task that completes when it is applied.</returns>
    public async Task ApplyAsync(IJSRuntime js)
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/FalconNotes.UI/js/appearance.js");
        var dark = IsDark;
        await _module.InvokeVoidAsync("apply", dark, state.Preferences.Accent.ToString().ToLowerInvariant());
        theme.ApplyChrome(dark);
    }

    /// <summary>The module, for the shell's other page-level calls (insets, scrolling).</summary>
    /// <param name="js">The page's JS runtime.</param>
    /// <returns>The module.</returns>
    public async Task<IJSObjectReference> ModuleAsync(IJSRuntime js) =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/FalconNotes.UI/js/appearance.js");
}
