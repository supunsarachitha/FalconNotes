namespace FalconNotes.Core.Domain;

/// <summary>What a note is for, which decides where the app shows it. Stored as an integer (docs/03, Schema).</summary>
public enum NoteKind
{
    /// <summary>A note in the Home timeline.</summary>
    Note = 0,

    /// <summary>A todo list: a title and <c>- [ ]</c> items, shown on the Todo page.</summary>
    Todo = 1,

    /// <summary>A quick note, kept out of the timeline on the Quick notes page.</summary>
    Quick = 2,

    /// <summary>
    /// A habit: a title and one <c>- yyyy-MM-dd</c> line per day done, shown only on the Habits page. A habit never
    /// becomes another kind of note, nor another note a habit.
    /// </summary>
    Habit = 3,
}

/// <summary>Which kinds of notes the user's features include. Port of <c>web/lib/kinds.ts</c>.</summary>
public static class NoteKinds
{
    /// <summary>Every kind of note: exports cover them all, and the trash shows them all.</summary>
    public static readonly IReadOnlyList<NoteKind> All = [NoteKind.Note, NoteKind.Todo, NoteKind.Quick, NoteKind.Habit];

    /// <summary>
    /// The kinds the user has turned on, which searches, tag, label and day filters, counts and the archive cover:
    /// always notes, plus todo lists and quick notes when on. Never habits: only the Habits page lists them.
    /// </summary>
    /// <param name="preferences">The user's preferences.</param>
    /// <returns>The enabled kinds.</returns>
    public static IReadOnlyList<NoteKind> Enabled(Preferences preferences)
    {
        var kinds = new List<NoteKind> { NoteKind.Note };
        if (preferences.TodoLists)
        {
            kinds.Add(NoteKind.Todo);
        }

        if (preferences.QuickNotes)
        {
            kinds.Add(NoteKind.Quick);
        }

        return kinds;
    }
}
