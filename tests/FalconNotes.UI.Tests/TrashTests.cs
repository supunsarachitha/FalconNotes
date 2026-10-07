using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Pages;

namespace FalconNotes.UI.Tests;

/// <summary>New tests (docs/11): TrashPage has no dedicated reference test; this covers showing a trashed note
/// with its days-left text, Restore, Delete forever (with its confirmation) and Empty trash.</summary>
public class TrashTests : BunitContext
{
    private void SetUpJs()
    {
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("showModal", _ => true).SetVoidResult();
        dialogs.SetupVoid("close", _ => true).SetVoidResult();
    }

    private async Task<Note> TrashedNoteAsync(UiTestApp app, string content = "a deleted note")
    {
        var note = await app.PostAsync(content);
        return (await app.Core.Notes.PatchAsync(note.Id, new NotePatch(IsTrashed: true)))!;
    }

    [Fact]
    public async Task Shows_a_trashed_note_with_its_days_left()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        await TrashedNoteAsync(app);

        var cut = Render<Trash>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("a deleted note", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("deleted for good in 30 days", cut.Markup, StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Shows_the_empty_state_with_nothing_trashed()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");

        var cut = Render<Trash>();

        cut.WaitForAssertion(() => Assert.Contains("The trash is empty", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Empty trash", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shows_the_trash_off_notice()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Trash = false });

        var cut = Render<Trash>();

        Assert.Contains("The trash is turned off", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restores_a_note()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var note = await TrashedNoteAsync(app);
        var cut = Render<Trash>();
        cut.WaitForAssertion(() => cut.Find("article"), TimeSpan.FromSeconds(5));

        cut.FindAll("button").First(b => b.TextContent.Contains("Restore", StringComparison.Ordinal)).Click();

        await WaitUntilAsync(async () => !(await app.Core.Notes.GetAsync(note.Id))!.IsTrashed);
    }

    [Fact]
    public async Task Deletes_forever_after_confirming()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var note = await TrashedNoteAsync(app);
        var cut = Render<Trash>();
        cut.WaitForAssertion(() => cut.Find("article"), TimeSpan.FromSeconds(5));

        cut.FindAll("button").First(b => b.TextContent.Contains("Delete forever", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Equal("Delete forever?", cut.Find("h2").TextContent), TimeSpan.FromSeconds(5));
        ClickButtonExact(cut, "Delete forever");

        await WaitUntilAsync(async () => await app.Core.Notes.GetAsync(note.Id) is null);
    }

    [Fact]
    public async Task Empties_the_trash_after_confirming()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        await TrashedNoteAsync(app, "one");
        await TrashedNoteAsync(app, "two");
        var cut = Render<Trash>();
        // Waits for the button itself, not just the articles: it is driven by NoteList's separate OnLoaded
        // callback, a render hop later than the list's own content.
        cut.WaitForState(() => cut.FindAll("button").Any(b => b.TextContent.Contains("Empty trash", StringComparison.Ordinal)), TimeSpan.FromSeconds(5));

        cut.FindAll("button").First(b => b.TextContent.Contains("Empty trash", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Equal("Empty the trash?", cut.Find("h2").TextContent), TimeSpan.FromSeconds(5));
        ClickButtonExact(cut, "Empty trash");

        await WaitUntilAsync(async () => (await app.Core.Notes.ListAsync(new NoteQuery(NoteState.Trash, NoteKinds.All))).Items.Count == 0);
    }

    /// <summary>Waits for a button with this exact text to exist, then clicks it: a plain FindAll+Click right
    /// after a WaitForAssertion on something else (the dialog's heading) was seen to race under load, since
    /// nothing guarantees the footer has committed in the same check as the heading.</summary>
    private static void ClickButtonExact(IRenderedComponent<FalconNotes.UI.Pages.Trash> cut, string text)
    {
        cut.WaitForState(() => cut.FindAll("button").Any(b => b.TextContent.Trim() == text), TimeSpan.FromSeconds(5));
        cut.FindAll("button").First(b => b.TextContent.Trim() == text).Click();
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
}
