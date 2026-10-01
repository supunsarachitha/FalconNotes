using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Text;

// Ported from web/lib/markdownEdit.test.ts.
public class MarkdownEditTests
{
    // Formats text where [ and ] mark the selection; returns the result with the new selection marked the same way.
    private static string Run(MarkdownFormat format, string marked)
    {
        var start = marked.IndexOf('[', StringComparison.Ordinal);
        var end = marked.IndexOf(']', StringComparison.Ordinal) - 1;
        var text = marked.Remove(marked.IndexOf('[', StringComparison.Ordinal), 1);
        text = text.Remove(text.IndexOf(']', StringComparison.Ordinal), 1);
        var edit = MarkdownEdit.Format(format, text, start, end);
        var result = MarkdownEdit.Apply(text, edit);
        return result[..edit.SelectStart] + "[" + result[edit.SelectStart..edit.SelectEnd] + "]" + result[edit.SelectEnd..];
    }

    [Fact]
    public void Wraps_and_unwraps_the_selection()
    {
        Assert.Equal("Buy **[maple]** syrup", Run(MarkdownFormat.Bold, "Buy [maple] syrup"));
        Assert.Equal("Buy [maple] syrup", Run(MarkdownFormat.Bold, "Buy **[maple]** syrup"));
        Assert.Equal("Buy [maple] syrup", Run(MarkdownFormat.Bold, "Buy [**maple**] syrup"));
        Assert.Equal("a *[b]* c", Run(MarkdownFormat.Italic, "a [b] c"));
        Assert.Equal("run `[npm test]` now", Run(MarkdownFormat.Code, "run [npm test] now"));
    }

    [Fact]
    public void Inserts_a_placeholder_to_type_over_when_nothing_is_selected()
    {
        Assert.Equal("Hello **[bold text]**", Run(MarkdownFormat.Bold, "Hello []"));
        Assert.Equal("See [link text]([https://])", Run(MarkdownFormat.Link, "See []"));
        Assert.Equal("See [the docs]([https://])", Run(MarkdownFormat.Link, "See [the docs]"));
    }

    [Fact]
    public void Makes_a_code_block_from_several_lines() =>
        Assert.Equal("```\n[a\nb]\n```", Run(MarkdownFormat.Code, "[a\nb]"));

    [Fact]
    public void Adds_and_removes_line_prefixes_keeping_the_cursor_in_place()
    {
        Assert.Equal("## Title[]", Run(MarkdownFormat.Heading, "Title[]"));
        Assert.Equal("Tit[]le", Run(MarkdownFormat.Heading, "## Tit[]le"));
        Assert.Equal("one\n[- two\n- three]", Run(MarkdownFormat.Bullets, "one\n[two\nthree]"));
        Assert.Equal("[two\nthree]", Run(MarkdownFormat.Bullets, "[- two\n- three]"));
        Assert.Equal("- [ ] mi[]lk", Run(MarkdownFormat.Checklist, "- mi[]lk"));
        Assert.Equal("[> x\n> y]", Run(MarkdownFormat.Quote, "[x\n> y]"));
    }

    private const string TaskText = "Plan\n\n- [ ] one\n- [x] two\n  * [X] nested\n\n> 3. [ ] quoted";

    [Fact]
    public void Ticks_and_unticks_the_item_that_starts_at_an_offset()
    {
        Assert.Equal(TaskText.Replace("- [ ] one", "- [x] one"), MarkdownEdit.ToggleTask(TaskText, TaskText.IndexOf("- [ ] one", StringComparison.Ordinal)));
        Assert.Equal(TaskText.Replace("- [x] two", "- [ ] two"), MarkdownEdit.ToggleTask(TaskText, TaskText.IndexOf("- [x] two", StringComparison.Ordinal)));
        Assert.Equal(TaskText.Replace("* [X]", "* [ ]"), MarkdownEdit.ToggleTask(TaskText, TaskText.IndexOf("* [X]", StringComparison.Ordinal)));
        Assert.Equal(TaskText.Replace("3. [ ]", "3. [x]"), MarkdownEdit.ToggleTask(TaskText, TaskText.IndexOf("3. [ ]", StringComparison.Ordinal)));
    }

    [Fact]
    public void Changes_nothing_where_no_item_starts()
    {
        Assert.Null(MarkdownEdit.ToggleTask(TaskText, 0));
        Assert.Null(MarkdownEdit.ToggleTask(TaskText, TaskText.IndexOf("[ ] one", StringComparison.Ordinal)));
        Assert.Null(MarkdownEdit.ToggleTask("- plain item", 0));
        Assert.Null(MarkdownEdit.ToggleTask("- [ ] x", 99));
    }
}
