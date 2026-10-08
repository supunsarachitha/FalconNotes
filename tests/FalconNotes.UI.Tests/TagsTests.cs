using FalconNotes.Core.Domain;
using FalconNotes.UI.Pages;

namespace FalconNotes.UI.Tests;

/// <summary>Port of the UI-level cases of TagsPage.test.tsx (docs/11); tagTree's own nesting and ordering logic is
/// covered in Core.Tests. Tags come from real notes' text (TagParser), not fabricated counts, since this page reads
/// them from the database.</summary>
public class TagsTests : BunitContext
{
    private static async Task SeedAsync(UiTestApp app)
    {
        await app.PostAsync("#ideas one");
        await app.PostAsync("#ideas two");
        await app.PostAsync("#work plain");
        for (var i = 0; i < 5; i++)
        {
            await app.PostAsync($"#work/meetings note {i}");
        }

        for (var i = 0; i < 3; i++)
        {
            await app.PostAsync($"#travel/japan note {i}");
        }
    }

    [Fact]
    public async Task Says_when_the_tags_page_is_turned_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Tags = false });

        var cut = Render<Tags>();

        Assert.Contains("The Tags page is turned off", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lists_every_tag_with_its_count_links_to_its_notes_and_filters()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await SeedAsync(app);

        var cut = Render<Tags>();
        cut.WaitForState(() => cut.FindAll("ul[aria-label='All tags']").Count == 1);

        var all = cut.Find("ul[aria-label='All tags']");
        var meetings = all.QuerySelectorAll("a").First(a => a.GetAttribute("aria-label") == "work/meetings, 5 notes");
        Assert.Equal("/?tag=work%2Fmeetings", meetings.GetAttribute("href"));
        var travel = all.QuerySelectorAll("a").First(a => a.GetAttribute("aria-label") == "travel"); // a parent without notes of its own
        Assert.Equal("/?tag=travel", travel.GetAttribute("href"));

        cut.Find("input[aria-label='Filter tags']").Input("#MEET");

        var matches = cut.Find("ul[aria-label='Matching tags']");
        Assert.Equal(["work/meetings, 5 notes"], matches.QuerySelectorAll("a").Select(a => a.GetAttribute("aria-label")));

        cut.Find("input[aria-label='Filter tags']").Input("zzz");

        Assert.Contains("No tags match \"zzz\".", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sorts_by_use()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await SeedAsync(app);
        var cut = Render<Tags>();
        cut.WaitForState(() => cut.FindAll("ul[aria-label='All tags']").Count == 1);

        cut.FindAll("button[role=radio]").First(b => b.TextContent.Trim() == "Most used").Click();

        // The click waits its turn behind the render that showed the list, so on a busy machine the new order is
        // not there yet when Click returns (it failed in about 1 run in 20 with the processors busy).
        cut.WaitForAssertion(() => Assert.Equal(
            ["work, 1 note", "work/meetings, 5 notes", "travel", "travel/japan, 3 notes", "ideas, 2 notes"],
            cut.Find("ul[aria-label='All tags']").QuerySelectorAll("a").Select(a => a.GetAttribute("aria-label"))));
    }
}
