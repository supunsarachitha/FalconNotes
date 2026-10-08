using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Components;
using Microsoft.AspNetCore.Components.Web;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Port of the applicable cases of TodoCard.test.tsx and TodoPage's TodoCard-focused cases (docs/11). Not ported:
/// "says why a change could not be saved when the storage is full" (no quota offline, docs/04); "sends quick changes
/// one after another, in order" and the "refreshes" describe block, which test NoteEditor's own save-ordering and
/// stale-reload rules — already covered generically by NoteEditorTests for any <c>T</c>, TodoList included; and the
/// caret-position half of "puts the caret at the end…", since bUnit has no real focus or selection to observe (the
/// JS calls that would do it are asserted instead).
/// </summary>
public class TodoCardTests : BunitContext
{
    private void SetUpDialogsJs()
    {
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("openMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("closeMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("showModal", _ => true).SetVoidResult();
        dialogs.SetupVoid("close", _ => true).SetVoidResult();
    }

    private Bunit.BunitJSModuleInterop SetUpEditorJs()
    {
        var module = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        module.SetupVoid("bindAutoGrow", _ => true).SetVoidResult();
        module.SetupVoid("focusAtEnd", _ => true).SetVoidResult();
        module.SetupVoid("focus", _ => true).SetVoidResult();
        return module;
    }

    private static void OpenMenu(IRenderedComponent<TodoCard> cut) => cut.Find("button[aria-label='List actions']").Click();

    private static AngleSharp.Dom.IElement MenuItem(IRenderedComponent<TodoCard> cut, string text) =>
        cut.FindAll("button[role=menuitem]").First(b => b.TextContent.Trim() == text);

    private static AngleSharp.Dom.IElement Checkbox(IRenderedComponent<TodoCard> cut, string label) =>
        cut.FindAll("input[type=checkbox]").First(c => c.GetAttribute("aria-label") == label);

    private static string Value(AngleSharp.Dom.IElement textarea) => textarea.GetAttribute("value") ?? "";

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

    private static Task<bool> ContentIsAsync(UiTestApp app, Guid id, string content) =>
        app.Core.Notes.GetAsync(id).ContinueWith(t => t.Result!.Content == content);

    [Fact]
    public async Task Shows_the_list_with_its_progress()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpEditorJs();
        var note = await app.PostAsync("# Groceries\n\n- [ ] oats\n- [x] maple syrup", NoteKind.Todo);

        var cut = Render<TodoCard>(p => p.Add(c => c.Note, note));

        Assert.Equal("Groceries", cut.Find("h3").TextContent);
        cut.Find("[aria-label='1 of 2 done']");
        Assert.False(Checkbox(cut, "oats").HasAttribute("checked"));
        Assert.True(Checkbox(cut, "maple syrup").HasAttribute("checked"));
    }

    [Fact]
    public async Task Ticks_adds_edits_and_removes_items_saving_the_list_as_markdown()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpEditorJs();
        var note = await app.PostAsync("# Groceries\n\n- [ ] oats\n- [x] maple syrup", NoteKind.Todo);
        var cut = Render<TodoCard>(p => p.Add(c => c.Note, note));

        Checkbox(cut, "oats").Change(true);
        await WaitUntilAsync(() => ContentIsAsync(app, note.Id, "# Groceries\n\n- [x] oats\n- [x] maple syrup"));

        cut.Find("input[aria-label='Add an item to Groceries']").Input("blueberries");
        cut.Find("form").Submit();
        await WaitUntilAsync(() => ContentIsAsync(app, note.Id, "# Groceries\n\n- [x] oats\n- [x] maple syrup\n- [ ] blueberries"));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "blueberries").Click();
        var edit = cut.Find("input[aria-label='Edit item']");
        edit.Input("wild blueberries");
        edit.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        await WaitUntilAsync(() => ContentIsAsync(app, note.Id, "# Groceries\n\n- [x] oats\n- [x] maple syrup\n- [ ] wild blueberries"));

        cut.Find("button[aria-label='Remove oats']").Click();
        await WaitUntilAsync(() => ContentIsAsync(app, note.Id, "# Groceries\n\n- [x] maple syrup\n- [ ] wild blueberries"));
    }

    [Fact]
    public async Task Clears_completed_items_and_renames_the_list_from_its_menu()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpEditorJs();
        SetUpDialogsJs();
        var note = await app.PostAsync("# Groceries\n\n- [ ] oats\n- [x] maple syrup", NoteKind.Todo);
        var cut = Render<TodoCard>(p => p.Add(c => c.Note, note));

        OpenMenu(cut);
        MenuItem(cut, "Clear completed").Click();
        await WaitUntilAsync(() => ContentIsAsync(app, note.Id, "# Groceries\n\n- [ ] oats"));

        OpenMenu(cut);
        MenuItem(cut, "Rename").Click();
        var name = cut.Find("input[aria-label='List name']");
        name.Input("Weekend groceries");
        name.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        await WaitUntilAsync(() => ContentIsAsync(app, note.Id, "# Weekend groceries\n\n- [ ] oats"));
    }

    [Fact]
    public async Task Edits_all_the_items_at_once_as_markdown()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpEditorJs();
        SetUpDialogsJs();
        var note = await app.PostAsync("# Groceries\n\n- [ ] oats\n- [x] maple syrup", NoteKind.Todo);
        var cut = Render<TodoCard>(p => p.Add(c => c.Note, note));

        OpenMenu(cut);
        MenuItem(cut, "Edit as Markdown").Click();
        var box = cut.Find("textarea[aria-label='Items in Groceries as Markdown']");
        Assert.Equal("- [ ] oats\n- [x] maple syrup", Value(box));
        Assert.Empty(cut.FindAll("input[type=checkbox]"));

        box.Input("- [x] oats\n- [ ] maple syrup\nflour\n\n* [X] eggs");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();

        await WaitUntilAsync(() => ContentIsAsync(app, note.Id, "# Groceries\n\n- [x] oats\n- [ ] maple syrup\n- [ ] flour\n- [x] eggs"));
        Assert.Empty(cut.FindAll("textarea[aria-label='Items in Groceries as Markdown']"));
        Assert.False(Checkbox(cut, "flour").HasAttribute("checked"));
        cut.Find("[aria-label='2 of 4 done']");
    }

    [Fact]
    public async Task Leaves_the_list_as_it_was_when_editing_as_markdown_is_cancelled_or_changes_nothing()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpEditorJs();
        SetUpDialogsJs();
        var note = await app.PostAsync("# Groceries\n\n- [ ] oats\n- [x] maple syrup", NoteKind.Todo);
        var cut = Render<TodoCard>(p => p.Add(c => c.Note, note));

        OpenMenu(cut);
        MenuItem(cut, "Edit as Markdown").Click();
        var box = cut.Find("textarea[aria-label='Items in Groceries as Markdown']");
        box.Input("- [ ] oats\n- [x] maple syrup\n- [ ] bread");
        box.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Checkbox(cut, "oats");

        OpenMenu(cut);
        MenuItem(cut, "Edit as Markdown").Click();
        box = cut.Find("textarea[aria-label='Items in Groceries as Markdown']");
        Assert.Equal("- [ ] oats\n- [x] maple syrup", Value(box)); // the cancelled change is gone
        box.KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true }); // no change made: saving does nothing

        Assert.Empty(cut.FindAll("textarea[aria-label='Items in Groceries as Markdown']"));
        Assert.Equal("# Groceries\n\n- [ ] oats\n- [x] maple syrup", (await app.Core.Notes.GetAsync(note.Id))!.Content);
    }

    [Fact]
    public async Task Moves_a_list_to_the_trash_and_puts_labels_on_it_while_labels_are_on()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = true });
        SetUpEditorJs();
        SetUpDialogsJs();
        var home = await app.Core.Labels.CreateAsync("Home");
        var created = await app.PostAsync("# Groceries\n\n- [ ] oats", NoteKind.Todo);
        var note = (await app.Core.Notes.PatchAsync(created.Id, new NotePatch(LabelIds: [home.Id])))!;
        var cut = Render<TodoCard>(p => p.Add(c => c.Note, note));
        cut.WaitForState(() => cut.FindAll("ul[aria-label=Labels]").Count > 0);

        Assert.Equal($"/?label={Uri.EscapeDataString(home.Id.ToString())}", cut.Find("a[href^='/?label=']").GetAttribute("href"));
        OpenMenu(cut);
        // On a busy machine the menu is not open yet when the click returns (about 1 run in 50 failed here).
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button[role=menuitem]"), b => b.TextContent.Trim() == "Labels…"));
        MenuItem(cut, "Move to trash").Click();

        await WaitUntilAsync(() => Task.FromResult(app.Toasts.Current.Any(t => t.Message == "List moved to the trash.")));
        Assert.True((await app.Core.Notes.GetAsync(note.Id))!.IsTrashed);
    }
}
