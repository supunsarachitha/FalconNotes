using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Text;

// Ported from web/lib/format.test.ts, with the exact English the app writes.
public class FormatTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static string Relative(int year, int month, int day, int hour, int minute, int second = 0) =>
        RelativeTime.Format(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero), Now, Utc);

    [Fact]
    public void Says_just_now_for_the_last_few_seconds() => Assert.Equal("just now", Relative(2026, 9, 28, 11, 59, 40));

    [Fact]
    public void Uses_minutes_hours_and_days_for_recent_notes()
    {
        Assert.Equal("5 minutes ago", Relative(2026, 9, 28, 11, 55));
        Assert.Equal("1 minute ago", Relative(2026, 9, 28, 11, 59));
        Assert.Equal("3 hours ago", Relative(2026, 9, 28, 9, 0));
        Assert.Equal("yesterday", Relative(2026, 9, 27, 12, 0));
        Assert.Equal("3 days ago", Relative(2026, 9, 25, 12, 0));
        Assert.Equal("in 2 hours", Relative(2026, 9, 28, 14, 0));
        Assert.Equal("tomorrow", Relative(2026, 9, 29, 12, 0));
    }

    [Fact]
    public void Switches_to_a_date_after_a_week_adding_the_year_only_when_it_differs()
    {
        Assert.Equal("Sep 1", Relative(2026, 9, 1, 12, 0));
        Assert.Equal("Sep 1, 2024", Relative(2024, 9, 1, 12, 0));
    }

    [Fact]
    public void Writes_absolute_times_with_the_device_clock()
    {
        var time = new DateTimeOffset(2026, 9, 28, 14, 30, 0, TimeSpan.Zero);

        Assert.Equal("Sep 28, 2026, 2:30 PM", RelativeTime.Absolute(time, Utc, use24Hour: false));
        Assert.Equal("Sep 28, 2026, 14:30", RelativeTime.Absolute(time, Utc, use24Hour: true));
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(25L * 1024 * 1024, "25 MB")]
    [InlineData((long)(3.2 * 1024 * 1024 * 1024), "3.2 GB")]
    public void Formats_byte_sizes(long bytes, string expected) => Assert.Equal(expected, Bytes.Format(bytes));
}
