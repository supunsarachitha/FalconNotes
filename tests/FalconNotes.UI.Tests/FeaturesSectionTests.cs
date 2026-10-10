using FalconNotes.Core.Domain;
using FalconNotes.UI.Pages.Settings;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Port of the cases SettingsPage.test.tsx gained in Maple Notes 1.9.0 to 1.15.0 (docs/11): Help in the menu, the
/// photo size and the daily-note template. The last case is new: where Help is when it is not in the menu.
/// </summary>
public class FeaturesSectionTests : BunitContext
{
    private static AngleSharp.Dom.IElement Switch(IRenderedComponent<FeaturesSection> cut, string label) =>
        cut.Find($"button[role=switch][aria-label='{label}']");

    [Fact]
    public async Task Help_can_leave_the_side_menu()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<FeaturesSection>();
        Assert.Equal("true", Switch(cut, "Help in the menu").GetAttribute("aria-checked"));

        Switch(cut, "Help in the menu").Click();

        cut.WaitForAssertion(() => Assert.False(app.State.Preferences.HelpMenu));
        Assert.Contains("the guide is then opened from the foot of Settings", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_photo_size_is_chosen_only_while_photos_are_shrunk()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<FeaturesSection>();
        Assert.Empty(cut.FindAll("input[name=photoSize]"));

        Switch(cut, "Shrink photos before adding").Click();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("input[name=photoSize]").Count));
        Assert.Equal("Large", cut.Find("input[name=photoSize][checked]").GetAttribute("value")); // as photos were shrunk before
        Assert.Contains("Sharp on large screens: at most 2,560 pixels on the longest side.", cut.Markup, StringComparison.Ordinal);

        cut.Find("input[name=photoSize][value=Small]").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(PhotoSize.Small, app.State.Preferences.PhotoSize));
        Assert.Contains("Smallest files, fine on phones: at most 1,280 pixels on the longest side.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_which_note_is_the_daily_template_and_can_stop_using_it()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<FeaturesSection>();
        Assert.DoesNotContain("Daily-note template", cut.Markup, StringComparison.Ordinal); // only with daily notes on

        await app.State.UpdatePreferencesAsync(p => p with { DailyNotes = true });
        cut.Render();
        Assert.Contains("New daily notes start empty. To start them with a note's text", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Stop using");

        var template = await app.PostAsync("# Morning pages\n\n## Plan");
        await app.State.UpdatePreferencesAsync(p => p with { DailyNoteTemplate = template.Id.ToString("D") });
        cut.Render();
        cut.WaitForAssertion(() => Assert.Contains("New daily notes start with the text of", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("“Morning pages”", cut.Markup, StringComparison.Ordinal);

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Stop using").Click();

        cut.WaitForAssertion(() => Assert.Equal("", app.State.Preferences.DailyNoteTemplate));
    }

    [Fact]
    public async Task Says_when_the_template_note_is_gone()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var template = await app.PostAsync("Old template");
        await app.Core.Notes.PatchAsync(template.Id, new Core.Notes.NotePatch(IsTrashed: true));
        await app.State.UpdatePreferencesAsync(p => p with { DailyNotes = true, DailyNoteTemplate = template.Id.ToString("D") });

        var cut = Render<FeaturesSection>();

        cut.WaitForAssertion(() => Assert.Contains(
            "The template note was deleted or is in the trash, so new daily notes start empty.", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_help_out_of_the_menu_the_foot_of_settings_opens_it()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<Settings>(p => p.Add(s => s.SectionId, "features")); // the list, beside the Features section
        Assert.Empty(cut.FindAll("a[href='/help']"));

        await app.State.UpdatePreferencesAsync(p => p with { HelpMenu = false });

        cut.WaitForAssertion(() => Assert.Equal("Help", cut.Find("a[href='/help']").TextContent.Trim()));
        Assert.Contains("Based on Maple Notes 1.16.0", cut.Markup, StringComparison.Ordinal);
    }
}
