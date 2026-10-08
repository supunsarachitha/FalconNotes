using CommunityToolkit.Maui;
using FalconNotes.App.Services;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Backup.Restore;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Events;
using FalconNotes.Core.Labels;
using FalconNotes.Core.Maintenance;
using FalconNotes.Core.Markdown;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Settings;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Platform;
using FalconNotes.Core.Startup;
using FalconNotes.UI.State;
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

        // Platform services (docs/02, Platform services).
        builder.Services.AddSingleton<ISecretStore, SecretStore>();
        builder.Services.AddSingleton<IAppDirectories, AppDirectories>();
        builder.Services.AddSingleton<Core.Platform.IFilePicker, Services.FilePicker>();
        #if ANDROID
        builder.Services.AddSingleton<Core.Platform.IFileSaver, AndroidFileSaver>();
#else
        builder.Services.AddSingleton<Core.Platform.IFileSaver, Services.FileSaver>();
#endif
        builder.Services.AddSingleton<IFileOpener, FileOpener>();
        builder.Services.AddSingleton<Core.Platform.IClipboard, Services.SystemClipboard>();
        builder.Services.AddSingleton<Core.Platform.IAppInfo, Services.AppInfo>();
        builder.Services.AddSingleton<IThemeSource, Services.ThemeSource>();
#if ANDROID
        builder.Services.AddSingleton<IWindowInsets, Services.AndroidWindowInsets>();
        builder.Services.AddSingleton<Core.Platform.IAppLock, Services.AndroidAppLock>();
        builder.Services.AddSingleton<Core.Platform.IShare, Services.AndroidShare>();
#endif

        // Core and start-up (docs/02, Runtime model): stateless services are singletons.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ChangeFeed>();
        builder.Services.AddSingleton<StorageContext>();
        builder.Services.AddSingleton<DeviceKeyStore>();
        builder.Services.AddSingleton<DatabaseBackups>();
        builder.Services.AddSingleton<DatabaseStartup>();
        builder.Services.AddSingleton<AttachmentStore>();
        builder.Services.AddSingleton<AttachmentService>();
        builder.Services.AddSingleton<IMediaSource>(sp => sp.GetRequiredService<AttachmentService>());
        builder.Services.AddSingleton<NoteService>();
        builder.Services.AddSingleton<DailyNotes>();
        builder.Services.AddSingleton<LabelService>();
        builder.Services.AddSingleton<PreferencesService>();
        builder.Services.AddSingleton<ProfileService>();
        builder.Services.AddSingleton<AppLockService>();
        builder.Services.AddSingleton<MarkdownRenderer>();
        builder.Services.AddSingleton<AttachmentCleanup>();
        builder.Services.AddSingleton<StartupTasks>();
        builder.Services.AddSingleton<AppBootstrapper>();
        builder.Services.AddSingleton<MediaHandler>();
        builder.Services.AddSingleton<NoteExporter>();
        builder.Services.AddSingleton<ExportService>();
        builder.Services.AddSingleton<RestoreReader>();
        builder.Services.AddSingleton<RestoreRunner>();
        builder.Services.AddSingleton<EraseAllData>();
        builder.Services.AddSingleton<KeyLostRecovery>();

        // UI-wide state (docs/02, Runtime model): lives as long as the BlazorWebView, so it is a singleton too.
        builder.Services.AddSingleton<AppState>();
        builder.Services.AddSingleton<AppLockState>();
        builder.Services.AddSingleton<Toasts>();
        builder.Services.AddSingleton<AppearanceService>();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
