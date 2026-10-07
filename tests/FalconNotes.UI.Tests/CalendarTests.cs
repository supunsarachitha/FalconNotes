using FalconNotes.Core.Domain;
using FalconNotes.UI.Components;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Port of components/Calendar.test.tsx (docs/11). <c>dayRange</c>/<c>parseDateKey</c> are
/// <see cref="FalconNotes.Core.Text.DateFormats"/>, already covered in Core.Tests, so only the rendering cases come
/// here; "shows the notes of the chosen day on Home" is Phase 4 (Home does not exist yet).
/// </summary>
public class CalendarTests : BunitContext
{
    private async Task<UiTestApp> StartAsync(Preferences? preferences = null) =>
        await UiTestApp.StartAsync(Services, "Maple", preferences);

    /// <summary>A moment that reads as this wall-clock time in the host's own time zone (the calendar's days are
    /// this device's calendar days), so the test does not depend on the host running in UTC.</summary>
    private static DateTimeOffset LocalTime(int year, int month, int day, int hour = 9, int minute = 30)
    {
        var naive = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(naive, TimeZoneInfo.Local.GetUtcOffset(naive));
    }

    [Fact]
    public async Task Marks_the_days_with_notes_and_links_each_day_to_its_notes()
    {
        using var app = await StartAsync();
        app.Core.Clock.Now = LocalTime(2026, 9, 29);
        await app.PostAsync("One"); // today, 2026-09-29: 3 notes
        await app.PostAsync("Two");
        await app.PostAsync("Three");
        app.Core.Clock.Now = LocalTime(2026, 9, 3);
        await app.PostAsync("Four"); // 2026-09-03: 1 note
        app.Core.Clock.Now = LocalTime(2026, 9, 29);

        var cut = Render<Calendar>();

        Assert.Equal("September 2026", cut.Find("h2").TextContent);
        var today = cut.WaitForElement("a[aria-label='Tuesday, September 29, 2026, 3 notes (today)']");
        Assert.Equal("/?day=2026-09-29", today.GetAttribute("href"));
        cut.Find("a[aria-label='Thursday, September 3, 2026, 1 note']");
        cut.Find("a[aria-label='Friday, September 4, 2026']");
    }

    [Fact]
    public async Task Starts_the_week_on_monday_when_chosen()
    {
        // A fresh BunitContext per fact (xUnit): services can only be registered once per context.
        using var app = await StartAsync(new Preferences { WeekStart = WeekStart.Monday });
        app.Core.Clock.Now = LocalTime(2026, 9, 1);
        var cut = Render<Calendar>();

        Assert.Equal("MoTuWeThFrSaSu", cut.Find(".grid-cols-7[aria-hidden=true]").TextContent);
        // September 1, 2026 is a Tuesday: with Monday first, one blank day precedes it.
        Assert.Single(cut.FindAll("ol li[aria-hidden=true]"));
    }

    [Fact]
    public async Task Starts_the_week_on_saturday_when_chosen()
    {
        using var app = await StartAsync(new Preferences { WeekStart = WeekStart.Saturday });
        app.Core.Clock.Now = LocalTime(2026, 9, 1);
        var cut = Render<Calendar>();

        Assert.Equal("SaSuMoTuWeThFr", cut.Find(".grid-cols-7[aria-hidden=true]").TextContent);
        Assert.Equal(3, cut.FindAll("ol li[aria-hidden=true]").Count);
    }

    [Fact]
    public async Task Moves_between_months()
    {
        using var app = await StartAsync();
        app.Core.Clock.Now = LocalTime(2026, 9, 29);
        var cut = Render<Calendar>();

        cut.Find("button[aria-label='Previous month']").Click();

        cut.WaitForAssertion(() => Assert.Equal("August 2026", cut.Find("h2").TextContent));
        Assert.Equal(31, cut.FindAll("ol a").Count);
    }
}
