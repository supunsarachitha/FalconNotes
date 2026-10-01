namespace FalconNotes.App;

/// <summary>The MAUI application: one window holding <see cref="MainPage"/>.</summary>
public partial class App : Application
{
    /// <summary>Creates the application.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage()) { Title = "Falcon Notes" };
}
