using FalconNotes.Core.Domain;
using FalconNotes.UI.Pages.Settings;
using FalconNotes.UI.State;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New tests (docs/11): the festival backgrounds in Settings → Appearance (docs/06, Backgrounds), which the web app
/// does not have, and that each one offered has everything it needs. How they look is a matter for the eye:
/// <c>scripts/make-backgrounds.py --preview</c> and a device.
/// </summary>
public class BackgroundTests : BunitContext
{
    private static readonly string Repository = FindRepository();

    private static string FindRepository()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "FalconNotes.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository was not found above the tests.");
    }

    /// <summary>Polls the store with a plain await (as HabitsPageTests does): a choice is shown at once and saved in
    /// the background, so the store can still hold the old value when the page already shows the new one.</summary>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task None_is_chosen_at_first_and_every_background_is_offered_by_name()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");

        var cut = Render<AppearanceSection>();

        Assert.Equal(
            ["None", "Halloween", "Christmas", "New Year", "Valentine's Day", "Easter", "Diwali", "Vesak", "Eid", "Lunar New Year"],
            cut.FindAll("input[name=background]").Select(i => i.ParentElement!.TextContent.Trim()));
        Assert.Equal("None", cut.Find("input[name=background][checked]").GetAttribute("value"));
        Assert.Contains("A festive pattern behind your notes. It follows the light or dark theme. Choose None for no background.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("background-preview-none", cut.Find("input[name=background][value=None]").ParentElement!.InnerHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Remove background"); // nothing to remove yet
    }

    [Fact]
    public async Task A_background_can_be_removed_with_its_own_button()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Background = Background.Halloween });
        var cut = Render<AppearanceSection>();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Remove background").Click();

        cut.WaitForAssertion(() => Assert.Equal(Background.None, app.State.Preferences.Background));
        cut.WaitForAssertion(() => Assert.Equal("None", cut.Find("input[name=background][checked]").GetAttribute("value")));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Remove background");
        await WaitUntilAsync(async () => (await app.Core.Preferences.GetAsync()).Background == Background.None);
    }

    [Fact]
    public async Task Choosing_a_background_saves_it_and_shows_it_as_chosen()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<AppearanceSection>();

        cut.Find("input[name=background][value=Vesak]").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(Background.Vesak, app.State.Preferences.Background));
        cut.WaitForAssertion(() => Assert.Equal("Vesak", cut.Find("input[name=background][checked]").GetAttribute("value")));
        await WaitUntilAsync(async () => (await app.Core.Preferences.GetAsync()).Background == Background.Vesak);

        cut.Find("input[name=background][value=None]").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(Background.None, app.State.Preferences.Background));
    }

    [Fact]
    public async Task Each_choice_shows_a_small_picture_of_its_own_background()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");

        var cut = Render<AppearanceSection>();

        var pictures = cut.FindAll("span.background-preview").Select(s => s.GetAttribute("data-background")).ToList();
        Assert.Equal(
            [null, "halloween", "christmas", "newyear", "valentine", "easter", "diwali", "vesak", "eid", "lunarnewyear"], pictures);
    }

    /// <summary>
    /// A background added to the enum and forgotten anywhere else would show as a blank choice or a plain page.
    /// </summary>
    [Fact]
    public void Every_background_has_a_name_its_colours_and_a_tile_for_each_theme()
    {
        var css = File.ReadAllText(Path.Combine(Repository, "src", "FalconNotes.UI", "Styles", "app.css"));
        var tiles = Path.Combine(Repository, "src", "FalconNotes.UI", "wwwroot", "img", "backgrounds");
        var generator = File.ReadAllText(Path.Combine(Repository, "scripts", "make-backgrounds.py"));

        foreach (var background in Enum.GetValues<Background>())
        {
            Assert.True(AppearanceSection.BackgroundLabels.ContainsKey(background), $"{background} has no name in Settings.");
            if (background == Background.None)
            {
                continue;
            }

            var name = AppearanceService.BackgroundName(background);
            Assert.Contains($"\n[data-background=\"{name}\"] {{", css, StringComparison.Ordinal);
            Assert.Contains($"\n.dark[data-background=\"{name}\"] {{", css, StringComparison.Ordinal);
            Assert.Contains($"\"{name}\":", generator, StringComparison.Ordinal);
            foreach (var theme in new[] { "light", "dark" })
            {
                Assert.Contains($"url(\"../img/backgrounds/{name}-{theme}.svg\")", css, StringComparison.Ordinal);
                var tile = new FileInfo(Path.Combine(tiles, $"{name}-{theme}.svg"));
                Assert.True(tile.Exists, $"{tile.Name} is missing: run scripts/make-backgrounds.py.");

                // Drawn here, never fetched: a tile that reached for anything outside itself would break offline.
                var svg = File.ReadAllText(tile.FullName);
                Assert.DoesNotContain("href", svg, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("<image", svg, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Equal(2 * (Enum.GetValues<Background>().Length - 1), Directory.GetFiles(tiles, "*.svg").Length); // and no stray tile
    }
}
