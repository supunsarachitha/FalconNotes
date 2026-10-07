using FalconNotes.Core.Backup.Restore;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
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
    private UiTestApp(TestApp core, AppState state, Toasts toasts, FakeFilePicker picker)
    {
        Core = core;
        State = state;
        Toasts = toasts;
        Picker = picker;
    }

    /// <summary>The underlying Core services and database.</summary>
    public TestApp Core { get; }

    /// <summary>The profile and preferences, as every screen reads them.</summary>
    public AppState State { get; }

    /// <summary>The notifications raised during the test.</summary>
    public Toasts Toasts { get; }

    /// <summary>Answers restore's file picker; queue the files a test wants chosen.</summary>
    public FakeFilePicker Picker { get; }

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
        var picker = new FakeFilePicker();

        services.AddSingleton(core.Notes);
        services.AddSingleton(core.Labels);
        services.AddSingleton(core.Preferences);
        services.AddSingleton(core.Profile);
        services.AddSingleton(core.Feed);
        services.AddSingleton<TimeProvider>(core.Clock);
        services.AddSingleton(state);
        services.AddSingleton(toasts);
        services.AddSingleton<Core.Platform.IFilePicker>(picker);
        services.AddSingleton<Core.Platform.IAppInfo>(new FakeAppInfo());
        services.AddSingleton(new RestoreReader(core.Directories, core.Clock));
        services.AddSingleton(new RestoreRunner(core.Storage, core.Attachments, core.Feed, core.Clock));
        services.AddSingleton(new KeyLostRecovery(new DeviceKeyStore(core.Secrets), core.Directories, core.Clock));

        return new UiTestApp(core, state, toasts, picker);
    }

    /// <summary>Posts a note (docs/04): used to give the calendar, tag counts and lists something to show.</summary>
    /// <param name="content">The note's text.</param>
    /// <param name="kind">The kind of note.</param>
    /// <returns>The created note.</returns>
    public Task<Core.Domain.Note> PostAsync(string content, NoteKind kind = NoteKind.Note) => Core.Notes.CreateAsync(content, kind);

    /// <inheritdoc />
    public void Dispose() => Core.Dispose();
}
