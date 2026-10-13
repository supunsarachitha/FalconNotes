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

    private IJSRuntime? _page;
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
        Use(js);
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
    public async Task<IJSObjectReference> ModuleAsync(IJSRuntime js)
    {
        Use(js);
        return _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/FalconNotes.UI/js/appearance.js");
    }

    /// <summary>
    /// Starts afresh for a new page. This service lives as long as the process, but the page does not: Android
    /// destroys the activity when Back leaves the app's first page, or to reclaim it, and the next start is a new
    /// WebView with its own JS runtime. The module of the page that is gone reaches nothing, and what was applied
    /// to that page says nothing about this one, whose window has its own status bar too. Kept, they left the new
    /// page without its safe-area insets, so the drawer lay under the status bar (found by the owner on a Pixel 8
    /// Pro, 2026-10-13; docs/12, Edge to edge).
    /// </summary>
    private void Use(IJSRuntime js)
    {
        if (!ReferenceEquals(_page, js))
        {
            _page = js;
            _module = null;
            _applied = null;
        }
    }
}
