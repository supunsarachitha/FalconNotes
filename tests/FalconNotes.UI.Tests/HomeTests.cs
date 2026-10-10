using FalconNotes.Core.Domain;
using FalconNotes.Core.Text;
using FalconNotes.UI.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New tests (docs/11): the web app's HomePage.test.tsx cases are folded into other components' own tests (Composer,
/// NoteCard, Labels) already ported; this covers what is Home's own job: picking the right view for ?tag, ?q, ?day
/// and ?label, and hiding today's note from the lists below it.
/// </summary>
public class HomeTests : BunitContext
{
    private void SetUpJs()
    {
        var editor = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        editor.SetupVoid("bindAutoGrow", _ => true).SetVoidResult();
        editor.SetupVoid("growNow", _ => true).SetVoidResult();
        editor.SetupVoid("focusAtEnd", _ => true).SetVoidResult();
        editor.SetupVoid("focus", _ => true).SetVoidResult();
        editor.SetupVoid("positionPopup", _ => true).SetVoidResult();
        editor.SetupVoid("setSelection", _ => true).SetVoidResult();
        editor.SetupVoid("applyEdit", _ => true).SetVoidResult();
        var gestures = JSInterop.SetupModule("./_content/FalconNotes.UI/js/gestures.js");
        gestures.SetupVoid("bindDoubleTap", _ => true).SetVoidResult();
        gestures.SetupVoid("unbindDoubleTap", _ => true).SetVoidResult();
        var markdown = JSInterop.SetupModule("./_content/FalconNotes.UI/js/markdown.js");
        markdown.SetupVoid("bindTaskToggle", _ => true).SetVoidResult();
        markdown.SetupVoid("unbindTaskToggle", _ => true).SetVoidResult();
        var observe = JSInterop.SetupModule("./_content/FalconNotes.UI/js/observe.js");
        observe.SetupVoid("bindSentinel", _ => true).SetVoidResult();
        observe.SetupVoid("unbindSentinel", _ => true).SetVoidResult();
    }

    [Fact]
    public async Task Shows_the_composer_and_the_feed_by_default()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        await app.PostAsync("hello there");

        var cut = Render<Home>();

        cut.Find("#composer");
        cut.WaitForAssertion(() => Assert.Contains("hello there", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Shows_the_today_card_when_daily_notes_are_on()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { DailyNotes = true });
        SetUpJs();

        var cut = Render<Home>();

        cut.WaitForAssertion(() => cut.Find("section[aria-label=Today]"), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Starts_a_new_days_note_with_the_templates_text_without_the_templates_title()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var template = await app.PostAsync("# Daily template\n\n## Plan\n- [ ] \n\n## Done\n", NoteKind.Quick);
        await app.State.UpdatePreferencesAsync(p => p with { DailyNotes = true, DateFormat = "dddd, d MMMM yyyy", DailyNoteTemplate = template.Id.ToString("D") });
        SetUpJs();

        var cut = Render<Home>();

        cut.WaitForAssertion(
            () => Assert.Equal("## Plan\n- [ ] \n\n## Done\n", cut.Find("section[aria-label=Today] textarea").GetAttribute("value")), TimeSpan.FromSeconds(5));
        cut.FindAll("section[aria-label=Today] button").First(b => b.TextContent.Trim() == "Post").Click();

        // Only a DOM check while waiting: the card shows the saved note in place of the composer.
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("section[aria-label=Today] textarea")), TimeSpan.FromSeconds(5));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Core.Clock.GetUtcNow(), app.Core.Clock.LocalTimeZone).DateTime);
        var saved = (await app.Core.Notes.GetDailyAsync(today))!;
        Assert.Equal($"# {DateFormats.Format(today, "dddd, d MMMM yyyy")}\n\n## Plan\n- [ ] \n\n## Done", saved.Content.TrimEnd());
    }

    [Fact]
    public async Task Starts_the_days_note_empty_when_the_template_is_in_the_trash_or_none_is_chosen()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var template = await app.PostAsync("Old template");
        await app.Core.Notes.PatchAsync(template.Id, new Core.Notes.NotePatch(IsTrashed: true));
        await app.State.UpdatePreferencesAsync(p => p with { DailyNotes = true, DailyNoteTemplate = template.Id.ToString("D") });
        SetUpJs();

        var cut = Render<Home>();

        cut.WaitForAssertion(() => cut.Find("section[aria-label=Today] textarea"), TimeSpan.FromSeconds(5));
        Assert.Equal("", cut.Find("section[aria-label=Today] textarea").GetAttribute("value") ?? "");
    }

    [Fact]
    public async Task Hides_todays_note_from_the_feed_below()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { DailyNotes = true });
        SetUpJs();
        var today = DateOnly.FromDateTime(app.Core.Clock.Now.UtcDateTime);
        await app.Core.Notes.CreateAsync("# Today\n\nFirst words of the day", dailyDate: today);
        await app.PostAsync("an ordinary note");

        var cut = Render<Home>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("an ordinary note", cut.Markup, StringComparison.Ordinal);
            // Shown once, by TodayCard; the feed below must leave it out rather than showing it a second time.
            Assert.Equal(1, Occurrences(cut.Markup, "First words of the day"));
        }, TimeSpan.FromSeconds(5));
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += value.Length;
        }

        return count;
    }

    [Fact]
    public async Task Shows_the_tag_filter_view()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        await app.PostAsync("about #work");
        await app.PostAsync("about #home");
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/?tag=work");

        var cut = Render<Home>();

        Assert.Equal("work", cut.Find("h1").TextContent);
        // TextContent, not raw Markup: the tag itself renders as a link, splitting "about #work" across tags.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("about", cut.Find("section").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("home", cut.Find("section").TextContent, StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(5));
        Assert.Empty(cut.FindAll("#composer"));
    }

    [Fact]
    public async Task Shows_the_search_filter_view()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        await app.PostAsync("buy milk");
        await app.PostAsync("walk the dog");
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/?q=milk");

        var cut = Render<Home>();

        Assert.Contains("milk", cut.Find("h1").TextContent, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Contains("buy milk", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Shows_the_day_filter_view()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var note = await app.PostAsync("posted today");
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/?day={DateFormats.Key(DateOnly.FromDateTime(app.Core.Clock.Now.UtcDateTime))}");

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("posted today", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Shows_the_label_filter_view_with_the_labels_name_and_colour()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = true });
        SetUpJs();
        var work = await app.Core.Labels.CreateAsync("Work");
        var note = await app.PostAsync("labelled note");
        await app.Core.Notes.PatchAsync(note.Id, new Core.Notes.NotePatch(LabelIds: [work.Id]));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/?label={work.Id}");

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Equal("Work", cut.Find("h1").TextContent), TimeSpan.FromSeconds(5));
        cut.Find("span[aria-hidden=true].rounded-full"); // the label's colour dot
        cut.WaitForAssertion(() => Assert.Contains("labelled note", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("hidden from Home and Quick notes", cut.Markup, StringComparison.Ordinal);
    }

    // Port of "says on a label's page when its notes are hidden from Home and Quick notes" (Labels.test.tsx, Maple Notes 1.16.0).
    [Fact]
    public async Task Says_on_a_labels_page_when_its_notes_are_hidden_from_home_and_quick_notes()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = true });
        SetUpJs();
        var work = await app.Core.Labels.CreateAsync("Work");
        await app.Core.Labels.UpdateAsync(work.Id, hideNotes: true);
        var note = await app.PostAsync("Quarterly report");
        await app.Core.Notes.PatchAsync(note.Id, new Core.Notes.NotePatch(LabelIds: [work.Id]));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/?label={work.Id}");

        var cut = Render<Home>();

        cut.WaitForAssertion(
            () => Assert.Contains("These notes are hidden from Home and Quick notes and show only here.", cut.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Equal("/settings/labels", cut.FindAll("a").Single(a => a.TextContent.Trim() == "Change in Settings").GetAttribute("href"));
        cut.WaitForAssertion(() => Assert.Contains("Quarterly report", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5)); // still listed here
    }

    [Fact]
    public async Task A_note_leaves_home_when_its_label_hides_its_notes_and_returns_when_it_stops()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = true, DailyNotes = false });
        SetUpJs();
        var label = await app.Core.Labels.CreateAsync("Private");
        var secret = await app.PostAsync("Surprise party plans");
        await app.PostAsync("Groceries");
        await app.Core.Notes.PatchAsync(secret.Id, new Core.Notes.NotePatch(LabelIds: [label.Id]));
        var cut = Render<Home>();
        cut.WaitForAssertion(() => Assert.Contains("Surprise party plans", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));

        await app.Core.Labels.UpdateAsync(label.Id, hideNotes: true);

        // Together: the lists are empty for a moment while they reload.
        cut.WaitForAssertion(
            () =>
            {
                Assert.Contains("Groceries", cut.Markup, StringComparison.Ordinal);
                Assert.DoesNotContain("Surprise party plans", cut.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
        await app.Core.Labels.UpdateAsync(label.Id, hideNotes: false);
        cut.WaitForAssertion(() => Assert.Contains("Surprise party plans", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Shows_deleted_label_for_a_label_that_no_longer_exists()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = true });
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/?label={Guid.NewGuid()}");

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Equal("Deleted label", cut.Find("h1").TextContent), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Clear_link_in_the_filter_header_points_back_to_home()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/?tag=work");
        var cut = Render<Home>();

        var clear = cut.FindAll("a").First(a => a.TextContent.Contains("Clear", StringComparison.Ordinal));
        Assert.Equal("/", clear.GetAttribute("href"));
    }
}
