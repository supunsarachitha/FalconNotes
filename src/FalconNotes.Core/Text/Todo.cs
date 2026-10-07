using System.Text.RegularExpressions;

namespace FalconNotes.Core.Text;

/// <summary>One item of a todo list.</summary>
/// <param name="Text">The item's text, on one line.</param>
/// <param name="Done">Whether it is ticked.</param>
public sealed record TodoItem(string Text, bool Done);

/// <summary>A todo list as the Todo page edits it: a title and its items.</summary>
/// <param name="Title">The list's name.</param>
/// <param name="Items">The items, in order.</param>
public sealed record TodoList(string Title, IReadOnlyList<TodoItem> Items);

/// <summary>
/// Todo lists are stored as Markdown: a <c># title</c>, then one <c>- [ ]</c> or <c>- [x]</c> line per item. Port of
/// <c>web/lib/todo.ts</c> (docs/04, Todo lists).
/// </summary>
public static partial class Todo
{
    /// <summary>The most characters in a list's name.</summary>
    public const int MaxTitleLength = 300;

    /// <summary>The most characters in a new item.</summary>
    public const int MaxItemLength = 500;

    /// <summary>
    /// Reads items from Markdown, one per line. Lines that are not <c>- [ ]</c> items (edited as text, say) become open
    /// items with any bullet removed; empty items are dropped.
    /// </summary>
    /// <param name="markdown">The items' text.</param>
    /// <returns>The items.</returns>
    public static IReadOnlyList<TodoItem> ParseItems(string markdown)
    {
        var items = new List<TodoItem>();
        foreach (var line in Lines().Split(markdown))
        {
            var match = Item().Match(line);
            if (match.Success)
            {
                var text = Titles.OneLine(match.Groups[2].Value);
                if (text.Length > 0)
                {
                    items.Add(new TodoItem(text, match.Groups[1].Value != " "));
                }
            }
            else if (line.Trim().Length > 0)
            {
                items.Add(new TodoItem(Titles.OneLine(Bullet().Replace(line, "", 1)), false));
            }
        }

        return items;
    }

    /// <summary>Writes items as Markdown, one <c>- [ ]</c> or <c>- [x]</c> line each; empty items are left out.</summary>
    /// <param name="items">The items.</param>
    /// <returns>The Markdown.</returns>
    public static string SerializeItems(IEnumerable<TodoItem> items) =>
        string.Join('\n', items
            .Where(item => Titles.OneLine(item.Text).Length > 0)
            .Select(item => $"- [{(item.Done ? 'x' : ' ')}] {Titles.OneLine(item.Text)}"));

    /// <summary>Reads a todo list from its note's text: the title, then the items.</summary>
    /// <param name="content">The note's text.</param>
    /// <returns>The list.</returns>
    public static TodoList Parse(string content)
    {
        var (title, body) = Titles.Split(content);
        return new TodoList(title, ParseItems(body));
    }

    /// <summary>Writes a todo list as Markdown. A list always has a title ("Untitled list"), so it is never empty.</summary>
    /// <param name="list">The list.</param>
    /// <returns>The note's text.</returns>
    public static string Serialize(TodoList list)
    {
        var title = Titles.OneLine(list.Title);
        return Titles.Join(title.Length > 0 ? title : "Untitled list", SerializeItems(list.Items));
    }

    [GeneratedRegex(@"^\s*[-*+]\s+\[([ xX])\]\s?(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Item();

    [GeneratedRegex(@"^\s*[-*+]\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex Lines();
}
