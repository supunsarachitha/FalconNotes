using FalconNotes.Core.Attachments;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Events;
using FalconNotes.Core.Labels;
using FalconNotes.Core.Maintenance;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Settings;
using FalconNotes.Core.Startup;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Tests;

/// <summary>Core's services over a real encrypted database in a temporary folder, with a fixed clock.</summary>
public sealed class TestApp : IDisposable
{
    private readonly TempDirectory _directory = new();

    private TestApp()
    {
        Directories = new FakeDirectories(_directory.Path);
        Store = new AttachmentStore(Directories);
        Notes = new NoteService(Storage, Store, Feed, Clock);
        Attachments = new AttachmentService(Storage, Store, Clock);
        Labels = new LabelService(Storage, Feed, Clock);
        Preferences = new PreferencesService(Storage, Feed);
        Profile = new ProfileService(Storage, Feed, Clock);
        Cleanup = new AttachmentCleanup(Storage, Store, Clock);
    }

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));

    public FakeSecretStore Secrets { get; } = new();

    public FakeDirectories Directories { get; }

    public StorageContext Storage { get; } = new();

    public ChangeFeed Feed { get; } = new();

    public AttachmentStore Store { get; }

    public NoteService Notes { get; }

    public AttachmentService Attachments { get; }

    public LabelService Labels { get; }

    public PreferencesService Preferences { get; }

    public ProfileService Profile { get; }

    public AttachmentCleanup Cleanup { get; }

    public DatabaseStartup Startup => new(new DeviceKeyStore(Secrets), Directories, new DatabaseBackups(Directories, Clock), Storage);

    public static async Task<TestApp> StartAsync()
    {
        var app = new TestApp();
        var (outcome, _) = await app.Startup.RunAsync();
        Assert.Equal(StartupOutcome.Ready, outcome);
        return app;
    }

    /// <summary>
    /// Adds a small file, not yet attached, and moves the clock on one tick. Files added one after another then have
    /// distinct times, as on a real clock, and a note lists them in that order instead of by their random IDs.
    /// </summary>
    public async Task<Domain.Attachment> AddFileAsync(string name = "photo.png", int size = 100, string? type = null)
    {
        var added = await Attachments.AddAsync(new MemoryStream(Enumerable.Range(0, size).Select(i => (byte)i).ToArray()), name, type);
        Clock.Advance(TimeSpan.FromTicks(1));
        return added;
    }

    public void Dispose()
    {
        Storage.Close();
        SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }
}
