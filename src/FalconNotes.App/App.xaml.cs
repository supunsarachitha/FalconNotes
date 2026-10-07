namespace FalconNotes.App;

/// <summary>The MAUI application: one window holding <see cref="MainPage"/>.</summary>
/// <param name="services">Creates the main page with its dependencies.</param>
public partial class App(IServiceProvider services) : Application
{
    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(services.GetRequiredService<MainPage>()) { Title = "Falcon Notes" };
}
