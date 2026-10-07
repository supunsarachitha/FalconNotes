using FalconNotes.Core.Domain;
using FalconNotes.UI.Components;

namespace FalconNotes.UI.Tests;

/// <summary>Port of HabitCalendar.test.tsx (docs/11).</summary>
public class HabitCalendarTests : BunitContext
{
    // Created at noon UTC, so each habit starts on that day regardless of the host's local time zone.
    private static Note MakeHabit(string content, DateTime created) => new(
        Guid.NewGuid(), NoteKind.Habit, content, null, false, null, null, created, created, 0, [], [], []);

    private static readonly Note Read = MakeHabit("# Read\n\n- 2026-09-27\n- 2026-09-28\n- 2026-09-29", new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));
    private static readonly Note Walk = MakeHabit("# Walk\n\n- 2026-09-24\n- 2026-09-25\n- 2026-09-26\n- 2026-09-29", new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc));
    private const string Today = "2026-09-29"; // a Tuesday

    private static bool HasText(IRenderedComponent<HabitCalendar> cut, string text) =>
        cut.FindAll("span.sr-only").Any(s => s.TextContent == text) || cut.Markup.Contains(text, StringComparison.Ordinal);

    [Fact]
    public async Task Shows_the_month_for_all_habits_counted_from_each_habits_start()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");

        var cut = Render<HabitCalendar>(p => p.Add(c => c.Notes, [Read, Walk]).Add(c => c.Today, Today).Add(c => c.WeekStart, DayOfWeek.Monday));

        Assert.Contains("September 2026", cut.Markup, StringComparison.Ordinal);
        Assert.True(HasText(cut, "Tuesday, September 29 (today): 2 of 2 habits done"));
        Assert.True(HasText(cut, "Sunday, September 27: 1 of 2 habits done"));
        Assert.True(HasText(cut, "Monday, September 21: 0 of 1 habit done"));
        Assert.True(HasText(cut, "Thursday, September 10: no habits yet"));
        Assert.True(HasText(cut, "Wednesday, September 30: still to come"));
        Assert.Contains("44% of habit days done", cut.Markup, StringComparison.Ordinal); // 7 of 16
        Assert.Contains("All done", cut.Markup, StringComparison.Ordinal);
        // September 1, 2026 is a Tuesday: in a week starting on Monday, one hidden blank comes before it.
        var days = cut.FindAll("ol > li");
        Assert.Equal(30, days.Count(d => !d.HasAttribute("aria-hidden")));
        Assert.True(days[0].HasAttribute("aria-hidden"));
    }

    [Fact]
    public async Task Shows_one_habits_days_and_goes_back_a_month_at_a_time_never_past_this_one()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<HabitCalendar>(p => p.Add(c => c.Notes, [Read, Walk]).Add(c => c.Today, Today).Add(c => c.WeekStart, DayOfWeek.Monday));

        cut.Find("select[aria-label='Habits in the calendar']").Change(Read.Id.ToString());

        Assert.True(HasText(cut, "Sunday, September 27: done"));
        Assert.True(HasText(cut, "Wednesday, September 23: not done"));
        Assert.True(HasText(cut, "Saturday, September 19: before this habit started"));
        Assert.Contains("3 of 10 days done", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("All done", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find("button[aria-label='Next month']").HasAttribute("disabled"));

        cut.Find("button[aria-label='Previous month']").Click();

        Assert.Contains("August 2026", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Nothing to count in this month yet.", cut.Markup, StringComparison.Ordinal);
        Assert.False(cut.Find("button[aria-label='Next month']").HasAttribute("disabled"));
    }
}
