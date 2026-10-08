using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FalconNotes.Core.Markdown;

/// <summary>
/// Renders note Markdown to HTML as the web app's <c>Markdown.tsx</c> does (docs/04, Markdown rendering): GitHub
/// flavour (tables, task lists, strikethrough, autolinks, footnotes), raw HTML shown as text, links limited to safe
/// protocols, images limited to the app's own files, <c>#tags</c> as links to their notes, and task checkboxes that
/// know where their item starts.
/// </summary>
public sealed partial class MarkdownRenderer
{
    private const int CacheLimit = 1000;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UseAutoLinks()
        .UseFootnotes()
        .UsePreciseSourceLocation()
        .DisableHtml()
        .Build();

    private readonly ConcurrentDictionary<(Guid Id, long Revision, int Length, bool Interactive), string> _cache = new();

    /// <summary>
    /// Renders a note, cached by its ID and revision so long lists do not render the same Markdown again. The text's
    /// length is part of the key because one revision is shown two ways: whole, or without its title line when note
    /// titles are on, and the setting can change while the note is cached.
    /// </summary>
    /// <param name="noteId">The note.</param>
    /// <param name="revision">Its revision.</param>
    /// <param name="content">Its text.</param>
    /// <param name="interactive">Whether task checkboxes can be ticked (not in archived notes).</param>
    /// <returns>The HTML.</returns>
    public string Render(Guid noteId, long revision, string content, bool interactive)
    {
        if (_cache.Count > CacheLimit)
        {
            _cache.Clear();
        }

        return _cache.GetOrAdd((noteId, revision, content.Length, interactive), _ => Render(content, interactive));
    }

    /// <summary>Renders Markdown, wrapped in <c>&lt;div class="markdown"&gt;</c>.</summary>
    /// <param name="content">The Markdown.</param>
    /// <param name="interactive">
    /// Whether task checkboxes can be ticked. Each has <c>data-task</c>, the offset in <paramref name="content"/> where
    /// its item's list marker starts (see <c>MarkdownEdit.ToggleTask</c>), and is named after its item.
    /// </param>
    /// <returns>The HTML.</returns>
    public static string Render(string content, bool interactive)
    {
        var document = Markdig.Markdown.Parse(content, Pipeline);
        foreach (var node in document.Descendants().ToList())
        {
            switch (node)
            {
                case LinkInline { IsImage: true } image:
                    ShowOwnFileOrDescription(image);
                    break;
                case LinkInline link:
                    link.Url = SafeUrl(link.Url);
                    if (link.Url is { } url && !IsAppPath(url))
                    {
                        OpenElsewhere(link);
                    }

                    break;
                case AutolinkInline autolink:
                    autolink.Url = SafeUrl(autolink.Url) ?? "";
                    OpenElsewhere(autolink);
                    break;
                case LiteralInline literal when !InsideLink(literal):
                    LinkTags(literal);
                    break;
            }
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.RemoveAll(r => r is HtmlTaskListRenderer);
        renderer.ObjectRenderers.Add(new TaskCheckboxRenderer(interactive));
        writer.Write("<div class=\"markdown\">");
        renderer.Render(document);
        writer.Write("</div>");
        return writer.ToString();
    }

    /// <summary>
    /// react-markdown's default <c>urlTransform</c>: relative URLs and the http, https, mailto, xmpp, irc and ircs
    /// protocols are kept; anything else (<c>javascript:</c>, <c>data:</c>, <c>file:</c>…) becomes empty.
    /// </summary>
    /// <param name="url">The URL as written.</param>
    /// <returns>The URL, or an empty string.</returns>
    public static string? SafeUrl(string? url)
    {
        if (url is null)
        {
            return null;
        }

        var colon = url.IndexOf(':', StringComparison.Ordinal);
        var question = url.IndexOf('?', StringComparison.Ordinal);
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        var slash = url.IndexOf('/', StringComparison.Ordinal);
        return colon == -1
               || (slash != -1 && colon > slash)
               || (question != -1 && colon > question)
               || (hash != -1 && colon > hash)
               || SafeProtocol().IsMatch(url[..colon])
            ? url
            : "";
    }

    /// <summary>
    /// An address in the app itself: a path, but not <c>//host/…</c> or <c>/\host</c>, which a browser reads as
    /// another site.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <returns>Whether it is a page of the app.</returns>
    public static bool IsAppPath(string url) => url.StartsWith('/') && !(url.Length > 1 && url[1] is '/' or '\\');

    /// <summary>
    /// The app's own attached files are the only images a note shows, so nothing else is asked for just by viewing a
    /// note. Any other image shows its description instead, or nothing when it has none.
    /// </summary>
    private static void ShowOwnFileOrDescription(LinkInline image)
    {
        if (image.Url is { } url && OwnFile().IsMatch(url))
        {
            image.GetAttributes().AddPropertyIfNotExist("loading", "lazy");
            return;
        }

        var description = string.Concat(image.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()));
        if (description.Length > 0)
        {
            image.ReplaceBy(new LiteralInline(description));
        }
        else
        {
            image.Remove();
        }
    }

    private static void OpenElsewhere(Inline link)
    {
        var attributes = link.GetAttributes();
        attributes.AddPropertyIfNotExist("target", "_blank");
        attributes.AddPropertyIfNotExist("rel", "noopener noreferrer nofollow");
    }

    private static bool InsideLink(Inline inline)
    {
        for (var parent = inline.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is LinkInline)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Splits a run of text around its <c>#tags</c>, turning each into a link to the tag's notes.</summary>
    private static void LinkTags(LiteralInline literal)
    {
        var text = literal.Content.ToString();
        var matches = TagPattern().Matches(text);
        if (matches.Count == 0)
        {
            return;
        }

        Inline current = literal;
        var position = 0;
        var replaced = new List<Inline>();
        foreach (Match match in matches)
        {
            var tag = match.Groups[1].Value.TrimEnd('/', '-').ToLowerInvariant();
            if (tag.Length == 0 || AsciiDigits().IsMatch(tag))
            {
                continue;
            }

            if (match.Index > position)
            {
                replaced.Add(new LiteralInline(text[position..match.Index]));
            }

            var link = new LinkInline($"/?tag={Uri.EscapeDataString(tag)}", "");
            link.GetAttributes().AddClass("tag");
            link.AppendChild(new LiteralInline(match.Value));
            replaced.Add(link);
            position = match.Index + match.Length;
        }

        if (replaced.Count == 0)
        {
            return;
        }

        if (position < text.Length)
        {
            replaced.Add(new LiteralInline(text[position..]));
        }

        foreach (var inline in replaced)
        {
            current.InsertAfter(inline);
            current = inline;
        }

        literal.Remove();
    }

    // Mirrors TagParser: a "#" not preceded by a letter, digit, "_", "/", "#" or "&".
    [GeneratedRegex(@"(?<![\p{L}\p{N}_/#&])#([\p{L}\p{N}_][\p{L}\p{N}_/-]{0,63})", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AsciiDigits();

    [GeneratedRegex("^(https?|ircs?|mailto|xmpp)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SafeProtocol();

    // The media handler's address for an attachment (MediaUrl in the UI project; docs/02, Serving attachments).
    [GeneratedRegex(@"^/_media/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?:\?[\w=&.-]*)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OwnFile();

    /// <summary>
    /// Renders task checkboxes enabled (or disabled when the note cannot be changed), with <c>data-task</c> set to where
    /// the list item starts and <c>aria-label</c> to the item's text without its nested lists.
    /// </summary>
    private sealed class TaskCheckboxRenderer(bool interactive) : HtmlObjectRenderer<TaskList>
    {
        protected override void Write(HtmlRenderer renderer, TaskList task)
        {
            var item = task.Parent?.ParentBlock?.Parent as ListItemBlock;
            renderer.Write("<input type=\"checkbox\"");
            if (task.Checked)
            {
                renderer.Write(" checked=\"checked\"");
            }

            if (!interactive || item is null)
            {
                renderer.Write(" disabled=\"disabled\"");
            }

            if (item is not null)
            {
                renderer.Write($" data-task=\"{item.Span.Start}\"");
                var label = Text.Titles.OneLine(ItemText(item));
                if (label.Length > 0)
                {
                    renderer.Write($" aria-label=\"{WebUtility.HtmlEncode(label)}\"");
                }
            }

            renderer.Write(" />");
        }

        private static string ItemText(ContainerBlock block)
        {
            var text = new StringBuilder();
            foreach (var child in block)
            {
                switch (child)
                {
                    case ListBlock:
                        break;
                    case LeafBlock { Inline: { } inline }:
                        AppendText(text, inline);
                        text.Append(' ');
                        break;
                    case ContainerBlock container:
                        text.Append(ItemText(container));
                        break;
                }
            }

            return text.ToString();
        }

        private static void AppendText(StringBuilder text, ContainerInline container)
        {
            foreach (var inline in container)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        text.Append(literal.Content.ToString());
                        break;
                    case CodeInline code:
                        text.Append(code.Content);
                        break;
                    case LineBreakInline:
                        text.Append(' ');
                        break;
                    case ContainerInline nested:
                        AppendText(text, nested);
                        break;
                }
            }
        }
    }
}
