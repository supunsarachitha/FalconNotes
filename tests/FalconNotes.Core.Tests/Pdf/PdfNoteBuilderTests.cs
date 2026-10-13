using FalconNotes.Core.Domain;
using FalconNotes.Core.Pdf;

namespace FalconNotes.Core.Tests.Pdf;

// New: the web app has no Export to PDF (docs/04, Export to PDF). What a note's PDF shows, before any page is drawn.
public class PdfNoteBuilderTests
{
    private static readonly DateTime Written = new(2026, 9, 28, 14, 30, 0, DateTimeKind.Utc);

    /// <summary>Each block on a line: quote marks, two spaces per list level, the marker or box, the style, the text.</summary>
    private static string[] Blocks(string markdown, bool lineBreaks = false) => PdfNoteBuilder.Body(markdown, lineBreaks).Select(Show).ToArray();

    private static string Show(PdfBlock block) => block switch
    {
        PdfText text => new string('>', text.Quote) + new string(' ', text.Indent * 2)
            + (text.Checked is { } done ? (done ? "[x] " : "[ ] ") : text.Marker is { } marker ? marker + " " : "")
            + $"{text.Style}: {Show(text.Spans)}",
        PdfRule => "---",
        PdfTable table => "table: " + string.Join(" / ", table.Rows.Select(row => string.Join(" | ", row.Select(Show)))),
        PdfPicture picture => $"picture: {picture.FileName}",
        _ => throw new InvalidOperationException(),
    };

    /// <summary>Styled runs as <c>&lt;b:text&gt;</c>, with a letter for each of bold, italic, strikethrough, code and link.</summary>
    private static string Show(IReadOnlyList<PdfSpan> spans) => string.Concat(spans.Select(span =>
    {
        var style = (span.Bold ? "b" : "") + (span.Italic ? "i" : "") + (span.Strikethrough ? "s" : "") + (span.Code ? "c" : "") + (span.Link ? "l" : "");
        return style.Length == 0 ? span.Text : $"<{style}:{span.Text}>";
    }));

    private static Note MakeNote(string content, DateTime? updated = null, params Attachment[] files) => new(
        Guid.CreateVersion7(), NoteKind.Note, content, null, false, null, null, Written, updated ?? Written, 0, [], files, []);

    private static Attachment File(string name, string type) => new(Guid.CreateVersion7(), null, name, type, 100, Written, 0);

    [Fact]
    public void Headings_paragraphs_and_emphasis_keep_their_styles()
    {
        Assert.Equal(
            [
                "Heading1: Trip to Algonquin",
                "Body: Pack the <b:camping> gear, <i:not> the <s:tent>, and <bi:both>.",
                "Heading2: Day one",
                "Heading3: Morning",
                "Heading4: Deep",
            ],
            Blocks("# Trip to Algonquin\n\nPack the **camping** gear, *not* the ~~tent~~, and ***both***.\n\n## Day one\n\n### Morning\n\n###### Deep"));
    }

    [Fact]
    public void A_single_line_break_joins_the_lines_as_on_the_screen_and_a_hard_one_breaks()
    {
        Assert.Equal(["Body: one two"], Blocks("one\ntwo"));
        Assert.Equal(["Body: one\ntwo"], Blocks("one  \ntwo"));
        Assert.Equal(["Body: one", "Body: two"], Blocks("one\n\ntwo"));
    }

    // Keep line breaks (docs/04, Markdown rendering): the PDF breaks where the screen does.
    [Fact]
    public void Kept_line_breaks_break_the_line_in_a_paragraph_and_in_a_list_item()
    {
        Assert.Equal(["Body: one\ntwo"], Blocks("one\ntwo", lineBreaks: true));
        Assert.Equal(["Body: one", "Body: two"], Blocks("one\n\ntwo", lineBreaks: true));
        Assert.Equal(["  [ ] Body: bread\nand <b:butter>", "  • Body: tea"], Blocks("- [ ] bread\n  and **butter**\n- tea", lineBreaks: true));
        Assert.Equal(["Code: first\nsecond"], Blocks("```\nfirst\nsecond\n```", lineBreaks: true));
    }

    [Fact]
    public void A_note_is_laid_out_with_its_line_breaks_kept_when_asked()
    {
        var note = MakeNote("one\ntwo");

        Assert.Contains("Body: one two", PdfNoteBuilder.Build(note, note.Content, TimeZoneInfo.Utc, use24Hour: true).Blocks.Select(Show));
        Assert.Contains("Body: one\ntwo", PdfNoteBuilder.Build(note, note.Content, TimeZoneInfo.Utc, use24Hour: true, lineBreaks: true).Blocks.Select(Show));
    }

    [Fact]
    public void Lists_have_bullets_numbers_and_levels_and_task_items_a_box()
    {
        Assert.Equal(
            [
                "  • Body: oats",
                "  • Body: fruit",
                "    • Body: apples",
                "  3. Body: third",
                "  4. Body: fourth",
                "  [ ] Body: book a time",
                "  [x] Body: call the <b:plumber>",
            ],
            Blocks("- oats\n- fruit\n  - apples\n\n3. third\n4. fourth\n\n- [ ] book a time\n- [x] call the **plumber**"));
    }

    [Fact]
    public void Only_the_first_paragraph_of_a_list_item_has_its_marker()
    {
        Assert.Equal(["  1. Body: first", "  Body: more of it", "  2. Body: second"], Blocks("1. first\n\n   more of it\n\n2. second"));
        Assert.Equal(["  • Body: "], Blocks("-"));
    }

    [Fact]
    public void A_todo_list_is_its_title_and_its_boxes()
    {
        var list = Core.Text.Todo.Serialize(new Core.Text.TodoList("Groceries", [new("oats", true), new("maple syrup", false)]));

        Assert.Equal(["Heading1: Groceries", "  [x] Body: oats", "  [ ] Body: maple syrup"], Blocks(list));
    }

    [Fact]
    public void Quotes_are_counted_and_code_keeps_its_lines()
    {
        Assert.Equal(
            [
                ">Body: said",
                ">>Body: said before",
                "Code: if (a < b)\n    return;",
                "Body: Run <c:dotnet test> now.",
                "---",
            ],
            Blocks("> said\n>\n> > said before\n\n```csharp\nif (a < b)\n    return;\n```\n\nRun `dotnet test` now.\n\n---"));
    }

    [Fact]
    public void A_link_is_followed_by_its_address_where_the_text_does_not_show_it()
    {
        Assert.Equal(["Body: See <l:the guide> (https://example.com/guide) first."], Blocks("See [the guide](https://example.com/guide) first."));
        Assert.Equal(["Body: <l:https://example.com>"], Blocks("https://example.com"));
        Assert.Equal(["Body: <l:https://example.com>"], Blocks("[https://example.com](https://example.com)"));
        Assert.Equal(["Body: <l:Features>"], Blocks("[Features](/settings/features)")); // a page of the app: no address to give
        Assert.Equal(["Body: <l:click>"], Blocks("[click](javascript:alert(1))")); // an address the screen refuses too
    }

    [Fact]
    public void A_picture_in_the_text_is_its_description_and_raw_html_stays_text()
    {
        Assert.Equal(["Body: Look: a cat"], Blocks("Look: ![a cat](https://example.com/cat.png)"));
        Assert.Equal(["Body: <b>not bold</b> & fine"], Blocks("<b>not bold</b> &amp; fine"));
        Assert.Equal(["Body: #tag stays as written"], Blocks("#tag stays as written"));
    }

    [Fact]
    public void A_table_keeps_its_rows_and_cells()
    {
        Assert.Equal(
            ["table: Item | Price / Oats | <b:3> / Syrup | 12"],
            Blocks("| Item | Price |\n|---|---|\n| Oats | **3** |\n| Syrup | 12 |"));
    }

    [Fact]
    public void Footnotes_follow_a_rule_under_their_numbers()
    {
        Assert.Equal(["Body: Water boils[1] at 100.", "---", "  1. Body: At sea level."], Blocks("Water boils[^a] at 100.\n\n[^a]: At sea level."));
    }

    [Fact]
    public void The_date_goes_under_the_title_or_at_the_top_and_says_when_the_note_was_edited()
    {
        var titled = PdfNoteBuilder.Build(MakeNote("# Groceries\n\noats"), "# Groceries\n\noats", TimeZoneInfo.Utc, use24Hour: true);
        Assert.Equal(["Heading1: Groceries", "Caption: Sep 28, 2026, 14:30", "Body: oats"], titled.Blocks.Select(Show));

        var plain = PdfNoteBuilder.Build(
            MakeNote("oats", Written.AddDays(4).AddHours(-5)), "oats", TimeZoneInfo.Utc, use24Hour: false);
        Assert.Equal(["Caption: Sep 28, 2026, 2:30 PM · edited Oct 2, 2026, 9:30 AM", "Body: oats"], plain.Blocks.Select(Show));

        // The card's text, not the saved note's: a tick still being saved is in the PDF.
        var ticked = PdfNoteBuilder.Build(MakeNote("- [ ] oats"), "- [x] oats", TimeZoneInfo.Utc, use24Hour: true);
        Assert.Equal("  [x] Body: oats", Show(ticked.Blocks[1]));
    }

    [Fact]
    public void Pictures_follow_the_text_and_other_files_are_named()
    {
        var note = MakeNote(
            "At the lake", null,
            File("lake.jpg", "image/jpeg"), File("clip.mp4", "video/mp4"), File("map.png", "image/png"), File("logo.svg", "image/svg+xml"),
            File("budget.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

        var pdf = PdfNoteBuilder.Build(note, note.Content, TimeZoneInfo.Utc, use24Hour: true);

        Assert.Equal(
            [
                "Caption: Sep 28, 2026, 14:30",
                "Body: At the lake",
                "picture: lake.jpg",
                "picture: map.png",
                "Caption: Attached files, not in this PDF: clip.mp4, logo.svg, budget.xlsx",
            ],
            pdf.Blocks.Select(Show));
        Assert.Equal(note.Attachments[0].Id, ((PdfPicture)pdf.Blocks[2]).AttachmentId);
    }

    [Fact]
    public void A_note_with_no_text_still_has_its_date()
    {
        var pdf = PdfNoteBuilder.Build(MakeNote(""), "", TimeZoneInfo.Utc, use24Hour: true);

        Assert.Equal(["Caption: Sep 28, 2026, 14:30"], pdf.Blocks.Select(Show));
    }
}
