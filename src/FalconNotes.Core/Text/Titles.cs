using System.Text.RegularExpressions;

namespace FalconNotes.Core.Text;

/// <summary>
/// A note's title is its first line written as a Markdown heading (<c># Title</c>), so titles survive exports,
/// searches and restores. Port of <c>web/lib/titles.ts</c> (docs/04, Titles).
/// </summary>
public static partial class Titles
{
    /// <summary>Splits a note's text into its title (the first line, if it is a <c># heading</c>) and the rest.</summary>
    /// <param name="content">The note's text.</param>
    /// <returns>The title (empty when there is none) and the body, with one leading blank line removed.</returns>
    public static (string Title, string Body) Split(string content)
    {
        var newline = content.IndexOf('\n', StringComparison.Ordinal);
        var first = newline < 0 ? content : content[..newline];
        if (first.EndsWith('\r'))
        {
            first = first[..^1];
        }

        var match = Heading().Match(first);
        if (!match.Success || match.Groups[1].Value.Trim().Length == 0)
        {
            return ("", content);
        }

        var rest = newline < 0 ? "" : content[(newline + 1)..];
        if (rest.StartsWith("\r\n", StringComparison.Ordinal))
        {
            rest = rest[2..];
        }
        else if (rest.StartsWith('\n'))
        {
            rest = rest[1..];
        }

        return (match.Groups[1].Value.Trim(), rest);
    }

    /// <summary>Puts a title in front of a note's text as a <c># heading</c>; an empty title leaves the text as it is.</summary>
    /// <param name="title">The title; white space is collapsed to single spaces.</param>
    /// <param name="body">The rest of the text.</param>
    /// <returns>The note's text.</returns>
    public static string Join(string title, string body)
    {
        var clean = OneLine(title);
        if (clean.Length == 0)
        {
            return body;
        }

        return body.Trim().Length > 0 ? $"# {clean}\n\n{body}" : $"# {clean}";
    }

    /// <summary>Collapses every run of white space to one space and trims the ends.</summary>
    /// <param name="text">Any text.</param>
    /// <returns>The text on one line.</returns>
    public static string OneLine(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"^# +(.*?)(?: +#+)? *$", RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
