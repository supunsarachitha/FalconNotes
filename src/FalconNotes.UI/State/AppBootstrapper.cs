using FalconNotes.Core.Maintenance;
using FalconNotes.Core.Startup;
using FalconNotes.Core.Storage;

namespace FalconNotes.UI.State;

/// <summary>
/// Runs the start-up sequence once, before the first screen (docs/02, Start-up sequence): the key, the database and
/// its migrations, then maintenance in the background, hourly. Phase 3 adds the profile and the routes to Welcome and
/// Lock.
/// </summary>
/// <param name="startup">The key and database check.</param>
/// <param name="maintenance">The trash purge and clean-up.</param>
/// <param name="time">The clock, for the hourly timer.</param>
public sealed class AppBootstrapper(DatabaseStartup startup, StartupTasks maintenance, TimeProvider time)
{
    private readonly CancellationTokenSource _stopping = new();

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
        if (outcome == StartupOutcome.Ready)
        {
            // Not awaited: maintenance must never hold up the first screen.
            _ = Task.Run(() => maintenance.RunPeriodicallyAsync(time, _stopping.Token));
        }

        return outcome;
    }
}
