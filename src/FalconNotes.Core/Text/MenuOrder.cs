namespace FalconNotes.Core.Text;

/// <summary>
/// The side menu's order. Port of <c>web/lib/menu.ts</c> (docs/04, Side menu order); the labels, routes and icons
/// live in the UI.
/// </summary>
public static class MenuOrder
{
    /// <summary>The menu's items in their default order.</summary>
    public static readonly IReadOnlyList<string> Items = ["home", "todo", "quick", "habits", "tags", "archive", "settings", "help"];

    /// <summary>
    /// Every item in the order the user chose (comma-separated). Items it leaves out follow in their default order;
    /// unknown or repeated names are ignored.
    /// </summary>
    /// <param name="saved">The saved order.</param>
    /// <returns>Every item, once.</returns>
    public static IReadOnlyList<string> Read(string saved)
    {
        var chosen = new List<string>();
        foreach (var item in saved.Split(','))
        {
            if (Items.Contains(item) && !chosen.Contains(item))
            {
                chosen.Add(item);
            }
        }

        return [.. chosen, .. Items.Where(item => !chosen.Contains(item))];
    }

    /// <summary>The value to save for an order: empty for the default order, so later changes to the default apply.</summary>
    /// <param name="order">The order.</param>
    /// <returns>The value to save.</returns>
    public static string Save(IEnumerable<string> order)
    {
        var joined = string.Join(',', order);
        return joined == string.Join(',', Items) ? "" : joined;
    }

    /// <summary>The list with one item moved; unchanged (the same instance) for moves out of range.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The list.</param>
    /// <param name="from">The item's position.</param>
    /// <param name="to">Its new position.</param>
    /// <returns>The new list.</returns>
    public static IReadOnlyList<T> Move<T>(IReadOnlyList<T> items, int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= items.Count || to >= items.Count)
        {
            return items;
        }

        var next = items.ToList();
        var item = next[from];
        next.RemoveAt(from);
        next.Insert(to, item);
        return next;
    }
}
