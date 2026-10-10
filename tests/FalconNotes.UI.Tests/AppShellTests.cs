using FalconNotes.Core.Domain;
using FalconNotes.UI.Layout;
using Microsoft.AspNetCore.Components;

namespace FalconNotes.UI.Tests;

/// <summary>Port of components/AppShell.test.tsx (docs/11): the shell has no server branding, so "shows the app's
/// name and icon" becomes "never shows Maple Notes" and the menu-size test keeps its own case.</summary>
public class AppShellTests : BunitContext
{
    private static RenderFragment Content => builder => builder.AddContent(0, "content");

    private async Task<UiTestApp> StartAsync(Preferences? preferences = null) =>
        await UiTestApp.StartAsync(Services, "Alex Maple", preferences);

    private static IEnumerable<string> Labels(IEnumerable<AngleSharp.Dom.IElement> links) => links.Select(a => a.TextContent.Trim());

    // New: found on a Pixel 8 Pro with three-button navigation (docs/12, Edge to edge). The page draws behind the
    // system bars, so the drawer and the sidebar must keep clear of both, or their last row (the name, and Lock) is
    // under the navigation bar and cannot be tapped.
    [Fact]
    public async Task The_drawer_and_the_sidebar_keep_clear_of_the_status_and_navigation_bars()
    {
        using var app = await StartAsync();
        var cut = Render<AppShell>(p => p.Add(s => s.Body, Content));

        var drawer = cut.Find("dialog[aria-label=Navigation]").ClassList;
        Assert.Contains("pt-[var(--safe-top,0px)]", drawer);
        Assert.Contains("pb-[var(--safe-bottom,0px)]", drawer);

        var sidebar = cut.Find("aside").ClassList;
        Assert.Contains("top-[var(--safe-top,0px)]", sidebar);
        Assert.Contains("h-[calc(100dvh-var(--safe-top,0px)-var(--safe-bottom,0px))]", sidebar);
    }

    [Fact]
    public async Task Shows_archive_and_tags_unless_they_are_turned_off()
    {
        using var app = await StartAsync();
        var menu = Labels(Render<AppShell>(p => p.Add(s => s.Body, Content)).Find("nav[aria-label=Main]").QuerySelectorAll("a"));

        Assert.Contains(menu, text => text.StartsWith("Tags", StringComparison.Ordinal));
        Assert.Contains("Archive", menu);
    }

    [Fact]
    public async Task Hides_archive_and_tags_when_turned_off()
    {
        using var app = await StartAsync(new Preferences { Archive = false, Tags = false });
        var menu = Labels(Render<AppShell>(p => p.Add(s => s.Body, Content)).Find("nav[aria-label=Main]").QuerySelectorAll("a"));

        Assert.DoesNotContain(menu, text => text.StartsWith("Tags", StringComparison.Ordinal));
        Assert.DoesNotContain("Archive", menu);
    }

    [Fact]
    public async Task Shows_help_in_the_menu_unless_it_is_turned_off()
    {
        using var app = await StartAsync();
        var cut = Render<AppShell>(p => p.Add(s => s.Body, Content));
        Assert.Contains("Help", Labels(cut.Find("nav[aria-label=Main]").QuerySelectorAll("a")));

        await app.State.UpdatePreferencesAsync(p => p with { HelpMenu = false });

        cut.WaitForAssertion(() => Assert.DoesNotContain("Help", Labels(cut.Find("nav[aria-label=Main]").QuerySelectorAll("a"))));
    }

    [Fact]
    public async Task Shows_falcon_notes_never_maple_notes_and_sizes_the_menu_as_chosen()
    {
        using var app = await StartAsync(new Preferences { MenuTextSize = MenuTextSize.Large });
        var cut = Render<AppShell>(p => p.Add(s => s.Body, Content));

        Assert.Contains("Falcon Notes", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Maple Notes", cut.Markup, StringComparison.Ordinal);
        cut.Find("[data-menu=large]");
    }

    [Fact]
    public async Task Orders_the_menu_as_chosen_leaving_out_pages_turned_off()
    {
        using var app = await StartAsync(new Preferences { MenuOrder = "help,settings,todo", HabitTracker = false });
        var links = Render<AppShell>(p => p.Add(s => s.Body, Content)).Find("nav[aria-label=Main]").QuerySelectorAll("a");

        Assert.Equal(["Help", "Settings", "Todo", "Home", "Quick notes", "Tags", "Archive"], Labels(links));
    }

    [Fact]
    public async Task Lists_the_labels_under_the_menu_while_labels_are_on()
    {
        using var app = await StartAsync(new Preferences { Labels = true });
        await app.Core.Labels.CreateAsync("Work", LabelColor.Blue);
        await app.Core.Labels.CreateAsync("Home", LabelColor.Green);

        var cut = Render<AppShell>(p => p.Add(s => s.Body, Content));
        var labels = cut.WaitForElement("nav[aria-label=Labels]");

        // LabelService lists alphabetically (docs/04), not in creation order.
        Assert.Equal(["Home", "Work"], Labels(labels.QuerySelectorAll("a")));
    }

    [Fact]
    public async Task No_labels_navigation_while_labels_are_off()
    {
        using var app = await StartAsync();

        var cut = Render<AppShell>(p => p.Add(s => s.Body, Content));

        Assert.Empty(cut.FindAll("nav[aria-label=Labels]"));
    }
}
