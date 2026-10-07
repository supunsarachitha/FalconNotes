using FalconNotes.Core.Text;

// Deliberately FalconNotes.UI.Components, not .Habits: a nested namespace named "Habits" would shadow
// FalconNotes.Core.Text.Habits (the static class) for every file in FalconNotes.UI.Components, this one's own
// folder included — C# prefers a same-named nested namespace over a using-imported type.
namespace FalconNotes.UI.Components;

/// <summary>
/// Shared layout and small helpers for the Habits page's list and its week header (<c>HABIT_ROW</c>, <c>DAY_GRID</c>,
/// <c>habitName</c> and <c>dayCount</c> in components/HabitRow.tsx): the name, the days and the menu line up between
/// the header and every row.
/// </summary>
public static class HabitLayout
{
    /// <summary>The name, the days and the menu on one line; on phones the name and the menu share a line and the
    /// days go under them.</summary>
    public const string Row = "grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-2 gap-y-1.5 sm:grid-cols-[minmax(0,1fr)_auto_auto]";

    /// <summary>The seven days, wrapping under the name on phones.</summary>
    public const string DayGrid = "col-span-2 grid grid-cols-7 justify-items-center gap-1 sm:order-2 sm:col-span-1 sm:flex";

    /// <summary>A habit's display name, falling back to "Untitled habit" as <see cref="Habits.Serialize"/> does.</summary>
    /// <param name="habit">The habit.</param>
    /// <returns>Its name.</returns>
    public static string Name(Habit habit) => habit.Name.Length > 0 ? habit.Name : "Untitled habit";

    /// <summary>A day count with its unit, singular for one.</summary>
    /// <param name="count">The days.</param>
    /// <returns>E.g. "1 day" or "3 days".</returns>
    public static string DayCount(int count) => $"{count} {(count == 1 ? "day" : "days")}";
}
