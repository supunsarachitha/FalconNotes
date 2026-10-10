using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Pages.Settings;
using Microsoft.AspNetCore.Components.Web;

namespace FalconNotes.UI.Tests;

/// <summary>New tests (docs/11): LabelSettings has no dedicated reference test. Covers the "Use labels" switch, and
/// creating, renaming, recolouring and deleting labels, against the real database. Hiding a label's notes is the port
/// of the case SettingsPage.test.tsx gained in Maple Notes 1.16.0.</summary>
public class LabelSettingsTests : BunitContext
{
    private void SetUpDialogsJs()
    {
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("openMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("closeMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("showModal", _ => true).SetVoidResult();
        dialogs.SetupVoid("close", _ => true).SetVoidResult();
    }

    private void SetUpEditorJs()
    {
        var editor = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        editor.SetupVoid("focusAtEnd", _ => true).SetVoidResult();
    }

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
    public async Task Shows_no_labels_yet_until_one_is_created()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");

        var cut = Render<LabelSettings>();

        cut.WaitForAssertion(() => Assert.Contains("No labels yet.", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Toggles_the_labels_preference()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = false });
        var cut = Render<LabelSettings>();

        cut.Find("button[role=switch]").Click();

        cut.WaitForAssertion(() => Assert.True(app.State.Preferences.Labels));
    }

    [Fact]
    public async Task Creates_a_label_with_the_next_colour()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpDialogsJs();
        var cut = Render<LabelSettings>();
        cut.WaitForAssertion(() => Assert.Contains("No labels yet.", cut.Markup, StringComparison.Ordinal));

        cut.Find("input[aria-label='New label name']").Input("Work");
        cut.Find("form").Submit();

        await WaitUntilAsync(async () => (await app.Core.Labels.ListAsync(NoteKinds.All)).Count == 1);
        var created = (await app.Core.Labels.ListAsync(NoteKinds.All)).Single();
        Assert.Equal("Work", created.Label.Name);
        Assert.Equal(LabelColor.Red, created.Label.Color); // NextColor(0): (0 % 9) + 1 = Red
        cut.WaitForAssertion(() => cut.Find("li"));
    }

    [Fact]
    public async Task Shows_an_error_for_a_duplicate_name_and_does_not_create()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.Core.Labels.CreateAsync("Work");
        var cut = Render<LabelSettings>();
        cut.WaitForAssertion(() => cut.Find("li"));

        cut.Find("input[aria-label='New label name']").Input("work");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("You already have a label called", cut.Markup, StringComparison.Ordinal));
        Assert.Single(await app.Core.Labels.ListAsync(NoteKinds.All));
    }

    [Fact]
    public async Task Renames_a_label_from_its_row()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpEditorJs();
        await app.Core.Labels.CreateAsync("Work");
        var cut = Render<LabelSettings>();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Rename Work']"));

        cut.Find("button[aria-label='Rename Work']").Click();
        cut.WaitForState(() => cut.FindAll("input[aria-label='New name for Work']").Count == 1);
        var name = cut.Find("input[aria-label='New name for Work']");
        name.Input("Office");
        name.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        await WaitUntilAsync(async () => (await app.Core.Labels.ListAsync(NoteKinds.All)).Single().Label.Name == "Office");
    }

    [Fact]
    public async Task Changes_a_labels_colour_from_its_swatch()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpDialogsJs();
        await app.Core.Labels.CreateAsync("Work", LabelColor.Grey);
        var cut = Render<LabelSettings>();
        cut.WaitForAssertion(() => cut.Find("button[aria-label^='Colour of Work']"));

        cut.Find("button[aria-label^='Colour of Work']").Click();
        cut.WaitForState(() => cut.FindAll("button[role=menuitemradio]").Count > 0);
        cut.FindAll("button[role=menuitemradio]").First(b => b.GetAttribute("aria-label") == "Blue").Click();

        await WaitUntilAsync(async () => (await app.Core.Labels.ListAsync(NoteKinds.All)).Single().Label.Color == LabelColor.Blue);
    }

    [Fact]
    public async Task Hides_a_labels_notes_from_home_and_quick_notes_and_shows_them_again()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = true });
        await app.Core.Labels.CreateAsync("Private", LabelColor.Purple);
        var cut = Render<LabelSettings>();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Hide notes labelled Private from Home and Quick notes']"));
        Assert.Contains("The eye hides a label's notes from Home and Quick notes", cut.Markup, StringComparison.Ordinal);

        var hide = cut.Find("button[aria-label='Hide notes labelled Private from Home and Quick notes']");
        Assert.Equal("false", hide.GetAttribute("aria-pressed"));
        Assert.DoesNotContain("Notes hidden", cut.Markup, StringComparison.Ordinal);
        hide.Click();

        await WaitUntilAsync(async () => (await app.Core.Labels.ListAsync(NoteKinds.All)).Single().Label.HideNotes);
        cut.WaitForAssertion(() => Assert.Contains("Notes hidden", cut.Markup, StringComparison.Ordinal));
        var show = cut.Find("button[aria-label='Show notes labelled Private on Home and in Quick notes']");
        Assert.Equal("true", show.GetAttribute("aria-pressed"));
        show.Click();

        await WaitUntilAsync(async () => !(await app.Core.Labels.ListAsync(NoteKinds.All)).Single().Label.HideNotes);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Notes hidden", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(LabelColor.Purple, (await app.Core.Labels.ListAsync(NoteKinds.All)).Single().Label.Color); // nothing else changed
    }

    [Fact]
    public async Task Deletes_a_label_after_confirming_with_its_note_count()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpDialogsJs();
        var work = await app.Core.Labels.CreateAsync("Work");
        var note = await app.PostAsync("hello");
        await app.Core.Notes.PatchAsync(note.Id, new NotePatch(LabelIds: [work.Id]));
        var cut = Render<LabelSettings>();
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Delete Work']"));

        cut.Find("button[aria-label='Delete Work']").Click();

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("h2"), h => h.TextContent.StartsWith("Delete the label", StringComparison.Ordinal)));
        Assert.Equal("Delete the label “Work”?", cut.FindAll("h2").First(h => h.TextContent.StartsWith("Delete the label", StringComparison.Ordinal)).TextContent);
        cut.WaitForAssertion(() => Assert.Contains("It comes off its note. The notes themselves stay as they are.", cut.Markup, StringComparison.Ordinal));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Delete label").Click();

        await WaitUntilAsync(async () => (await app.Core.Labels.ListAsync(NoteKinds.All)).Count == 0);
        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Label “Work” deleted."));
    }
}
