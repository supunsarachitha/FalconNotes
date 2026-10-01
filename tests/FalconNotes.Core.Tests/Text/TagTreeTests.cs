using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Text;

// Ported from the tagTree case of web/pages/TagsPage.test.tsx.
public class TagTreeTests
{
    private static readonly TagCount[] Tags = [new("ideas", 2), new("work", 1), new("work/meetings", 5), new("travel/japan", 3)];

    [Fact]
    public void Nests_tags_under_their_parents_with_totals_for_ordering()
    {
        var tree = TagTree.Build(Tags, TagOrder.Count);

        Assert.Equal([("work", 1, 6), ("travel", null, 3), ("ideas", (int?)2, 2)], tree.Select(n => (n.Path, n.Count, n.Total)));
        Assert.Equal([("meetings", (int?)5)], tree[0].Children.Select(n => (n.Label, n.Count)));
        Assert.Equal(["ideas", "travel", "work"], TagTree.Build(Tags, TagOrder.Name).Select(n => n.Path));
    }
}
