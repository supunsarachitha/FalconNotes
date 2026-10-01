using FalconNotes.Core.Startup;
using FalconNotes.Core.Storage;

namespace FalconNotes.UI.State;

/// <summary>
/// Runs the start-up sequence once, before the first screen (docs/02, Start-up sequence). Phase 0 covers the key and
/// the database; Phase 3 adds migrations, maintenance, the profile and the routes to Welcome and Lock.
/// </summary>
/// <param name="startup">The key and database check.</param>
public sealed class AppBootstrapper(DatabaseStartup startup)
{
    private Task<StartupOutcome>? _run;

    /// <summary>The open database, once the outcome is <see cref="StartupOutcome.Ready"/>.</summary>
    public Database? Database { get; private set; }

    /// <summary>Starts the sequence, or returns the run already under way.</summary>
    /// <returns>What the check found.</returns>
    public Task<StartupOutcome> StartAsync() => _run ??= RunAsync();

    private async Task<StartupOutcome> RunAsync()
    {
        var (outcome, database) = await startup.RunAsync();
        Database = database;
        return outcome;
    }
}
