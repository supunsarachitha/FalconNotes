using Android.App;
using Android.Runtime;

namespace FalconNotes.App;

/// <summary>The Android application object, which creates the MAUI app.</summary>
[Application]
public class MainApplication : MauiApplication
{
    /// <summary>Called by the Android runtime.</summary>
    /// <param name="handle">The Java object's handle.</param>
    /// <param name="ownership">Who owns <paramref name="handle"/>.</param>
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
