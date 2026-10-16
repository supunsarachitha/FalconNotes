using System.Text;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Markdown;
using FalconNotes.Core.Text;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FalconNotes.Core.Pdf;

/// <summary>
/// Turns a note into what its PDF shows (docs/04, Export to PDF): its Markdown, read with the pipeline the screen
/// uses so the two never disagree about what is a heading or a list; when it was written; its pictures; and the
/// names of the files a page cannot show.
/// </summary>
public static class PdfNoteBuilder
{
    /// <summary>Lays a note out.</summary>
    /// <param name="note">The note: its dates and files.</param>
    /// <param name="content">Its text as the card shows it now, which may be a change still being saved.</param>
    /// <param name="zone">The device's time zone, for the dates.</param>
    /// <param name="use24Hour">Whether the device uses a 24-hour clock.</param>
    /// <param name="lineBreaks">Whether a single line break is a new line, as the screen shows it with Keep line breaks on.</param>
    /// <returns>What to draw.</returns>
    public static PdfNote Build(Note note, string content, TimeZoneInfo zone, bool use24Hour, bool lineBreaks = false)
    {
        var blocks = Body(content, lineBreaks);

        // Under the title when the note starts with one, as a letter is dated; otherwise at the very top.
        var written = RelativeTime.Absolute(note.CreatedAtUtc, zone, use24Hour);
        if (note.UpdatedAtUtc - note.CreatedAtUtc > TimeSpan.FromMinutes(1))
        {
            written += $" · edited {RelativeTime.Absolute(note.UpdatedAtUtc, zone, use24Hour)}";
        }

        blocks.Insert(
            blocks.Count > 0 && blocks[0] is PdfText { Style: PdfTextStyle.Heading1 } ? 1 : 0,
            new PdfText(PdfTextStyle.Caption, [new PdfSpan(written)]));

        blocks.AddRange(note.Attachments.Where(file => file.IsImage).Select(file => new PdfPicture(file.Id, file.FileName, file.IsSticker)));

        var others = note.Attachments.Where(file => !file.IsImage).Select(file => file.FileName).ToList();
        if (others.Count > 0)
        {
            blocks.Add(new PdfText(PdfTextStyle.Caption, [new PdfSpan($"Attached files, not in this PDF: {string.Join(", ", others)}")]));
        }

        return new PdfNote(blocks);
    }

    /// <summary>Reads Markdown into blocks, as <see cref="MarkdownRenderer"/> reads it into HTML.</summary>
    /// <param name="content">The Markdown.</param>
    /// <param name="lineBreaks">Whether a single line break is a new line, as the screen shows it with Keep line breaks on.</param>
    /// <returns>The blocks.</returns>
    public static List<PdfBlock> Body(string content, bool lineBreaks = false)
    {
        var blocks = new List<PdfBlock>();
        AddBlocks(blocks, MarkdownRenderer.Parse(content, lineBreaks), 0, 0, null);
        return blocks;
    }

    private static void AddBlocks(List<PdfBlock> blocks, ContainerBlock container, int indent, int quote, ItemStart? start)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    AddText(blocks, HeadingStyle(heading.Level), heading.Inline, indent, quote, start);
                    break;
                case CodeBlock code:
                    blocks.Add(new PdfText(PdfTextStyle.Code, [new PdfSpan(Lines(code))], indent, start?.TakeMarker(), null, quote));
                    break;
                case ListBlock list:
                    var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var first) ? first : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var itemStart = new ItemStart(list.IsOrdered ? $"{number++}." : "•");
                        AddBlocks(blocks, item, indent + 1, quote, itemStart);
                        if (itemStart.TakeMarker() is { } unused)
                        {
                            blocks.Add(new PdfText(PdfTextStyle.Body, [], indent + 1, unused, null, quote)); // an item with nothing in it
                        }
                    }

                    break;
                case QuoteBlock quoted:
                    AddBlocks(blocks, quoted, indent, quote + 1, start);
                    break;
                case ThematicBreakBlock:
                    blocks.Add(new PdfRule());
                    break;
                case Table table:
                    blocks.Add(new PdfTable(table.OfType<TableRow>()
                        .Select(row => (IReadOnlyList<IReadOnlyList<PdfSpan>>)row.OfType<TableCell>().Select(Cell).ToList())
                        .ToList()));
                    break;
                case FootnoteGroup footnotes:
                    blocks.Add(new PdfRule());
                    foreach (var footnote in footnotes.OfType<Footnote>())
                    {
                        AddBlocks(blocks, footnote, indent + 1, quote, new ItemStart($"{footnote.Order}."));
                    }

                    break;
                case LinkReferenceDefinitionGroup:
                    break; // the targets of [text][ref] links: nothing to show
                case ContainerBlock other:
                    AddBlocks(blocks, other, indent, quote, start);
                    break;
                case LeafBlock { Inline: not null } leaf:
                    AddText(blocks, PdfTextStyle.Body, leaf.Inline, indent, quote, start);
                    break;
                case LeafBlock leaf when leaf.Lines.Count > 0:
                    blocks.Add(new PdfText(PdfTextStyle.Body, [new PdfSpan(Lines(leaf))], indent, start?.TakeMarker(), null, quote));
                    break;
            }
        }
    }

    private static void AddText(List<PdfBlock> blocks, PdfTextStyle style, ContainerInline? inline, int indent, int quote, ItemStart? start)
    {
        // A task item's box stands where its bullet or number would.
        var task = inline?.FirstChild as TaskList;
        var marker = start?.TakeMarker();
        var spans = new SpanList();
        if (inline is not null)
        {
            AddInlines(spans, inline, default);
        }

        var isTask = task is not null && marker is not null;
        blocks.Add(new PdfText(style, spans.ToList(), indent, isTask ? null : marker, isTask ? task!.Checked : null, quote));
    }

    private static void AddInlines(SpanList spans, ContainerInline container, Style style)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case TaskList:
                    break; // drawn as the item's box
                case LiteralInline literal:
                    spans.Add(literal.Content.ToString(), style);
                    break;
                case CodeInline code:
                    spans.Add(code.Content, style with { Code = true });
                    break;
                case LineBreakInline lineBreak:
                    spans.Add(lineBreak.IsHard ? "\n" : " ", style); // a single line break joins the lines unless they are kept, as on the screen
                    break;
                case AutolinkInline autolink:
                    spans.Add(autolink.Url, style with { Link = true });
                    break;
                case HtmlEntityInline entity:
                    spans.Add(entity.Transcoded.ToString(), style);
                    break;
                case FootnoteLink { IsBackLink: false } footnote:
                    spans.Add($"[{footnote.Footnote.Order}]", style);
                    break;
                case FootnoteLink:
                    break;
                case LinkInline { IsImage: true } image:
                    spans.Add(PlainText(image), style); // the note's own pictures follow its text; any other is not fetched
                    break;
                case LinkInline link:
                    AddInlines(spans, link, style with { Link = true });

                    // Paper cannot be tapped: an address the text does not already show is written out after it.
                    if (MarkdownRenderer.SafeUrl(link.Url) is { Length: > 0 } url && !MarkdownRenderer.IsAppPath(url)
                        && !url.StartsWith('#') && !string.Equals(PlainText(link).Trim(), url, StringComparison.OrdinalIgnoreCase))
                    {
                        spans.Add($" ({url})", style);
                    }

                    break;
                case EmphasisInline emphasis:
                    AddInlines(spans, emphasis, emphasis.DelimiterChar == '~'
                        ? style with { Strikethrough = true }
                        : emphasis.DelimiterCount >= 2 ? style with { Bold = true } : style with { Italic = true });
                    break;
                case DelimiterInline delimiter:
                    spans.Add(delimiter.ToLiteral(), style); // a mark that opened nothing is plain text
                    AddInlines(spans, delimiter, style);
                    break;
                case ContainerInline nested:
                    AddInlines(spans, nested, style);
                    break;
            }
        }
    }

    private static IReadOnlyList<PdfSpan> Cell(TableCell cell)
    {
        var spans = new SpanList();
        foreach (var leaf in cell.Descendants<LeafBlock>())
        {
            if (leaf.Inline is not null)
            {
                spans.Add(spans.IsEmpty ? "" : "\n", default);
                AddInlines(spans, leaf.Inline, default);
            }
        }

        return spans.ToList();
    }

    private static string PlainText(ContainerInline container)
    {
        var text = new StringBuilder();
        foreach (var inline in container.Descendants())
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
            }
        }

        return text.ToString();
    }

    private static string Lines(LeafBlock block)
    {
        var lines = block.Lines;
        return string.Join('\n', Enumerable.Range(0, lines.Count).Select(i => lines.Lines[i].Slice.ToString()));
    }

    private static PdfTextStyle HeadingStyle(int level) => level switch
    {
        1 => PdfTextStyle.Heading1,
        2 => PdfTextStyle.Heading2,
        3 => PdfTextStyle.Heading3,
        _ => PdfTextStyle.Heading4,
    };

    private readonly record struct Style(bool Bold, bool Italic, bool Strikethrough, bool Code, bool Link);

    /// <summary>A list item's bullet or number, until the first text in the item has taken it.</summary>
    private sealed class ItemStart(string marker)
    {
        private string? _marker = marker;

        public string? TakeMarker()
        {
            var marker = _marker;
            _marker = null;
            return marker;
        }
    }

    /// <summary>Collects runs of text, joining neighbours in the same style and trimming the block's ends.</summary>
    private sealed class SpanList
    {
        private readonly List<(StringBuilder Text, Style Style)> _runs = [];

        public bool IsEmpty => _runs.Count == 0;

        public void Add(string text, Style style)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (_runs.Count > 0 && _runs[^1].Style == style)
            {
                _runs[^1].Text.Append(text);
            }
            else
            {
                _runs.Add((new StringBuilder(text), style));
            }
        }

        public List<PdfSpan> ToList()
        {
            var spans = new List<PdfSpan>(_runs.Count);
            for (var i = 0; i < _runs.Count; i++)
            {
                var text = _runs[i].Text.ToString();
                text = i == 0 ? text.TrimStart() : text;
                text = i == _runs.Count - 1 ? text.TrimEnd() : text;
                var style = _runs[i].Style;
                if (text.Length > 0)
                {
                    spans.Add(new PdfSpan(text, style.Bold, style.Italic, style.Strikethrough, style.Code, style.Link));
                }
            }

            return spans;
        }
    }
}
