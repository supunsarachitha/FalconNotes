using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Backup.Restore;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Markdown;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Settings;
using FalconNotes.Core.Startup;
using FalconNotes.Core.Tests;
using FalconNotes.UI.State;
using FalconNotes.UI.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Core's <see cref="TestApp"/> (a real encrypted database in a temporary folder, with a fixed clock) plus the
/// UI-layer state (docs/02, Runtime model) and platform fakes, registered into a bUnit test's services.
/// </summary>
public sealed class UiTestApp : IDisposable
{
    private UiTestApp(
        TestApp core, AppState state, AppLockState appLock, Toasts toasts, FakeFilePicker picker, FakeFileOpener opener, FakeFileSaver saver,
        FakeClipboard clipboard, FakeShare share)
    {
        Core = core;
        State = state;
        AppLock = appLock;
        Toasts = toasts;
        Picker = picker;
        Opener = opener;
        Saver = saver;
        Clipboard = clipboard;
        Share = share;
    }

    /// <summary>The underlying Core services and database.</summary>
    public TestApp Core { get; }

    /// <summary>The profile and preferences, as every screen reads them.</summary>
    public AppState State { get; }

    /// <summary>The app lock's session state.</summary>
    public AppLockState AppLock { get; }

    /// <summary>The notifications raised during the test.</summary>
    public Toasts Toasts { get; }

    /// <summary>Answers restore's file picker; queue the files a test wants chosen.</summary>
    public FakeFilePicker Picker { get; }

    /// <summary>Records calls to Open a stored file.</summary>
    public FakeFileOpener Opener { get; }

    /// <summary>Records calls to Save a copy of a stored file.</summary>
    public FakeFileSaver Saver { get; }

    /// <summary>Records text copied to the clipboard.</summary>
    public FakeClipboard Clipboard { get; }

    /// <summary>Records files handed to the share sheet.</summary>
    public FakeShare Share { get; }

    /// <summary>
    /// Starts a database, registers it and the UI state into <paramref name="services"/>, and loads <see cref="AppState"/>.
    /// </summary>
    /// <param name="services">The bUnit test's service collection.</param>
    /// <param name="displayName">A name to create the profile with before loading; null leaves no profile (Welcome's case).</param>
    /// <param name="preferences">Preferences to save before loading; null keeps the defaults.</param>
    /// <returns>The started app.</returns>
    public static async Task<UiTestApp> StartAsync(IServiceCollection services, string? displayName = null, Preferences? preferences = null)
    {
        var core = await TestApp.StartAsync();
        if (displayName is not null)
        {
            await core.Profile.CreateAsync(displayName);
        }

        if (preferences is not null)
        {
            await core.Preferences.SaveAsync(preferences);
        }

        var toasts = new Toasts(core.Clock);
        var state = new AppState(core.Preferences, core.Profile, core.Feed, toasts);
        await state.LoadAsync();
        var appLockService = new AppLockService(core.Storage, core.Clock);
        var fakeAppLock = new FakeAppLock();
        var appLock = new AppLockState(appLockService, fakeAppLock, core.Clock);
        await appLock.LoadAsync();
        var picker = new FakeFilePicker();
        var opener = new FakeFileOpener();
        var saver = new FakeFileSaver();
        var clipboard = new FakeClipboard();
        var share = new FakeShare();

        services.AddSingleton(core.Notes);
        services.AddSingleton(new DailyNotes(core.Notes));
        services.AddSingleton(core.Labels);
        services.AddSingleton(core.Attachments);
        services.AddSingleton<MarkdownRenderer>();
        services.AddSingleton(core.Preferences);
        services.AddSingleton(core.Profile);
        services.AddSingleton(core.Feed);
        services.AddSingleton<TimeProvider>(core.Clock);
        services.AddSingleton(state);
        services.AddSingleton(appLockService);
        services.AddSingleton(appLock);
        services.AddSingleton<Core.Platform.IAppLock>(fakeAppLock);
        services.AddSingleton(toasts);
        services.AddSingleton<Core.Platform.IFilePicker>(picker);
        services.AddSingleton<Core.Platform.IAppInfo>(new FakeAppInfo());
        services.AddSingleton<Core.Platform.IAppDirectories>(core.Directories);
        services.AddSingleton<Core.Platform.IFileOpener>(opener);
        services.AddSingleton<Core.Platform.IFileSaver>(saver);
        services.AddSingleton<Core.Platform.IClipboard>(clipboard);
        services.AddSingleton<Core.Platform.IShare>(share);
        services.AddSingleton(new ExportService(
            new NoteExporter(core.Storage, core.Attachments, core.Profile, core.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<NoteExporter>.Instance),
            saver, core.Directories, core.Storage, core.Clock));
        services.AddSingleton(new RestoreReader(core.Directories, core.Clock));
        services.AddSingleton(new RestoreRunner(core.Storage, core.Attachments, core.Feed, core.Clock));
        services.AddSingleton(new KeyLostRecovery(new DeviceKeyStore(core.Secrets), core.Directories, core.Clock));
        services.AddSingleton(new EraseAllData(new DeviceKeyStore(core.Secrets), core.Storage, core.Directories));

        return new UiTestApp(core, state, appLock, toasts, picker, opener, saver, clipboard, share);
    }

    /// <summary>Posts a note (docs/04): used to give the calendar, tag counts and lists something to show.</summary>
    /// <param name="content">The note's text.</param>
    /// <param name="kind">The kind of note.</param>
    /// <returns>The created note.</returns>
    public Task<Core.Domain.Note> PostAsync(string content, NoteKind kind = NoteKind.Note) => Core.Notes.CreateAsync(content, kind);

    /// <inheritdoc />
    public void Dispose() => Core.Dispose();
}
