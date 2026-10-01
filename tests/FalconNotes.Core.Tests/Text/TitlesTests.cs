using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Text;

// Ported from web/lib/titles.test.ts.
public class TitlesTests
{
    [Fact]
    public void Reads_a_first_line_heading_as_the_title()
    {
        Assert.Equal(("Trip to Algonquin", "Pack the #camping gear"), Titles.Split("# Trip to Algonquin\n\nPack the #camping gear"));
        Assert.Equal(("Only a title", ""), Titles.Split("# Only a title"));
        Assert.Equal(("Closing hashes", "body"), Titles.Split("# Closing hashes ##\r\nbody"));
    }

    [Theory]
    [InlineData("Just text")]
    [InlineData("## Second-level heading\nbody")]
    [InlineData("#hashtag first")]
    [InlineData("#\nbody")]
    [InlineData("")]
    [InlineData("text\n# heading later")]
    public void Leaves_text_without_a_first_line_heading_alone(string text) =>
        Assert.Equal(("", text), Titles.Split(text));

    [Fact]
    public void Writes_the_title_as_a_heading_and_round_trips()
    {
        Assert.Equal("# Weekend plans\n\nHike\nSwim", Titles.Join("  Weekend   plans ", "Hike\nSwim"));
        Assert.Equal("# Title only", Titles.Join("Title only", "  "));
        Assert.Equal("No title", Titles.Join("", "No title"));
        var note = Titles.Join("Groceries", "- [ ] maple syrup\n- [x] oats");
        Assert.Equal(("Groceries", "- [ ] maple syrup\n- [x] oats"), Titles.Split(note));
    }
}
