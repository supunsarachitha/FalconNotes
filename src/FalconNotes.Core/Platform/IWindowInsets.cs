namespace FalconNotes.Core.Platform;

/// <summary>Space taken by system bars and cut-outs on each side, in CSS pixels.</summary>
/// <param name="Top">The status bar.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Bottom">The navigation bar or gesture area.</param>
/// <param name="Left">The left edge.</param>
public readonly record struct Insets(double Top, double Right, double Bottom, double Left);

/// <summary>
/// The window's safe-area insets. Android's WebView reports <c>env(safe-area-inset-*)</c> as 0 when drawing edge to edge
/// (Phase 0 spike S5), so the app measures them natively and hands them to the page as CSS variables (docs/12).
/// </summary>
public interface IWindowInsets
{
    /// <summary>The insets now.</summary>
    Insets Current { get; }

    /// <summary>Raised when they change (rotation, keyboard, split screen); may run on any thread.</summary>
    event Action? Changed;
}
