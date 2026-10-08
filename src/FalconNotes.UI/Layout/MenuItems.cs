using FalconNotes.Core.Domain;
using FalconNotes.UI.Components.Ui;

namespace FalconNotes.UI.Layout;

/// <summary>A side-menu item: its label, address, icon and when it shows. Port of <c>MENU_INFO</c> in <c>lib/menu.ts</c>.</summary>
/// <param name="Id">The item's name in the saved order.</param>
/// <param name="Label">What the menu says.</param>
/// <param name="Href">Where it goes.</param>
/// <param name="Icon">Its icon.</param>
/// <param name="Shown">Whether it shows with these preferences.</param>
public sealed record MenuItemInfo(string Id, string Label, string Href, IconName Icon, Func<Preferences, bool> Shown);

/// <summary>The side menu's items.</summary>
public static class MenuItems
{
    /// <summary>Every item by its name.</summary>
    public static readonly IReadOnlyDictionary<string, MenuItemInfo> All = new[]
    {
        new MenuItemInfo("home", "Home", "/", IconName.Home, _ => true),
        new MenuItemInfo("todo", "Todo", "/todo", IconName.ListTodo, p => p.TodoLists),
        new MenuItemInfo("quick", "Quick notes", "/quick", IconName.Zap, p => p.QuickNotes),
        new MenuItemInfo("habits", "Habits", "/habits", IconName.CalendarCheck, p => p.HabitTracker),
        new MenuItemInfo("tags", "Tags", "/tags", IconName.Hash, p => p.Tags),
        new MenuItemInfo("archive", "Archive", "/archive", IconName.Archive, p => p.Archive),
        new MenuItemInfo("settings", "Settings", "/settings", IconName.Settings, _ => true),
        new MenuItemInfo("help", "Help", "/help", IconName.CircleHelp, p => p.HelpMenu),
    }.ToDictionary(item => item.Id);

    /// <summary>
    /// Whether an item is the page being shown (<c>isActive</c> in AppShell.tsx): Home is not active while a filter shows,
    /// Tags is active on <c>?tag=</c>, Settings on <c>/settings/*</c> and the trash.
    /// </summary>
    /// <param name="id">The item.</param>
    /// <param name="path">The page's path.</param>
    /// <param name="query">Its query values.</param>
    /// <returns>Whether it is active.</returns>
    public static bool IsActive(string id, string path, IReadOnlyDictionary<string, string> query)
    {
        var filtered = new[] { "tag", "q", "day", "label" }.Any(query.ContainsKey);
        return id switch
        {
            "home" => path == "/" && !filtered,
            "tags" => path == "/tags" || (path == "/" && query.ContainsKey("tag")),
            "settings" => path == "/settings" || path.StartsWith("/settings/", StringComparison.Ordinal) || path == "/trash",
            _ => path == All[id].Href,
        };
    }
}
