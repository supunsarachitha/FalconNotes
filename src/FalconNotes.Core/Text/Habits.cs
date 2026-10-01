using System.Globalization;
using System.Text.RegularExpressions;

namespace FalconNotes.Core.Text;

/// <summary>A habit as the Habits page edits it.</summary>
/// <param name="Name">Its name: the note's title.</param>
/// <param name="Days">The days done (<c>yyyy-MM-dd</c>), oldest first, each once.</param>
/// <param name="Notes">Any other text in the note, kept as it was.</param>
public sealed record Habit(string Name, IReadOnlyList<string> Days, string Notes);

/// <summary>A stretch of days, first and last included (<c>yyyy-MM-dd</c>).</summary>
/// <param name="Start">The first day.</param>
/// <param name="End">The last day.</param>
public sealed record Period(string Start, string End);

/// <summary>The days done in a period, and the days that could have been done.</summary>
/// <param name="Start">The first day.</param>
/// <param name="End">The last day.</param>
/// <param name="Done">Days done.</param>
/// <param name="Possible">Days that counted.</param>
public sealed record PeriodScore(string Start, string End, int Done, int Possible);

/// <summary>What the chart needs to know about a habit.</summary>
/// <param name="Start">The first day it counts from (see <see cref="Habits.Start"/>).</param>
/// <param name="Days">The days done.</param>
public sealed record TrackedHabit(string Start, IReadOnlyList<string> Days);

/// <summary>The current streak, the best streak and the days done in total.</summary>
/// <param name="Current">Days in a row up to today, or up to yesterday while today is not ticked.</param>
/// <param name="Best">The longest run.</param>
/// <param name="Total">Days done up to today.</param>
public sealed record Streaks(int Current, int Best, int Total);

/// <summary>Weeks or months, for the habit chart.</summary>
public enum ChartRange
{
    /// <summary>Weeks starting on the chosen first day.</summary>
    Weeks,

    /// <summary>Calendar months.</summary>
    Months,
}

/// <summary>
/// Habits are notes of kind Habit: a <c># name</c>, any other text, then one <c>- yyyy-MM-dd</c> line per day done.
/// Port of <c>web/lib/habits.ts</c> (docs/04, Habits). Day arithmetic uses day numbers
/// (<see cref="DateOnly.DayNumber"/>), which no time zone or daylight-saving change can shift.
/// </summary>
public static partial class Habits
{
    /// <summary>The most characters in a habit's name.</summary>
    public const int MaxNameLength = 300;

    /// <summary>The day number of a valid <c>yyyy-MM-dd</c> date, or null.</summary>
    /// <param name="key">The date.</param>
    /// <returns>The day number, or null.</returns>
    public static int? DayNumber(string key) => DateFormats.ParseKey(key)?.DayNumber;

    /// <summary>The <c>yyyy-MM-dd</c> date of a day number.</summary>
    /// <param name="day">The day number.</param>
    /// <returns>The date.</returns>
    public static string DayKey(int day) => DateFormats.Key(DateOnly.FromDayNumber(day));

    /// <summary>Moves a date by a number of days.</summary>
    /// <param name="key">The date.</param>
    /// <param name="days">Days to add; negative to go back.</param>
    /// <returns>The new date.</returns>
    public static string AddDays(string key, int days) => DayKey(DayNumber(key)!.Value + days);

    /// <summary>The day of the week of a date.</summary>
    /// <param name="key">The date.</param>
    /// <returns>Its weekday.</returns>
    public static DayOfWeek Weekday(string key) => DateFormats.ParseKey(key)!.Value.DayOfWeek;

    /// <summary>The <paramref name="count"/> days that end on <paramref name="end"/>, oldest first.</summary>
    /// <param name="end">The last day.</param>
    /// <param name="count">How many days.</param>
    /// <returns>The days.</returns>
    public static IReadOnlyList<string> DaysEnding(string end, int count)
    {
        var last = DayNumber(end)!.Value;
        return Enumerable.Range(0, count).Select(i => DayKey(last - count + 1 + i)).ToList();
    }

    /// <summary>Reads a habit from its note's text. Lines that are not days, such as a description, are kept as text.</summary>
    /// <param name="content">The note's text.</param>
    /// <returns>The habit.</returns>
    public static Habit Parse(string content)
    {
        var (title, body) = Titles.Split(content);
        var days = new SortedSet<string>(StringComparer.Ordinal);
        var other = new List<string>();
        foreach (var line in Lines().Split(body))
        {
            var match = DayLine().Match(line);
            if (match.Success && DayNumber(match.Groups[1].Value) is not null)
            {
                days.Add(match.Groups[1].Value);
            }
            else
            {
                other.Add(line);
            }
        }

        return new Habit(title, days.ToList(), string.Join('\n', other).Trim());
    }

    /// <summary>Writes a habit as Markdown: its <c># name</c>, any other text, then the days done.</summary>
    /// <param name="habit">The habit. A blank name becomes "Untitled habit".</param>
    /// <returns>The note's text.</returns>
    public static string Serialize(Habit habit)
    {
        var days = string.Join('\n', habit.Days.Select(day => $"- {day}"));
        var name = Titles.OneLine(habit.Name);
        var body = string.Join("\n\n", new[] { habit.Notes.Trim(), days }.Where(part => part.Length > 0));
        return Titles.Join(name.Length > 0 ? name : "Untitled habit", body);
    }

    /// <summary>Marks a day done, or not done if it was.</summary>
    /// <param name="habit">The habit.</param>
    /// <param name="day">The day.</param>
    /// <returns>The changed habit.</returns>
    public static Habit ToggleDay(Habit habit, string day)
    {
        var days = habit.Days.Contains(day)
            ? habit.Days.Where(d => d != day).ToList()
            : habit.Days.Append(day).Order(StringComparer.Ordinal).ToList();
        return habit with { Days = days };
    }

    /// <summary>
    /// The first day a habit counts from: the local day it was created on this device, or its first day done if earlier.
    /// </summary>
    /// <param name="habit">The habit.</param>
    /// <param name="createdAtUtc">When its note was created.</param>
    /// <param name="zone">The device's time zone.</param>
    /// <returns>The day.</returns>
    public static string Start(Habit habit, DateTime createdAtUtc, TimeZoneInfo zone)
    {
        var created = DateFormats.LocalKey(new DateTimeOffset(DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc)), zone);
        return habit.Days.Count > 0 && string.CompareOrdinal(habit.Days[0], created) < 0 ? habit.Days[0] : created;
    }

    /// <summary>
    /// The current streak (up to today, or up to yesterday while today is not ticked), the best streak, and the days
    /// done in total. Days after today do not count.
    /// </summary>
    /// <param name="days">The days done.</param>
    /// <param name="today">Today.</param>
    /// <returns>The streaks.</returns>
    public static Streaks Streaks(IEnumerable<string> days, string today)
    {
        var past = days.Where(day => string.CompareOrdinal(day, today) <= 0)
            .Distinct(StringComparer.Ordinal)
            .Select(day => DayNumber(day)!.Value)
            .Order()
            .ToList();
        int best = 0, run = 0;
        for (var i = 0; i < past.Count; i++)
        {
            run = i > 0 && past[i] == past[i - 1] + 1 ? run + 1 : 1;
            best = Math.Max(best, run);
        }

        var done = past.ToHashSet();
        var day = DayNumber(today)!.Value;
        if (!done.Contains(day))
        {
            day--;
        }

        var current = 0;
        while (done.Contains(day))
        {
            current++;
            day--;
        }

        return new Streaks(current, best, past.Count);
    }

    /// <summary>The last <paramref name="count"/> weeks or months, oldest first; the last one holds today.</summary>
    /// <param name="range">Weeks or months.</param>
    /// <param name="today">Today.</param>
    /// <param name="weekStart">The week's first day.</param>
    /// <param name="count">How many periods.</param>
    /// <returns>The periods.</returns>
    public static IReadOnlyList<Period> RecentPeriods(ChartRange range, string today, DayOfWeek weekStart, int count = 12)
    {
        if (range == ChartRange.Weeks)
        {
            var start = DayNumber(today)!.Value - (((int)Weekday(today) - (int)weekStart + 7) % 7);
            return Enumerable.Range(0, count).Select(i =>
            {
                var first = start - 7 * (count - 1 - i);
                return new Period(DayKey(first), DayKey(first + 6));
            }).ToList();
        }

        return Enumerable.Range(0, count).Select(i => MonthOf(today, -(count - 1 - i))).ToList();
    }

    /// <summary>The month <paramref name="offset"/> months away from the one holding <paramref name="day"/>.</summary>
    /// <param name="day">A day in the starting month.</param>
    /// <param name="offset">Months to move; 0 for that month.</param>
    /// <returns>The month's first and last day.</returns>
    public static Period MonthOf(string day, int offset = 0)
    {
        var date = DateFormats.ParseKey(day)!.Value;
        var first = new DateOnly(date.Year, date.Month, 1).AddMonths(offset);
        return new Period(DateFormats.Key(first), DateFormats.Key(first.AddMonths(1).AddDays(-1)));
    }

    /// <summary>Every day of a period, oldest first.</summary>
    /// <param name="period">The period.</param>
    /// <returns>The days.</returns>
    public static IReadOnlyList<string> DaysOf(Period period) =>
        DaysEnding(period.End, DayNumber(period.End)!.Value - DayNumber(period.Start)!.Value + 1);

    /// <summary>
    /// How many days of a period the habits were done, out of how many they could have been: each habit counts from
    /// its start, and no day after today counts.
    /// </summary>
    /// <param name="habits">The habits.</param>
    /// <param name="period">The period.</param>
    /// <param name="today">Today.</param>
    /// <returns>The score.</returns>
    public static PeriodScore ScorePeriod(IEnumerable<TrackedHabit> habits, Period period, string today)
    {
        var last = string.CompareOrdinal(period.End, today) < 0 ? period.End : today;
        int done = 0, possible = 0;
        foreach (var habit in habits)
        {
            var first = string.CompareOrdinal(period.Start, habit.Start) > 0 ? period.Start : habit.Start;
            if (string.CompareOrdinal(first, last) > 0)
            {
                continue;
            }

            possible += DayNumber(last)!.Value - DayNumber(first)!.Value + 1;
            done += habit.Days.Count(day => string.CompareOrdinal(day, first) >= 0 && string.CompareOrdinal(day, last) <= 0);
        }

        return new PeriodScore(period.Start, period.End, done, possible);
    }

    /// <summary>The share of possible days done, from 0 to 1, or null when no day was possible.</summary>
    /// <param name="scores">The scores.</param>
    /// <returns>The share, or null.</returns>
    public static double? Share(IEnumerable<PeriodScore> scores)
    {
        var list = scores.ToList();
        var possible = list.Sum(score => score.Possible);
        return possible > 0 ? (double)list.Sum(score => score.Done) / possible : null;
    }

    [GeneratedRegex(@"^\s*[-*+]\s+([0-9]{4}-[0-9]{2}-[0-9]{2})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex DayLine();

    [GeneratedRegex(@"\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex Lines();
}
