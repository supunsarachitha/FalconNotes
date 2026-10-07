using System.Globalization;
using System.Text.RegularExpressions;

namespace FalconNotes.Core.Text;

/// <summary>A tag and how many notes use it.</summary>
/// <param name="Name">The tag, lower case, without <c>#</c>.</param>
/// <param name="NoteCount">Active notes of the enabled kinds that carry it.</param>
public sealed record TagCount(string Name, int NoteCount);

/// <summary>A <c>#tag</c> being typed: where its <c>#</c> is and what follows it, up to the caret.</summary>
/// <param name="Start">The offset of the <c>#</c>.</param>
/// <param name="Text">What was typed after it, lower case (may be empty).</param>
public sealed record TagQuery(int Start, string Text);

/// <summary>
/// Suggesting existing tags while a <c>#tag</c> is typed. Port of <c>web/lib/tagSuggest.ts</c> (docs/04, Tags). Where
/// a tag can start follows <see cref="TagParser"/>.
/// </summary>
public static partial class TagSuggest
{
    /// <summary>The most suggestions shown at once.</summary>
    public const int MaxSuggestions = 6;

    /// <summary>
    /// The tag being typed just before the caret, or null when the caret is not right after <c>#</c> and a partial tag.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="caret">The caret's offset.</param>
    /// <returns>The query, or null.</returns>
    public static TagQuery? QueryAt(string text, int caret)
    {
        var match = PartialTag().Match(text[..caret]);
        if (!match.Success)
        {
            return null;
        }

        // The tag must end at the caret: a tag character right after it means the caret is inside a word.
        if (caret < text.Length && TagCharacter().IsMatch(text[caret..]))
        {
            return null;
        }

        return new TagQuery(match.Index, match.Groups[1].Value.ToLowerInvariant());
    }

    /// <summary>
    /// Tags that match what was typed, best first: those starting with it, then those with a nested part starting with
    /// it (<c>meet</c> finds <c>work/meetings</c>), each group by note count, then name. A single exact match is no
    /// suggestion.
    /// </summary>
    /// <param name="tags">The tags in use.</param>
    /// <param name="typed">What was typed.</param>
    /// <param name="limit">The most to return.</param>
    /// <returns>The suggestions.</returns>
    public static IReadOnlyList<TagCount> Suggest(IEnumerable<TagCount> tags, string typed, int limit = MaxSuggestions)
    {
        int Rank(TagCount tag) =>
            tag.Name.StartsWith(typed, StringComparison.Ordinal) ? 0
            : tag.Name.Split('/').Any(part => part.StartsWith(typed, StringComparison.Ordinal)) ? 1
            : -1;

        var matches = tags
            .Select(tag => (Tag: tag, Rank: Rank(tag)))
            .Where(entry => entry.Rank >= 0)
            .OrderBy(entry => entry.Rank)
            .ThenByDescending(entry => entry.Tag.NoteCount)
            .ThenBy(entry => entry.Tag.Name, StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.None))
            .Select(entry => entry.Tag)
            .Take(limit)
            .ToList();
        return matches.Count == 1 && matches[0].Name == typed ? [] : matches;
    }

    /// <summary>The text with the partial tag replaced by the whole tag and a space, and where the caret goes.</summary>
    /// <param name="text">The text.</param>
    /// <param name="query">The tag being typed.</param>
    /// <param name="caret">The caret's offset.</param>
    /// <param name="tag">The chosen tag.</param>
    /// <returns>The new text and caret.</returns>
    public static (string Text, int Caret) Insert(string text, TagQuery query, int caret, string tag)
    {
        var after = text[caret..];
        var spacer = after.StartsWith(' ') || after.StartsWith('\n') ? "" : " ";
        var inserted = $"#{tag}{spacer}";
        return (text[..query.Start] + inserted + after, query.Start + inserted.Length + (spacer.Length > 0 ? 0 : 1));
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}_/#&])#([\p{L}\p{N}_][\p{L}\p{N}_/-]{0,63})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex PartialTag();

    [GeneratedRegex(@"^[\p{L}\p{N}_/-]", RegexOptions.CultureInvariant)]
    private static partial Regex TagCharacter();
}
