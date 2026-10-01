using System.Globalization;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Text;

// Ported from the "dates" cases of web/lib/titles.test.ts, plus the C#-specific day ranges.
public class DateFormatsTests
{
    [Fact]
    public void Formats_every_offered_date_format()
    {
        var date = new DateOnly(2026, 9, 7); // Monday, 7 September 2026

        Assert.Equal(
            [
                "2026-09-07", "07/09/2026", "09/07/2026", "07.09.2026", "7 Sep 2026", "Sep 7, 2026",
                "Monday, 7 September 2026", "Monday, September 7, 2026",
            ],
            DateFormats.All.Select(format => DateFormats.Format(date, format)));
    }

    [Fact]
    public void Names_the_local_calendar_day()
    {
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

        Assert.Equal("2026-01-05", DateFormats.LocalKey(new DateTimeOffset(2026, 1, 5, 22, 59, 0, TimeSpan.Zero), paris));
        Assert.Equal("2026-01-06", DateFormats.LocalKey(new DateTimeOffset(2026, 1, 5, 23, 0, 0, TimeSpan.Zero), paris));
    }

    [Theory]
    [InlineData("2026-09-07", true)]
    [InlineData("2026-02-29", false)]
    [InlineData("2026-9-7", false)]
    [InlineData("२०२६-०९-०७", false)] // Devanagari digits are not ASCII digits
    [InlineData("not a day", false)]
    public void Reads_only_real_dates(string key, bool valid) => Assert.Equal(valid, DateFormats.ParseKey(key) is not null);

    [Fact]
    public void A_day_spans_its_local_midnights_even_across_daylight_saving_changes()
    {
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

        var (start, end) = DateFormats.DayRange(new DateOnly(2026, 3, 29), paris); // clocks go forward: a 23-hour day

        Assert.Equal(new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void A_missing_midnight_starts_the_day_at_the_first_valid_minute()
    {
        var santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago"); // DST starts at midnight in September

        var (start, _) = DateFormats.DayRange(new DateOnly(2026, 9, 6), santiago);

        Assert.Equal(new DateTime(2026, 9, 6, 4, 0, 0, DateTimeKind.Utc), start); // 01:00 local, UTC-3
    }

    [Fact]
    public void The_week_starts_on_the_chosen_day_or_the_culture_s()
    {
        Assert.Equal(DayOfWeek.Monday, DateFormats.WeekStartDay(WeekStart.Monday, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(DayOfWeek.Sunday, DateFormats.WeekStartDay(WeekStart.Auto, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(DayOfWeek.Monday, DateFormats.WeekStartDay(WeekStart.Auto, CultureInfo.GetCultureInfo("en-GB")));
    }
}
