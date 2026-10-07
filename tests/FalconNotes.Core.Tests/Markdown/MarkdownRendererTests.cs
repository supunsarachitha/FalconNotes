using System.Text.RegularExpressions;
using FalconNotes.Core.Markdown;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Markdown;

// Ported from web/components/Markdown.test.tsx.
public partial class MarkdownRendererTests
{
    private static string Render(string content, bool interactive = true) => MarkdownRenderer.Render(content, interactive);

    [Fact]
    public void Renders_GitHub_flavoured_Markdown_in_a_markdown_div()
    {
        var html = Render("**bold** ~~old~~\n\n- [x] done\n- [ ] todo\n\n| a | b |\n|---|---|\n| 1 | 2 |");

        Assert.StartsWith("<div class=\"markdown\">", html, StringComparison.Ordinal);
        Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<del>old</del>", html, StringComparison.Ordinal);
        Assert.Equal(2, Checkboxes(html).Count);
        Assert.Contains("<table>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Ticks_task_items_only_when_the_note_can_be_changed_naming_each_box_after_its_item()
    {
        const string content = "Plan\n\n- [ ] book **flights**\n- [x] pack\n  - [ ] socks\n\n> 1. [ ] quoted";

        Assert.All(Checkboxes(Render(content, interactive: false)), box => Assert.Contains("disabled", box, StringComparison.Ordinal));

        var boxes = Checkboxes(Render(content));
        Assert.All(boxes, box => Assert.DoesNotContain("disabled", box, StringComparison.Ordinal));
        Assert.Equal(["book flights", "pack", "socks", "quoted"], boxes.Select(b => Attribute(b, "aria-label")));
        // Where each item's list marker starts in the text.
        Assert.Equal(
            new[] { "- [ ] book", "- [x] pack", "- [ ] socks", "1. [ ]" }.Select(marker => content.IndexOf(marker, StringComparison.Ordinal).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            boxes.Select(b => Attribute(b, "data-task")));
        Assert.Contains("checked", boxes[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("- [ ] one\n- [x] two")]
    [InlineData("Intro\r\n\r\n* [ ] crlf\r\n* [X] lines")]
    [InlineData("1. [ ] first\n2) [x] second\n\n   - [ ] nested deeper")]
    [InlineData("- [ ] loose\n\n- [x] items\n")]
    [InlineData("# Title\n\nText with #tag\n\n- [ ] item #tag")]
    public void Every_checkbox_offset_ticks_its_own_item(string content)
    {
        foreach (var box in Checkboxes(Render(content)))
        {
            var offset = int.Parse(Attribute(box, "data-task"), System.Globalization.CultureInfo.InvariantCulture);
            Assert.NotNull(MarkdownEdit.ToggleTask(content, offset)); // an item's marker starts there
            var ticked = content[content.IndexOf('[', offset) + 1] != ' ';
            Assert.Equal(ticked, box.Contains("checked", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Turns_tags_into_links_to_the_tag_view()
    {
        var html = Render("Plan the trip #Travel and #work/meetings.");

        Assert.Contains("<a href=\"/?tag=travel\" class=\"tag\">#Travel</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"/?tag=work%2Fmeetings\" class=\"tag\">#work/meetings</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Leaves_headings_numbers_URLs_links_and_code_alone()
    {
        var html = Render("# Heading\n\nIssue #42, https://example.com/#anchor, [a #link](https://x.org) and `#code`\n\n```\n#fenced\n```");

        Assert.DoesNotContain("class=\"tag\"", html, StringComparison.Ordinal);
        Assert.Contains("<h1", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Never_renders_raw_HTML_from_a_note()
    {
        var html = Render("<script>alert(1)</script><img src=x onerror=\"alert(2)\"> safe");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("safe", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Neutralises_javascript_and_data_links()
    {
        var html = Render("[click](javascript:alert(1)) [data](data:text/html;base64,PHNjcmlwdD4=) ![img](file:///etc/passwd) <javascript:alert(3)>");

        Assert.DoesNotMatch(new Regex("(href|src)=\"(javascript|data|file):", RegexOptions.IgnoreCase), html);
    }

    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("MAILTO:me@example.com", "MAILTO:me@example.com")]
    [InlineData("/?tag=work", "/?tag=work")]
    [InlineData("relative/path:with-colon", "relative/path:with-colon")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("vbscript:x", "")]
    [InlineData("data:text/html,x", "")]
    public void Keeps_only_safe_protocols(string url, string expected) => Assert.Equal(expected, MarkdownRenderer.SafeUrl(url));

    [Fact]
    public void Opens_external_links_elsewhere_and_internal_ones_in_the_app()
    {
        var html = Render("[docs](https://example.com) [help](/help) www.example.org");

        Assert.Matches("<a href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">docs</a>", html);
        Assert.Contains("<a href=\"/help\">help</a>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"http://www.example.org\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Cached_renders_follow_the_revision()
    {
        var renderer = new MarkdownRenderer();
        var id = Guid.NewGuid();

        Assert.Contains("one", renderer.Render(id, 1, "one", true), StringComparison.Ordinal);
        Assert.Contains("one", renderer.Render(id, 1, "two", true), StringComparison.Ordinal); // same revision: cached
        Assert.Contains("two", renderer.Render(id, 2, "two", true), StringComparison.Ordinal);
    }

    private static List<string> Checkboxes(string html) => CheckboxPattern().Matches(html).Select(m => m.Value).ToList();

    private static string Attribute(string element, string name) =>
        System.Net.WebUtility.HtmlDecode(Regex.Match(element, $"{name}=\"([^\"]*)\"").Groups[1].Value);

    [GeneratedRegex("<input type=\"checkbox\"[^>]*>")]
    private static partial Regex CheckboxPattern();
}
