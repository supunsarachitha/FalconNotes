using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Text;

// Ported from web/lib/habits.test.ts.
public class HabitsTests
{
    private static void AssertDays(IEnumerable<string> expected, IEnumerable<string> actual) => Assert.Equal(expected, actual);

    [Fact]
    public void Reads_and_writes_a_name_and_its_days()
    {
        const string text = "# Read 20 minutes\n\n- 2026-09-27\n- 2026-09-28";
        var habit = Habits.Parse(text);

        Assert.Equal("Read 20 minutes", habit.Name);
        AssertDays(["2026-09-27", "2026-09-28"], habit.Days);
        Assert.Equal("", habit.Notes);
        Assert.Equal(text, Habits.Serialize(habit));
        Assert.Equal("# Stretch", Habits.Serialize(new Habit("Stretch", [], "")));
    }

    [Fact]
    public void Sorts_days_drops_repeats_and_keeps_any_other_text()
    {
        var habit = Habits.Parse("# Walk\nEvery evening, after dinner.\n\n- 2026-09-02\n* 2026-09-01\n- 2026-09-02\n- 2026-02-30\n");

        AssertDays(["2026-09-01", "2026-09-02"], habit.Days);
        Assert.Equal("Every evening, after dinner.\n\n- 2026-02-30", habit.Notes); // not a real day: kept as text
        Assert.Equal("# Walk\n\nEvery evening, after dinner.\n\n- 2026-02-30\n\n- 2026-09-01\n- 2026-09-02", Habits.Serialize(habit));
    }

    [Fact]
    public void Always_has_a_name()
    {
        Assert.Equal("# Untitled habit\n\n- 2026-09-01", Habits.Serialize(new Habit("  ", ["2026-09-01"], "")));
        Assert.Equal("# Two lines", Habits.Serialize(new Habit("Two\nlines", [], "")));
    }

    [Fact]
    public void Ticks_and_unticks_a_day()
    {
        var habit = new Habit("Run", ["2026-09-20", "2026-09-29"], "");

        AssertDays(["2026-09-20", "2026-09-25", "2026-09-29"], Habits.ToggleDay(habit, "2026-09-25").Days);
        AssertDays(["2026-09-20"], Habits.ToggleDay(habit, "2026-09-29").Days);
    }

    [Fact]
    public void Moves_across_months_years_leap_days_and_daylight_saving_changes()
    {
        Assert.Equal("2025-03-30", Habits.AddDays("2025-03-29", 1));
        Assert.Equal("2025-03-31", Habits.AddDays("2025-03-30", 1));
        Assert.Equal("2025-10-27", Habits.AddDays("2025-10-26", 1));
        Assert.Equal("2025-01-01", Habits.AddDays("2024-12-31", 1));
        Assert.Equal("2024-02-29", Habits.AddDays("2024-02-28", 1));
        Assert.Equal("2026-02-28", Habits.AddDays("2026-03-01", -1));
        Assert.Null(Habits.DayNumber("2026-02-29"));
        Assert.Null(Habits.DayNumber("not a day"));
        Assert.Equal(DayOfWeek.Tuesday, Habits.Weekday("2026-09-29"));
        AssertDays(["2026-09-29", "2026-09-30", "2026-10-01", "2026-10-02"], Habits.DaysEnding("2026-10-02", 4));
    }

    [Fact]
    public void Counts_a_habit_from_its_creation_day_on_this_device_or_an_earlier_first_day_done()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
        var created = new DateTime(2026, 9, 24, 16, 30, 0, DateTimeKind.Utc); // 12:30 in Toronto

        Assert.Equal("2026-09-24", Habits.Start(new Habit("Run", [], ""), created, zone));
        Assert.Equal("2026-09-24", Habits.Start(new Habit("Run", ["2026-09-26"], ""), created, zone));
        Assert.Equal("2026-09-20", Habits.Start(new Habit("Run", ["2026-09-20", "2026-09-26"], ""), created, zone));
        Assert.Equal("2026-09-23", Habits.Start(new Habit("Run", [], ""), new DateTime(2026, 9, 24, 2, 0, 0, DateTimeKind.Utc), zone));
    }

    private static readonly string[] StreakDays = ["2026-09-20", "2026-09-21", "2026-09-22", "2026-09-23", "2026-09-26", "2026-09-27", "2026-09-28"];

    [Fact]
    public void Counts_the_current_streak_up_to_yesterday_while_today_is_open_and_the_best_one()
    {
        Assert.Equal(new Streaks(3, 4, 7), Habits.Streaks(StreakDays, "2026-09-29"));
        Assert.Equal(new Streaks(4, 4, 8), Habits.Streaks([.. StreakDays, "2026-09-29"], "2026-09-29"));
    }

    [Fact]
    public void A_streak_ends_when_a_day_is_missed_and_days_after_today_do_not_count()
    {
        Assert.Equal(new Streaks(0, 4, 7), Habits.Streaks(StreakDays, "2026-09-30"));
        Assert.Equal(new Streaks(3, 4, 7), Habits.Streaks([.. StreakDays, "2026-10-05"], "2026-09-28"));
        Assert.Equal(new Streaks(0, 0, 0), Habits.Streaks([], "2026-09-29"));
    }

    [Fact]
    public void Finds_a_month_and_its_days_across_years_and_leap_years()
    {
        Assert.Equal(new Period("2026-09-01", "2026-09-30"), Habits.MonthOf("2026-09-29"));
        Assert.Equal(new Period("2025-12-01", "2025-12-31"), Habits.MonthOf("2026-01-15", -1));
        Assert.Equal(new Period("2028-02-01", "2028-02-29"), Habits.MonthOf("2027-12-31", 2));
        Assert.Equal(28, Habits.DaysOf(Habits.MonthOf("2026-02-10")).Count);
        AssertDays(["2026-09-29", "2026-09-30", "2026-10-01", "2026-10-02"], Habits.DaysOf(new Period("2026-09-29", "2026-10-02")));
    }

    [Fact]
    public void Lists_weeks_starting_on_the_chosen_day_the_last_one_holding_today()
    {
        var monday = Habits.RecentPeriods(ChartRange.Weeks, "2026-09-29", DayOfWeek.Monday);
        var sunday = Habits.RecentPeriods(ChartRange.Weeks, "2026-09-29", DayOfWeek.Sunday, 2);

        Assert.Equal(12, monday.Count);
        Assert.Equal(new Period("2026-09-28", "2026-10-04"), monday[11]);
        Assert.Equal(new Period("2026-07-13", "2026-07-19"), monday[0]);
        Assert.Equal([new Period("2026-09-20", "2026-09-26"), new Period("2026-09-27", "2026-10-03")], sunday);
        Assert.Equal([new Period("2026-09-27", "2026-10-03")], Habits.RecentPeriods(ChartRange.Weeks, "2026-09-27", DayOfWeek.Sunday, 1));
    }

    [Fact]
    public void Lists_months_across_a_new_year()
    {
        Assert.Equal(
            [new Period("2025-11-01", "2025-11-30"), new Period("2025-12-01", "2025-12-31"), new Period("2026-01-01", "2026-01-31")],
            Habits.RecentPeriods(ChartRange.Months, "2026-01-15", DayOfWeek.Monday, 3));
        Assert.Equal(new Period("2024-02-01", "2024-02-29"), Habits.RecentPeriods(ChartRange.Months, "2024-03-10", DayOfWeek.Monday, 2)[0]);
    }

    [Fact]
    public void Scores_the_days_done_out_of_the_days_each_habit_existed_up_to_today()
    {
        var read = new TrackedHabit("2026-09-24", ["2026-09-24", "2026-09-25", "2026-09-28", "2026-09-29"]);
        var walk = new TrackedHabit("2026-09-01", ["2026-09-21", "2026-09-22"]);
        var lastWeek = new Period("2026-09-21", "2026-09-27");
        var thisWeek = new Period("2026-09-28", "2026-10-04");

        Assert.Equal(new PeriodScore("2026-09-21", "2026-09-27", 2, 4), Habits.ScorePeriod([read], lastWeek, "2026-09-29"));
        Assert.Equal(new PeriodScore("2026-09-28", "2026-10-04", 2, 2), Habits.ScorePeriod([read], thisWeek, "2026-09-29"));
        Assert.Equal(new PeriodScore("2026-09-21", "2026-09-27", 4, 11), Habits.ScorePeriod([read, walk], lastWeek, "2026-09-29"));
        Assert.Equal(0, Habits.ScorePeriod([read], new Period("2026-09-14", "2026-09-20"), "2026-09-29").Possible);
        Assert.Equal(4.0 / 6, Habits.Share([new("a", "b", 2, 4), new("a", "b", 2, 2)]));
        Assert.Null(Habits.Share([new("a", "b", 0, 0)]));
    }
}
