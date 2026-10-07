#if DEBUG
namespace FalconNotes.App;

/// <summary>
/// Debug builds only: a start route passed by <c>adb shell am start … --es route /dev/spikes?auto=1</c>, so the Phase 0
/// spikes can run without tapping through the app.
/// </summary>
public static class DevLaunch
{
    /// <summary>The route to open first, or null for Home.</summary>
    public static string? StartPath { get; set; }
}
#endif
