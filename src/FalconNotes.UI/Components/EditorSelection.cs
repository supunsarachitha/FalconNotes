namespace FalconNotes.UI.Components;

/// <summary>A text box's current value and selection, read from the DOM (editor.js's <c>getSelection</c>).</summary>
/// <param name="Value">The text box's value.</param>
/// <param name="Start">The selection's start.</param>
/// <param name="End">The selection's end; equal to <paramref name="Start"/> when nothing is selected.</param>
public sealed record EditorSelection(string Value, int Start, int End);
