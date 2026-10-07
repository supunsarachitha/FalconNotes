using AndroidX.Core.View;
using CoreInsets = FalconNotes.Core.Platform.Insets;

namespace FalconNotes.App.Services;

/// <summary>
/// <see cref="Core.Platform.IWindowInsets"/> measured from the decor view's system-bar insets (docs/12, Edge to
/// edge): the WebView itself reports <c>env(safe-area-inset-*)</c> as 0 (Phase 0 spike S5).
/// </summary>
public sealed class AndroidWindowInsets : Core.Platform.IWindowInsets
{
    private CoreInsets _current;

    /// <summary>Attaches the listener to the current activity's decor view, if one exists yet.</summary>
    public AndroidWindowInsets()
    {
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.DecorView is { } decorView)
        {
            ViewCompat.SetOnApplyWindowInsetsListener(decorView, new Listener(this));
        }
    }

    /// <inheritdoc />
    public CoreInsets Current => _current;

    /// <inheritdoc />
    public event Action? Changed;

    private void Update(CoreInsets insets)
    {
        if (!insets.Equals(_current))
        {
            _current = insets;
            Changed?.Invoke();
        }
    }

    private sealed class Listener(AndroidWindowInsets owner) : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(global::Android.Views.View? v, WindowInsetsCompat? insets)
        {
            if (v is null || insets is null)
            {
                return insets;
            }

            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());
            var density = v.Resources?.DisplayMetrics?.Density ?? 1f;
            if (bars is not null)
            {
                owner.Update(new CoreInsets(bars.Top / density, bars.Right / density, bars.Bottom / density, bars.Left / density));
            }
            return insets;
        }
    }
}
