using CommunityToolkit.Maui;
using FalconNotes.App.Services;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Platform;
using FalconNotes.Core.Startup;
using FalconNotes.UI.State;
using Microsoft.Extensions.Logging;
#if DEBUG
using FalconNotes.Core.Attachments;
using FalconNotes.UI.Dev;
#endif

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

        // Platform services (docs/02, Platform services).
        builder.Services.AddSingleton<ISecretStore, SecretStore>();
        builder.Services.AddSingleton<IAppDirectories, AppDirectories>();
        builder.Services.AddSingleton<Core.Platform.IFilePicker, Services.FilePicker>();
        builder.Services.AddSingleton<Core.Platform.IFileSaver, Services.FileSaver>();
        builder.Services.AddSingleton<IFileOpener, FileOpener>();

        // Core and start-up.
        builder.Services.AddSingleton<DeviceKeyStore>();
        builder.Services.AddSingleton<DatabaseStartup>();
        builder.Services.AddSingleton<AppBootstrapper>();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        // Phase 0 spikes: the media library stands in for the attachment store until Phase 1.
        builder.Services.AddSingleton<SpikeMediaLibrary>();
        builder.Services.AddSingleton<IMediaSource>(sp => sp.GetRequiredService<SpikeMediaLibrary>());
        builder.Services.AddSingleton<MediaHandler>();
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
