using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace FalconNotes.App;

/// <summary>Builds the MAUI app: services, Blazor and the platform implementations of Core's interfaces.</summary>
public static class MauiProgram
{
    /// <summary>Creates the app. Called once by each platform's entry point.</summary>
    /// <returns>The configured app.</returns>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
