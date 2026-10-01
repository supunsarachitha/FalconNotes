using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Text;

// Ported from web/lib/tagSuggest.test.ts.
public class TagSuggestTests
{
    private static readonly TagCount[] Tags =
    [
        new("groceries", 4), new("garden", 9), new("work/meetings", 2), new("work", 7), new("meetup", 1),
    ];

    private static IEnumerable<string> Names(IEnumerable<TagCount> tags) => tags.Select(t => t.Name);

    [Fact]
    public void Finds_the_tag_being_typed_just_before_the_caret()
    {
        Assert.Equal(new TagQuery(4, "gro"), TagSuggest.QueryAt("Buy #Gro", 8));
        Assert.Equal(new TagQuery(0, ""), TagSuggest.QueryAt("#", 1));
        Assert.Equal(new TagQuery(4, "work/me"), TagSuggest.QueryAt("see #work/me", 12));
    }

    [Theory]
    [InlineData("issue#12", 8)]
    [InlineData("## Heading", 2)]
    [InlineData("#gro ", 5)]
    [InlineData("#groceries", 3)]
    [InlineData("plain text", 5)]
    public void Offers_nothing_where_a_hash_does_not_start_a_tag_or_the_caret_is_not_at_its_end(string text, int caret) =>
        Assert.Null(TagSuggest.QueryAt(text, caret));

    [Fact]
    public void Ranks_tags_that_start_with_what_was_typed_then_nested_parts_each_by_use()
    {
        Assert.Equal(["garden", "work", "groceries", "work/meetings", "meetup"], Names(TagSuggest.Suggest(Tags, "")));
        Assert.Equal(["garden", "groceries"], Names(TagSuggest.Suggest(Tags, "g")));
        Assert.Equal(["meetup", "work/meetings"], Names(TagSuggest.Suggest(Tags, "mee")));
        Assert.Equal(2, TagSuggest.Suggest(Tags, "", 2).Count);
    }

    [Fact]
    public void Has_nothing_to_add_once_the_whole_tag_is_typed()
    {
        Assert.Empty(TagSuggest.Suggest(Tags, "garden"));
        Assert.Equal(["work", "work/meetings"], Names(TagSuggest.Suggest(Tags, "work")));
    }

    [Fact]
    public void Puts_the_whole_tag_in_with_a_space_after_it_and_the_caret_after_that()
    {
        Assert.Equal(("Buy #groceries ", 15), TagSuggest.Insert("Buy #gro", new TagQuery(4, "gro"), 8, "groceries"));
        Assert.Equal(("#garden now", 8), TagSuggest.Insert("#ga now", new TagQuery(0, "ga"), 3, "garden"));
    }
}
