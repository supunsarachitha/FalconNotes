using System.Globalization;

namespace FalconNotes.Core.Text;

/// <summary>How the Tags page orders tags.</summary>
public enum TagOrder
{
    /// <summary>A–Z.</summary>
    Name,

    /// <summary>Most used first (totals include nested tags).</summary>
    Count,
}

/// <summary>A tag on the Tags page, with the tags nested under it.</summary>
/// <param name="Path">The full tag, e.g. <c>work/meetings</c>.</param>
/// <param name="Label">The last part, e.g. <c>meetings</c>.</param>
/// <param name="Count">Notes with exactly this tag; null for a parent that only exists through its nested tags.</param>
/// <param name="Total">This tag's notes plus those of every tag below it.</param>
/// <param name="Children">The nested tags.</param>
public sealed record TagNode(string Path, string Label, int? Count, int Total, IReadOnlyList<TagNode> Children);

/// <summary>Arranges tags as a tree by their <c>/</c> parts. Port of <c>tagTree</c> in <c>web/pages/TagsPage.tsx</c>.</summary>
public static class TagTree
{
    /// <summary>Builds the tree, sorted by name or by total use.</summary>
    /// <param name="tags">The tags in use.</param>
    /// <param name="order">The order.</param>
    /// <returns>The top-level nodes.</returns>
    public static IReadOnlyList<TagNode> Build(IEnumerable<TagCount> tags, TagOrder order)
    {
        var roots = new List<Builder>();
        var byPath = new Dictionary<string, Builder>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            var siblings = roots;
            var path = "";
            foreach (var part in tag.Name.Split('/'))
            {
                path = path.Length > 0 ? $"{path}/{part}" : part;
                if (!byPath.TryGetValue(path, out var node))
                {
                    node = new Builder(path, part);
                    byPath[path] = node;
                    siblings.Add(node);
                }

                node.Total += tag.NoteCount;
                if (path == tag.Name)
                {
                    node.Count = tag.NoteCount;
                }

                siblings = node.Children;
            }
        }

        return Sort(roots, order);
    }

    /// <summary>Compares labels as the browser's <c>localeCompare</c> does; also used to sort a flat match list the
    /// same way (the Tags page's filter).</summary>
    public static readonly StringComparer LabelComparer = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.None);

    private static List<TagNode> Sort(List<Builder> nodes, TagOrder order) =>
        nodes
            .OrderBy(node => order == TagOrder.Count ? -node.Total : 0)
            .ThenBy(node => node.Label, LabelComparer)
            .Select(node => new TagNode(node.Path, node.Label, node.Count, node.Total, Sort(node.Children, order)))
            .ToList();

    private sealed class Builder(string path, string label)
    {
        public string Path { get; } = path;

        public string Label { get; } = label;

        public int? Count { get; set; }

        public int Total { get; set; }

        public List<Builder> Children { get; } = [];
    }
}
