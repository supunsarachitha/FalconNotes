using Android.App;
using Android.Content.PM;
using Android.Content;
using Android.OS;
using FalconNotes.App.Services;

namespace FalconNotes.App;

/// <summary>The app's only activity.</summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if DEBUG
        DevLaunch.StartPath = Intent?.GetStringExtra("route");
#endif
        base.OnCreate(savedInstanceState);
    }

    /// <inheritdoc />
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        AndroidFileSaver.OnActivityResult(requestCode, resultCode, data);
    }
}
