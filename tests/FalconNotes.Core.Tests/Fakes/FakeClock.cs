namespace FalconNotes.Core.Tests.Fakes;

/// <summary>A clock that stands still until moved.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    /// <summary>
    /// How far each reading moves the clock on; zero by default. Rows stamped in a row (a note's files on restore)
    /// then get distinct times, as on a real clock, and keep their order instead of falling back to their random IDs.
    /// </summary>
    public TimeSpan Step { get; set; }

    /// <summary>The device's time zone, for tests whose result depends on it; the machine's own when not set.</summary>
    public TimeZoneInfo? Zone { get; set; }

    public override TimeZoneInfo LocalTimeZone => Zone ?? base.LocalTimeZone;

    public override DateTimeOffset GetUtcNow()
    {
        var now = Now;
        Now += Step;
        return now;
    }

    public void Advance(TimeSpan by) => Now += by;
}
