using Android.App;
using Android.Content.PM;
using Android.Content;
using Android.OS;
using Android.Views;
using FalconNotes.App.Services;
using FalconNotes.UI.State;

namespace FalconNotes.App;

/// <summary>The app's only activity.</summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private AppLockState? _appLock;

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if DEBUG
        DevLaunch.StartPath = Intent?.GetStringExtra("route");
#endif
        base.OnCreate(savedInstanceState);

        // While the app lock is on, hide this window from screenshots and the recent-apps thumbnail (docs/03, App
        // lock; docs/12, Lock privacy). Kept in step with the setting for as long as the activity lives.
        _appLock = IPlatformApplication.Current?.Services.GetService<AppLockState>();
        if (_appLock is not null)
        {
            _appLock.Changed += OnAppLockChanged;
            ApplySecureFlag();
        }
    }

    private void OnAppLockChanged() => RunOnUiThread(ApplySecureFlag);

    private void ApplySecureFlag()
    {
        if (_appLock is null || Window is null)
        {
            return;
        }

        if (_appLock.Settings.Enabled)
        {
            Window.AddFlags(WindowManagerFlags.Secure);
        }
        else
        {
            Window.ClearFlags(WindowManagerFlags.Secure);
        }
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        if (_appLock is not null)
        {
            _appLock.Changed -= OnAppLockChanged;
        }

        base.OnDestroy();
    }

    /// <inheritdoc />
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        AndroidFileSaver.OnActivityResult(requestCode, resultCode, data);
    }
}
