using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Components;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Port of the applicable cases of NoteCard.test.tsx (docs/11). gestures.js's own timing and target-exclusion rules
/// ("leaves links, checkboxes and the menu alone", slow or far-apart taps) run in the browser and are not
/// reproducible in bUnit, so this covers what is: whether double-tap is bound at all (on/off, archived), and that a
/// double-tap, once it reaches .NET, opens editing. Checkbox toggling and Copy text are new cases for this port's
/// own glue (NoteEditor, IClipboard) that the reference does not need.
/// </summary>
public class NoteCardTests : BunitContext
{
    private static Note MakeNote(
        NoteKind kind = NoteKind.Note, bool isPinned = false, DateTime? archivedAtUtc = null) => new(
        Guid.CreateVersion7(), kind, "Call the plumber about the sink\n\n- [ ] book a time", null, isPinned, archivedAtUtc, null,
        DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-10), 0, [], [], []);

    private void SetUpGesturesJs(out Bunit.BunitJSModuleInterop module)
    {
        module = JSInterop.SetupModule("./_content/FalconNotes.UI/js/gestures.js");
        module.SetupVoid("bindDoubleTap", _ => true).SetVoidResult();
        module.SetupVoid("unbindDoubleTap", _ => true).SetVoidResult();
        var markdown = JSInterop.SetupModule("./_content/FalconNotes.UI/js/markdown.js");
        markdown.SetupVoid("bindTaskToggle", _ => true).SetVoidResult();
        markdown.SetupVoid("unbindTaskToggle", _ => true).SetVoidResult();
    }

    private void SetUpDialogsJs()
    {
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("openMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("closeMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("showModal", _ => true).SetVoidResult();
        dialogs.SetupVoid("close", _ => true).SetVoidResult();
    }

    private static void OpenMenu(IRenderedComponent<NoteCard> cut) => cut.Find("button[aria-label='Note actions']").Click();

    private static AngleSharp.Dom.IElement MenuItem(IRenderedComponent<NoteCard> cut, string text) =>
        cut.FindAll("button[role=menuitem]").First(b => b.TextContent.Trim() == text);

    /// <summary>Polls with a plain await, never a blocking one: a blocking wait on the renderer's own thread would
    /// deadlock a pending save that needs that same thread to resume on (found the hard way in LabelPickerTests and
    /// ComposerTests).</summary>
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
    public async Task Shows_the_kind_label_for_a_todo_list_and_the_pinned_badge()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpGesturesJs(out _);
        var note = MakeNote(kind: NoteKind.Todo, isPinned: true);

        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        Assert.Contains("Todo list", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Pinned", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renders_the_title_and_body_separately_when_note_titles_are_on()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { NoteTitles = true });
        SetUpGesturesJs(out _);
        var note = MakeNote() with { Content = "# Groceries\n\n- [ ] oats" };

        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        Assert.Equal("Groceries", cut.Find("h3").TextContent);
        Assert.DoesNotContain("Groceries", cut.Find(".markdown").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_bind_double_tap_for_an_archived_note()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { DoubleTapToEdit = true });
        SetUpGesturesJs(out var module);
        var note = MakeNote(archivedAtUtc: DateTime.UtcNow);

        Render<NoteCard>(p => p.Add(c => c.Note, note));

        Assert.Empty(module.Invocations["bindDoubleTap"]);
    }

    [Fact]
    public async Task Binds_double_tap_when_the_preference_is_on_for_an_active_note()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { DoubleTapToEdit = true });
        SetUpGesturesJs(out var module);
        var note = MakeNote();

        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        cut.WaitForAssertion(() => Assert.Single(module.Invocations["bindDoubleTap"]));
    }

    [Fact]
    public async Task A_double_tap_opens_the_note_for_editing()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { DoubleTapToEdit = true });
        SetUpGesturesJs(out _);
        var note = MakeNote();
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        await cut.InvokeAsync(cut.Instance.OnDoubleTap);

        cut.Find($"#edit-{note.Id}");
    }

    [Fact]
    public async Task Ticking_a_checkbox_saves_the_change_in_the_background()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpGesturesJs(out _);
        var note = await app.PostAsync("Plan\n\n- [ ] book flights");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));
        var markdown = cut.FindComponent<Markdown>();
        var marker = note.Content.IndexOf("- [ ]", StringComparison.Ordinal);

        await cut.InvokeAsync(() => markdown.Instance.ToggleTask(marker));

        await WaitUntilAsync(async () => (await app.Core.Notes.GetAsync(note.Id))!.Content.Contains("- [x] book flights", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pin_toggles_and_shows_a_toast()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpGesturesJs(out _);
        SetUpDialogsJs();
        var note = await app.PostAsync("hello");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        OpenMenu(cut);
        MenuItem(cut, "Pin to top").Click();

        // Only the in-memory toast here: the PatchAsync it follows has already completed by the time it shows.
        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Pinned to the top."));
        Assert.True((await app.Core.Notes.GetAsync(note.Id))!.IsPinned);
    }

    [Fact]
    public async Task Copy_text_copies_the_current_content_to_the_clipboard()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpGesturesJs(out _);
        SetUpDialogsJs();
        var note = await app.PostAsync("Call the plumber");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        OpenMenu(cut);
        MenuItem(cut, "Copy text").Click();

        cut.WaitForAssertion(() => Assert.Single(app.Clipboard.Copied));
        Assert.Equal("Call the plumber", app.Clipboard.Copied[0]);
    }

    [Fact]
    public async Task Moves_to_the_trash_at_once_and_undo_restores_it()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpGesturesJs(out _);
        SetUpDialogsJs();
        var note = await app.PostAsync("hello");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        OpenMenu(cut);
        MenuItem(cut, "Move to trash").Click();

        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Note moved to the trash."));
        Assert.True((await app.Core.Notes.GetAsync(note.Id))!.IsTrashed);

        var toast = app.Toasts.Current.Single(t => t.Message == "Note moved to the trash.");
        await toast.Action!.OnClick();

        await WaitUntilAsync(async () => !(await app.Core.Notes.GetAsync(note.Id))!.IsTrashed);
    }

    [Fact]
    public async Task Asks_first_and_deletes_for_good_when_the_trash_is_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Trash = false });
        SetUpGesturesJs(out _);
        SetUpDialogsJs();
        var note = await app.PostAsync("hello");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        OpenMenu(cut);
        MenuItem(cut, "Delete…").Click();

        // The "open" attribute is only ever set by the real browser's showModal(); the mocked JS here never
        // touches the DOM, so Open's own Razor-conditional content is what the test waits for instead.
        cut.WaitForAssertion(() => cut.Find("h2"));
        Assert.Equal("Delete this note?", cut.Find("h2").TextContent);
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Delete").Click();

        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Note deleted."));
        Assert.Null(await app.Core.Notes.GetAsync(note.Id));
    }

    [Fact]
    public async Task Hides_labels_and_their_menu_item_while_turned_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpGesturesJs(out _);
        SetUpDialogsJs();
        var note = await app.PostAsync("hello");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        OpenMenu(cut);

        Assert.DoesNotContain(cut.FindAll("button[role=menuitem]"), b => b.TextContent.Contains("Labels", StringComparison.Ordinal));
    }
}
