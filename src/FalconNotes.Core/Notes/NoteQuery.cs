using FalconNotes.Core.Domain;

namespace FalconNotes.Core.Notes;

/// <summary>Which notes a list shows (docs/04, Lists, and the filters <c>?tag</c>, <c>?q</c>, <c>?day</c>, <c>?label</c>).</summary>
/// <param name="State">Which state.</param>
/// <param name="Kinds">Which kinds.</param>
/// <param name="Tag">A tag, including the tags nested under it; null for none.</param>
/// <param name="Label">A label's ID; null for none.</param>
/// <param name="CreatedFromUtc">Created at or after; null for no bound.</param>
/// <param name="CreatedBeforeUtc">Created before; null for no bound.</param>
/// <param name="Search">Text to find in the note or its file names; null for none.</param>
public sealed record NoteQuery(
    NoteState State,
    IReadOnlyList<NoteKind> Kinds,
    string? Tag = null,
    Guid? Label = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedBeforeUtc = null,
    string? Search = null);
