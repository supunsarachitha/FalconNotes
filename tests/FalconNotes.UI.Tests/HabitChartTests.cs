using FalconNotes.Core.Domain;
using FalconNotes.UI.Components;

namespace FalconNotes.UI.Tests;

/// <summary>Port of HabitChart.test.tsx (docs/11). Needs only a TimeProvider (for each habit's local start day), not
/// a database: habits are plain in-memory notes, as the reference passes plain objects.</summary>
public class HabitChartTests : BunitContext
{
    // Created at noon UTC, so each habit starts on that day regardless of the host's local time zone.
    private static Note MakeHabit(string content, DateTime created) => new(
        Guid.NewGuid(), NoteKind.Habit, content, null, false, null, null, created, created, 0, [], [], []);

    private static readonly Note Read = MakeHabit("# Read\n\n- 2026-09-27\n- 2026-09-28\n- 2026-09-29", new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));
    private static readonly Note Walk = MakeHabit("# Walk\n\n- 2026-09-24\n- 2026-09-25\n- 2026-09-26\n- 2026-09-29", new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc));
    private const string Today = "2026-09-29"; // a Tuesday

    private static string ParagraphText(IRenderedComponent<HabitChart> cut, int index) => cut.FindAll("p")[index].TextContent;

    private static string[] RowTexts(IRenderedComponent<HabitChart> cut) => cut.FindAll("table tr").Select(r => r.TextContent).ToArray();

    [Fact]
    public async Task Shows_the_share_of_days_done_per_week_for_all_habits_each_counted_from_its_start()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");

        var cut = Render<HabitChart>(p => p.Add(c => c.Notes, [Read, Walk]).Add(c => c.Today, Today).Add(c => c.WeekStart, DayOfWeek.Monday));

        Assert.Equal("This week: 75% · Last 12 weeks: 44%", ParagraphText(cut, 0));
        var rows = RowTexts(cut);
        Assert.Equal(13, rows.Length);
        Assert.Equal("Week of Jul 13—No habits yet", rows[1]);
        Assert.Equal(["Week of Sep 140%0 of 1 day", "Week of Sep 2136%4 of 11 days", "Week of Sep 2875%3 of 4 days"], rows[^3..]);
        Assert.Equal(3, cut.FindAll("[data-bar]").Count);
        Assert.DoesNotContain("Current streak", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_one_habit_with_its_streaks()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<HabitChart>(p => p.Add(c => c.Notes, [Read, Walk]).Add(c => c.Today, Today).Add(c => c.WeekStart, DayOfWeek.Monday));

        cut.Find("select[aria-label='Habits in the chart']").Change(Walk.Id.ToString());

        Assert.Equal("This week: 50% · Last 12 weeks: 67%", ParagraphText(cut, 0));
        Assert.Equal("Current streak: 1 day · Best: 3 days · Done: 4 days", ParagraphText(cut, 1));
        Assert.Equal("Share of days done per week, Walk", cut.Find("caption").TextContent);
    }

    [Fact]
    public async Task Switches_to_months()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<HabitChart>(p => p.Add(c => c.Notes, [Read, Walk]).Add(c => c.Today, Today).Add(c => c.WeekStart, DayOfWeek.Monday));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Months").Click();

        Assert.Equal("true", cut.FindAll("button").First(b => b.TextContent.Trim() == "Months").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.FindAll("button").First(b => b.TextContent.Trim() == "Weeks").GetAttribute("aria-pressed"));
        Assert.Equal("This month: 44% · Last 12 months: 44%", ParagraphText(cut, 0));
        var rows = RowTexts(cut);
        Assert.Equal("October 2025—No habits yet", rows[1]);
        Assert.Equal("September 202644%7 of 16 days", rows[12]);
    }

    [Fact]
    public async Task Starts_weeks_on_the_chosen_first_day()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<HabitChart>(p => p.Add(c => c.Notes, [Read, Walk]).Add(c => c.Today, Today).Add(c => c.WeekStart, DayOfWeek.Sunday));

        var rows = RowTexts(cut);

        Assert.Equal("Week of Sep 2767%4 of 6 days", rows[^1]); // Sunday to today
    }

    [Fact]
    public async Task Goes_back_to_all_habits_when_the_chosen_one_is_archived()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<HabitChart>(p => p.Add(c => c.Notes, [Read, Walk]).Add(c => c.Today, Today).Add(c => c.WeekStart, DayOfWeek.Monday));
        cut.Find("select[aria-label='Habits in the chart']").Change(Walk.Id.ToString());

        cut.Render(p => p.Add(c => c.Notes, [Read]));

        Assert.Equal("all", cut.Find("select[aria-label='Habits in the chart']").GetAttribute("value"));
        Assert.Equal("Share of days done per week, all habits", cut.Find("caption").TextContent);
    }
}
