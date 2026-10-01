namespace FalconNotes.UI.Components.Ui;

/// <summary>Class-string helpers shared by the UI primitives, ported from <c>components/ui.tsx</c>.</summary>
public static class Classes
{
    /// <summary>The focus outline every interactive primitive uses.</summary>
    public const string FocusRing =
        "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-maple-500";

    /// <summary>Joins class names, skipping empty ones (the reference's <c>cn</c>).</summary>
    /// <param name="classes">Class strings; null or empty ones are left out.</param>
    /// <returns>The classes separated by single spaces.</returns>
    public static string Join(params string?[] classes) =>
        string.Join(' ', classes.Where(c => !string.IsNullOrEmpty(c)));
}
