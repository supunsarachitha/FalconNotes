using System.Text.RegularExpressions;

namespace FalconNotes.Core.Text;

/// <summary>The toolbar's formats.</summary>
public enum MarkdownFormat
{
    /// <summary><c>**bold**</c>.</summary>
    Bold,

    /// <summary><c>*italic*</c>.</summary>
    Italic,

    /// <summary><c>## </c> before each line.</summary>
    Heading,

    /// <summary><c>- </c> before each line.</summary>
    Bullets,

    /// <summary><c>- [ ] </c> before each line.</summary>
    Checklist,

    /// <summary><c>&gt; </c> before each line.</summary>
    Quote,

    /// <summary>Inline code, or a fenced block over several lines.</summary>
    Code,

    /// <summary><c>[text](https://)</c>.</summary>
    Link,
}

/// <summary>
/// Replace <c>text[Start..End]</c> with <see cref="Insert"/>, then select <see cref="SelectStart"/> to
/// <see cref="SelectEnd"/> (positions in the new text).
/// </summary>
/// <param name="Start">Where the replaced text starts.</param>
/// <param name="End">Where it ends (exclusive).</param>
/// <param name="Insert">The new text.</param>
/// <param name="SelectStart">The new selection's start.</param>
/// <param name="SelectEnd">The new selection's end.</param>
public sealed record TextEdit(int Start, int End, string Insert, int SelectStart, int SelectEnd);

/// <summary>
/// Markdown formatting for the toolbar, and ticking task-list items: pure functions from the text and selection to the
/// change, so the editor applies it as one undoable step. Port of <c>web/lib/markdownEdit.ts</c> (docs/04).
/// Positions are UTF-16 offsets, as in the browser.
/// </summary>
public static partial class MarkdownEdit
{
    /// <summary>The change a toolbar button makes to the text around the selection.</summary>
    /// <param name="format">The button.</param>
    /// <param name="text">The text.</param>
    /// <param name="start">The selection's start.</param>
    /// <param name="end">The selection's end.</param>
    /// <returns>The edit.</returns>
    public static TextEdit Format(MarkdownFormat format, string text, int start, int end)
    {
        if (format == MarkdownFormat.Code && text[start..end].Contains('\n', StringComparison.Ordinal))
        {
            var selected = text[start..end];
            var inner = selected.EndsWith('\n') ? selected[..^1] : selected;
            return new TextEdit(start, end, "```\n" + inner + "\n```", start + 4, start + 4 + inner.Length);
        }

        switch (format)
        {
            case MarkdownFormat.Bold: return Wrap(text, start, end, "**", "**", "bold text");
            case MarkdownFormat.Italic: return Wrap(text, start, end, "*", "*", "italic text");
            case MarkdownFormat.Code: return Wrap(text, start, end, "`", "`", "code");
            case MarkdownFormat.Heading: return PrefixLines(text, start, end, "## ");
            case MarkdownFormat.Bullets: return PrefixLines(text, start, end, "- ");
            case MarkdownFormat.Checklist: return PrefixLines(text, start, end, "- [ ] ");
            case MarkdownFormat.Quote: return PrefixLines(text, start, end, "> ");
        }

        // A link: the selection becomes the link text, and the address placeholder is selected to type over.
        var label = start < end ? text[start..end] : "link text";
        var insert = $"[{label}](https://)";
        var urlStart = start + label.Length + 3;
        return new TextEdit(start, end, insert, urlStart, urlStart + "https://".Length);
    }

    /// <summary>Applies an edit to a string.</summary>
    /// <param name="text">The text.</param>
    /// <param name="edit">The edit.</param>
    /// <returns>The changed text.</returns>
    public static string Apply(string text, TextEdit edit) => text[..edit.Start] + edit.Insert + text[edit.End..];

    /// <summary>
    /// Ticks or unticks the task-list item whose list marker starts at <paramref name="offset"/>; null when no item
    /// starts there.
    /// </summary>
    /// <param name="text">The note's text.</param>
    /// <param name="offset">Where the item's bullet or number starts.</param>
    /// <returns>The changed text, or null.</returns>
    public static string? ToggleTask(string text, int offset)
    {
        if (offset < 0 || offset > text.Length)
        {
            return null;
        }

        var match = Task().Match(text, offset);
        if (!match.Success)
        {
            return null;
        }

        var box = offset + match.Length - 2;
        return text[..box] + (match.Groups[1].Value == " " ? "x" : " ") + text[(box + 1)..];
    }

    private static TextEdit Wrap(string text, int start, int end, string before, string after, string placeholder)
    {
        // Already wrapped (markers just outside or just inside the selection): unwrap.
        if (Slice(text, start - before.Length, start) == before && Slice(text, end, end + after.Length) == after)
        {
            var inner = text[start..end];
            return new TextEdit(start - before.Length, end + after.Length, inner, start - before.Length, end - before.Length);
        }

        var selected = text[start..end];
        if (selected.Length >= before.Length + after.Length
            && selected.StartsWith(before, StringComparison.Ordinal) && selected.EndsWith(after, StringComparison.Ordinal))
        {
            var inner = selected[before.Length..^after.Length];
            return new TextEdit(start, end, inner, start, start + inner.Length);
        }

        var content = selected.Length > 0 ? selected : placeholder;
        return new TextEdit(start, end, before + content + after, start + before.Length, start + before.Length + content.Length);
    }

    private static TextEdit PrefixLines(string text, int start, int end, string prefix)
    {
        // JavaScript's lastIndexOf clamps a negative start to 0, so at offset 0 it still looks at the first character.
        var lineStart = (start == 0 ? (text.Length > 0 && text[0] == '\n' ? 0 : -1) : text.LastIndexOf('\n', start - 1)) + 1;
        var searchFrom = end > start && text[end - 1] == '\n' ? end - 1 : end;
        var at = searchFrom <= text.Length ? text.IndexOf('\n', searchFrom) : -1;
        var lineEnd = at < 0 ? text.Length : at;
        var lines = text[lineStart..lineEnd].Split('\n');

        // Every line already has it: take it off. Otherwise add it to the lines that lack it, replacing another list or
        // heading marker (a bullet becomes a checklist item, and so on).
        var remove = lines.All(line => line.StartsWith(prefix, StringComparison.Ordinal));
        var changed = lines.Select(line => remove
            ? line[prefix.Length..]
            : line.StartsWith(prefix, StringComparison.Ordinal) ? line : prefix + OtherMarker().Replace(line, "", 1));
        var insert = string.Join('\n', changed);
        var single = lines.Length == 1;

        // One line: keep the cursor where it was in the text; several: select them all.
        return new TextEdit(
            lineStart,
            lineEnd,
            insert,
            single ? Math.Max(lineStart, start + insert.Length - lines[0].Length) : lineStart,
            single ? Math.Max(lineStart, end + insert.Length - lines[0].Length) : lineStart + insert.Length);
    }

    /// <summary>JavaScript's <c>slice</c>: out-of-range bounds are clamped, never thrown.</summary>
    private static string Slice(string text, int from, int to)
    {
        from = Math.Clamp(from, 0, text.Length);
        to = Math.Clamp(to, 0, text.Length);
        return from < to ? text[from..to] : "";
    }

    [GeneratedRegex(@"\G(?:[-*+]|[0-9]{1,9}[.)])[ \t]+\[([ xX])\]", RegexOptions.CultureInvariant)]
    private static partial Regex Task();

    [GeneratedRegex(@"^(#{1,6} |- \[[ xX]\] |[-*+] |> )", RegexOptions.CultureInvariant)]
    private static partial Regex OtherMarker();
}
