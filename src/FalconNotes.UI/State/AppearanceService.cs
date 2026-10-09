using FalconNotes.Core.Domain;
using FalconNotes.Core.Platform;
using Microsoft.JSInterop;

namespace FalconNotes.UI.State;

/// <summary>
/// Applies the theme, accent and background (docs/06, Light and dark; Backgrounds): resolves System from the device,
/// sets the page's classes, accent and background through <c>appearance.js</c>, and makes the native chrome match.
/// Port of <c>lib/appearance.ts</c>; the background is this app's own.
/// </summary>
/// <param name="theme">The device's theme and the native chrome.</param>
/// <param name="state">The preferences.</param>
public sealed class AppearanceService(IThemeSource theme, AppState state)
{
    /// <summary>
    /// Each accent's 600 shade, for the swatches in Settings → Appearance (docs/06; <c>ACCENT_COLORS</c> in
    /// <c>lib/appearance.ts</c>).
    /// </summary>
    public static readonly IReadOnlyDictionary<Accent, string> Swatches = new Dictionary<Accent, string>
    {
        [Accent.Falcon] = "#8f1d21",
        [Accent.Ocean] = "#1d4ed8",
        [Accent.Forest] = "#166534",
        [Accent.Teal] = "#115e59",
        [Accent.Plum] = "#6b21a8",
        [Accent.Amber] = "#9a3412",
        [Accent.Slate] = "#334155",
    };

    private IJSObjectReference? _module;
    private (bool Dark, Accent Accent, Background Background)? _applied;

    /// <summary>Whether the app shows dark now.</summary>
    public bool IsDark => state.Preferences.Theme switch
    {
        Theme.Dark => true,
        Theme.Light => false,
        _ => theme.DeviceIsDark,
    };

    /// <summary>
    /// Applies the appearance to the page and the native chrome. The router calls this after every change to the
    /// preferences or the device's theme (as <c>useAppearance</c> does in the web app), so it does nothing when the
    /// theme, accent and background are the ones already shown.
    /// </summary>
    /// <param name="js">The page's JS runtime.</param>
    /// <returns>A task that completes when it is applied.</returns>
    public async Task ApplyAsync(IJSRuntime js)
    {
        var wanted = (Dark: IsDark, state.Preferences.Accent, state.Preferences.Background);
        if (_applied == wanted)
        {
            return;
        }

        _applied = wanted;
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/FalconNotes.UI/js/appearance.js");
        await _module.InvokeVoidAsync("apply", wanted.Dark, wanted.Accent.ToString().ToLowerInvariant(), BackgroundName(wanted.Background));
        theme.ApplyChrome(wanted.Dark);
    }

    /// <summary>
    /// A background's name as the page and <c>app.css</c> know it (<c>data-background</c>, and the tiles'
    /// file names): lower case, "none" for the plain page.
    /// </summary>
    /// <param name="background">The background.</param>
    /// <returns>The name.</returns>
    public static string BackgroundName(Background background) => background.ToString().ToLowerInvariant();

    /// <summary>The module, for the shell's other page-level calls (insets, scrolling).</summary>
    /// <param name="js">The page's JS runtime.</param>
    /// <returns>The module.</returns>
    public async Task<IJSObjectReference> ModuleAsync(IJSRuntime js) =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/FalconNotes.UI/js/appearance.js");
}
