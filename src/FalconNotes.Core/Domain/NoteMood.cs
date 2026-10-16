namespace FalconNotes.Core.Domain;

/// <summary>
/// How its writer felt, chosen as a smiley on a note or quick note (docs/04, Moods). This app's own, from 1.7.0.
/// Stored as an integer (docs/03, Schema) and written by name in backups (docs/05, Moods in backups), so the numbers
/// and the names never change; a new mood takes the next number.
/// </summary>
public enum NoteMood
{
    /// <summary>No mood chosen: the default, and every note written before 1.7.0.</summary>
    None = 0,

    /// <summary>The worst of the five.</summary>
    Awful = 1,

    /// <summary>Not good.</summary>
    Bad = 2,

    /// <summary>Neither good nor bad.</summary>
    Okay = 3,

    /// <summary>Good.</summary>
    Good = 4,

    /// <summary>The best of the five.</summary>
    Great = 5,
}

/// <summary>The rules of moods: which there are, which notes can have one, and how they are stored and exported.</summary>
public static class NoteMoods
{
    /// <summary>The moods to choose from, in the order the picker shows them: worst to best.</summary>
    public static readonly IReadOnlyList<NoteMood> All = [NoteMood.Awful, NoteMood.Bad, NoteMood.Okay, NoteMood.Good, NoteMood.Great];

    /// <summary>
    /// Whether a note of this kind can have a mood: notes and quick notes, which are written like a diary. A todo list
    /// and a habit are not, and have no composer to choose one in.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>Whether it can.</returns>
    public static bool Allowed(NoteKind kind) => kind is NoteKind.Note or NoteKind.Quick;

    /// <summary>The mood as backups write it: <c>awful</c>, <c>bad</c>, <c>okay</c>, <c>good</c> or <c>great</c>.</summary>
    /// <param name="mood">The mood.</param>
    /// <returns>Its name, or null for none, which backups leave out.</returns>
    public static string? Name(NoteMood mood) => mood == NoteMood.None || !Enum.IsDefined(mood) ? null : mood.ToString().ToLowerInvariant();

    /// <summary>Reads a mood as backups write it, in any case. Anything else, such as a mood a later version adds, is none.</summary>
    /// <param name="name">The name, or null.</param>
    /// <returns>The mood.</returns>
    public static NoteMood Parse(string? name) =>
        All.FirstOrDefault(mood => string.Equals(Name(mood), name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads a stored mood. A number this version does not know, written by a later one, shows as none and is left as it is.</summary>
    /// <param name="stored">The column's value.</param>
    /// <returns>The mood.</returns>
    public static NoteMood FromStored(long stored) => stored is >= 1 and <= 5 ? (NoteMood)stored : NoteMood.None;
}
