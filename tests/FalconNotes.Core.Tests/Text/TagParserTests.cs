using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Text;

// Ported from the server's TagParserTests (Notes/NoteHelpersTests.cs).
public class TagParserTests
{
    [Theory]
    [InlineData("Buy milk #groceries", new[] { "groceries" })]
    [InlineData("#Work and #work and #WORK", new[] { "work" })]
    [InlineData("#work/meetings/weekly notes", new[] { "work/meetings/weekly" })]
    [InlineData("Unicode #café #日本", new[] { "café", "日本" })]
    [InlineData("#a, #b. #c!", new[] { "a", "b", "c" })]
    [InlineData("trailing #slash/ and #dash-", new[] { "slash", "dash" })]
    public void Finds_tags(string markdown, string[] expected) => Assert.Equal(expected, TagParser.Extract(markdown));

    [Fact]
    public void Pathological_input_cannot_stall_tag_parsing()
    {
        var hostile = string.Concat(Enumerable.Repeat("```#a`", 20_000)) + new string('#', 50_000);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        TagParser.Extract(hostile);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("# Heading")]
    [InlineData("## Second level")]
    [InlineData("see https://example.com/page#section")]
    [InlineData("see example.com/#anchor")]
    [InlineData("an&#39;entity")]
    [InlineData("issue #123")]
    [InlineData("word#nottag")]
    [InlineData("`#inline` code")]
    [InlineData("```\n#fenced code\n```")]
    [InlineData("```\n#unclosed fence")]
    public void Ignores_things_that_are_not_tags(string markdown) => Assert.Empty(TagParser.Extract(markdown));
}
