using FalconNotes.UI.State;

namespace FalconNotes.App;

/// <summary>The MAUI application: one window holding <see cref="MainPage"/>.</summary>
/// <param name="services">Creates the main page with its dependencies.</param>
public partial class App(IServiceProvider services) : Application
{
    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(services.GetRequiredService<MainPage>()) { Title = "Falcon Notes" };

        // The app lock's background timer (docs/03, App lock): Stopped/Resumed, not Activated/Deactivated, so a
        // transient system dialog (the picker, a permission prompt) does not count as leaving the app.
        var appLock = services.GetRequiredService<AppLockState>();
        window.Stopped += (_, _) => appLock.OnBackgrounded();
        window.Resumed += (_, _) => appLock.OnForegrounded();
        return window;
    }
}
