using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Components;
using FalconNotes.UI.Pages;
using FalconNotes.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using TodoPage = FalconNotes.UI.Pages.Todo;
using TodoText = FalconNotes.Core.Text.Todo;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New tests (docs/11): locked notes are this app's own, with no reference test to port (docs/07, Locked notes).
/// Covers Lock and Unlock in the ⋯ menus, what a locked card shows and offers, and the PIN being asked for wherever
/// a locked note's text would otherwise get out: an export, the automatic backups' folder, a new app lock.
/// </summary>
/// <remarks>
/// Checking or saving a PIN is 210,000 rounds of PBKDF2 off the render thread, so every check after one waits for it.
/// </remarks>
public class LockedNotesTests : BunitContext
{
    private const string Pin = "2580";
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(10);

    private void SetUpJs()
    {
        var gestures = JSInterop.SetupModule("./_content/FalconNotes.UI/js/gestures.js");
        gestures.SetupVoid("bindDoubleTap", _ => true).SetVoidResult();
        gestures.SetupVoid("unbindDoubleTap", _ => true).SetVoidResult();
        var markdown = JSInterop.SetupModule("./_content/FalconNotes.UI/js/markdown.js");
        markdown.SetupVoid("bindTaskToggle", _ => true).SetVoidResult();
        markdown.SetupVoid("unbindTaskToggle", _ => true).SetVoidResult();
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("openMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("closeMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("showModal", _ => true).SetVoidResult();
        dialogs.SetupVoid("close", _ => true).SetVoidResult();
        var editor = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        editor.SetupVoid("bindAutoGrow", _ => true).SetVoidResult();
        editor.SetupVoid("focusAtEnd", _ => true).SetVoidResult();
        editor.SetupVoid("focus", _ => true).SetVoidResult();
        var observe = JSInterop.SetupModule("./_content/FalconNotes.UI/js/observe.js");
        observe.SetupVoid("bindSentinel", _ => true).SetVoidResult();
    }

    private static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> cut, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text);

    /// <summary>Opens a card's menu and waits for its items: a click made while a list is still rendering what it
    /// loaded waits its turn, so the menu is not open yet when <c>Click</c> returns (1 run in 6 on CI).</summary>
    private static List<string> MenuItems<T>(IRenderedComponent<T> cut, string menu)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        cut.Find($"button[aria-label='{menu}']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button[role=menuitem]")), Slow);
        return cut.FindAll("button[role=menuitem]").Select(b => b.TextContent.Trim()).ToList();
    }

    private static void Choose<T>(IRenderedComponent<T> cut, string menu, string item)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        Assert.Contains(item, MenuItems(cut, menu));
        cut.FindAll("button[role=menuitem]").First(b => b.TextContent.Trim() == item).Click();
    }

    /// <summary>Types into the open PIN dialog's fields (one to check a PIN, two to choose one) and confirms.</summary>
    private static void EnterPin<T>(IRenderedComponent<T> cut, string confirm, string pin = Pin)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input[type=password]")));
        for (var i = 0; i < cut.FindAll("input[type=password]").Count; i++)
        {
            cut.FindAll("input[type=password]")[i].Input(pin);
        }

        Button(cut, confirm).Click();
    }

    /// <summary>A note locked as the app would leave it: a PIN chosen, the app lock off.</summary>
    private static async Task<Note> LockedAsync(UiTestApp app, string content, NoteKind kind = NoteKind.Note)
    {
        var note = await app.PostAsync(content, kind);
        if (!await app.AppLock.PinInUseAsync())
        {
            await app.AppLock.SetPinAsync(Pin);
        }

        return (await app.Core.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true)))!;
    }

    [Fact]
    public async Task The_first_lock_asks_for_a_PIN_and_leaves_the_app_lock_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var note = await app.PostAsync("the door code is 4711");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        Choose(cut, "Note actions", "Lock");

        cut.WaitForAssertion(() => Assert.Contains("Choose a PIN of 4 to 8 digits for your locked notes.", cut.Markup, StringComparison.Ordinal));
        Assert.False((await app.Core.Notes.GetAsync(note.Id))!.IsLocked); // not before there is a PIN to open it with
        EnterPin(cut, "Set PIN");

        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Locked. Unlocking it takes your PIN."), Slow);
        Assert.True((await app.Core.Notes.GetAsync(note.Id))!.IsLocked);
        Assert.False(app.AppLock.Settings.Enabled);
        Assert.True(await app.AppLock.PinInUseAsync());
    }

    [Fact]
    public async Task With_a_PIN_in_use_Lock_locks_at_once()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        await app.AppLock.EnableAsync(Pin);
        var note = await app.PostAsync("hello");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        Choose(cut, "Note actions", "Lock");

        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Locked. Unlocking it takes your PIN."), Slow);
        Assert.True((await app.Core.Notes.GetAsync(note.Id))!.IsLocked);
        Assert.Empty(cut.FindAll("input[type=password]"));
    }

    [Fact]
    public async Task A_note_just_unlocked_locks_again_without_choosing_a_PIN_again()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var locked = await LockedAsync(app, "the door code is 4711");
        var card = Render<NoteCard>(p => p.Add(c => c.Note, locked));
        Choose(card, "Note actions", "Unlock…");
        EnterPin(card, "Continue");
        card.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Unlocked."), Slow);
        Assert.False(await app.AppLock.PinInUseAsync()); // nothing is locked now, and the app lock is off

        var unlocked = (await app.Core.Notes.GetAsync(locked.Id))!;
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, unlocked));
        Choose(cut, "Note actions", "Lock");

        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Locked. Unlocking it takes your PIN."), Slow);
        Assert.True((await app.Core.Notes.GetAsync(locked.Id))!.IsLocked);
        Assert.Empty(cut.FindAll("input[type=password]"));
        Assert.True(await app.AppLock.TryPinAsync(Pin)); // the same PIN still opens it
    }

    [Fact]
    public async Task A_PIN_nobody_has_typed_since_the_app_started_is_chosen_anew_before_it_locks_a_note()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();

        // As after a restart: a PIN is stored, left by an app lock turned off or by notes unlocked since.
        await Services.GetRequiredService<Core.Settings.AppLockService>().SetPinAsync("1111");
        var note = await app.PostAsync("hello");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        Choose(cut, "Note actions", "Lock");

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("input[type=password]").Count)); // Set a PIN
        Assert.False((await app.Core.Notes.GetAsync(note.Id))!.IsLocked);
        EnterPin(cut, "Set PIN");
        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Locked. Unlocking it takes your PIN."), Slow);
        Assert.True(await app.AppLock.TryPinAsync(Pin));
        Assert.False(await app.AppLock.TryPinAsync("1111")); // the forgotten one opens nothing
    }

    [Fact]
    public async Task A_locked_note_shows_the_lock_and_nothing_of_what_is_in_it()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { NoteTitles = true, Labels = true, DoubleTapToEdit = true, DailyNotes = true });
        SetUpJs();
        var label = await app.Core.Labels.CreateAsync("Private", LabelColor.Red);
        var file = await app.Core.AddFileAsync("passport-scan.png");
        var note = await app.Core.Notes.CreateAsync("# Passport\n\nthe number is X1234567 #papers", attachmentIds: [file.Id]);
        await app.Core.Notes.PatchAsync(note.Id, new NotePatch(LabelIds: [label.Id]));
        await app.AppLock.SetPinAsync(Pin);
        var locked = (await app.Core.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true)))!;

        var cut = Render<NoteCard>(p => p.Add(c => c.Note, locked));

        Assert.Single(cut.FindAll("[role=img][aria-label=Locked]"));
        Assert.Contains("This note is locked. Choose Unlock in its ⋯ menu to open it.", cut.Markup, StringComparison.Ordinal);
        foreach (var hidden in new[] { "Passport", "X1234567", "papers", "passport-scan", "Private", file.Id.ToString("D") })
        {
            Assert.DoesNotContain(hidden, cut.Markup, StringComparison.Ordinal);
        }

        // Nothing in the menu shows the text either: no Edit, Copy text, Labels… or template.
        Assert.Equal(["Pin to top", "Unlock…", "Archive", "Move to trash"], MenuItems(cut, "Note actions"));
        Assert.Empty(JSInterop.Invocations["bindDoubleTap"]);
    }

    [Fact]
    public async Task Unlock_asks_for_the_PIN_and_a_wrong_one_leaves_the_note_locked()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var locked = await LockedAsync(app, "the door code is 4711");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, locked));

        Choose(cut, "Note actions", "Unlock…");
        cut.WaitForAssertion(() => Assert.Contains("Unlock this note", cut.Markup, StringComparison.Ordinal));
        EnterPin(cut, "Continue", "0000");

        cut.WaitForAssertion(() => Assert.Contains("That PIN is not right.", cut.Markup, StringComparison.Ordinal), Slow);
        Assert.True((await app.Core.Notes.GetAsync(locked.Id))!.IsLocked);

        EnterPin(cut, "Continue");

        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Unlocked."), Slow);
        Assert.False((await app.Core.Notes.GetAsync(locked.Id))!.IsLocked);
    }

    [Fact]
    public async Task After_too_many_wrong_PINs_the_dialog_says_to_wait()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var locked = await LockedAsync(app, "secret");
        for (var i = 0; i < 4; i++)
        {
            await app.AppLock.TryPinAsync("0000");
        }

        var cut = Render<NoteCard>(p => p.Add(c => c.Note, locked));
        Choose(cut, "Note actions", "Unlock…");
        EnterPin(cut, "Continue", "0000"); // the fifth

        cut.WaitForAssertion(() => Assert.Contains("Too many tries. Try again in 30 seconds.", cut.Markup, StringComparison.Ordinal), Slow);
    }

    [Fact]
    public async Task A_habit_in_a_mixed_list_is_not_offered_a_lock()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var habit = await app.PostAsync("Stretch", NoteKind.Habit);

        var cut = Render<NoteCard>(p => p.Add(c => c.Note, habit));

        Assert.DoesNotContain("Lock", MenuItems(cut, "Note actions"));
    }

    [Fact]
    public async Task A_todo_list_locks_from_its_menu_and_then_shows_as_a_locked_card_in_the_Todo_tab()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var list = await app.PostAsync(TodoText.Serialize(new Core.Text.TodoList("Presents", [new("a watch for Sam", false)])), NoteKind.Todo);
        var page = Render<TodoPage>();
        page.WaitForAssertion(() => Assert.Contains("a watch for Sam", page.Markup, StringComparison.Ordinal), Slow);

        Choose(page, "List actions", "Lock");
        EnterPin(page, "Set PIN"); // the first lock on this device

        page.WaitForAssertion(() => Assert.Contains("This list is locked.", page.Markup, StringComparison.Ordinal), Slow);
        Assert.True((await app.Core.Notes.GetAsync(list.Id))!.IsLocked);
        Assert.DoesNotContain("Presents", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("a watch for Sam", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("input[type=checkbox]"));
        Assert.Contains("Unlock…", MenuItems(page, "Note actions"));
    }

    [Fact]
    public async Task A_locked_note_stays_hidden_in_the_trash()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var locked = await LockedAsync(app, "the door code is 4711");
        await app.Core.Notes.PatchAsync(locked.Id, new NotePatch(IsTrashed: true));

        var cut = Render<Trash>();

        cut.WaitForAssertion(() => Assert.Contains("This note is locked. Restore it,", cut.Markup, StringComparison.Ordinal), Slow);
        Assert.DoesNotContain("4711", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[role=img][aria-label=Locked]"));
    }

    [Fact]
    public async Task Exporting_asks_for_the_PIN_while_a_note_is_locked()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        await LockedAsync(app, "the door code is 4711");
        var cut = Render<BackupSection>();

        Button(cut, "Export ZIP…").Click();

        cut.WaitForAssertion(() => Assert.Contains("The export holds your locked notes too", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(app.Saver.Saved);
        EnterPin(cut, "Continue");

        cut.WaitForAssertion(() => Assert.Single(app.Saver.Saved), Slow);
        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Exported 1 note and 0 files."), Slow);
    }

    [Fact]
    public async Task Exporting_asks_for_nothing_while_no_note_is_locked()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        await app.PostAsync("nothing to hide");
        var cut = Render<BackupSection>();

        Button(cut, "Export ZIP…").Click();

        cut.WaitForAssertion(() => Assert.Single(app.Saver.Saved), Slow);
        Assert.Empty(cut.FindAll("input[type=password]"));
    }

    [Fact]
    public async Task Choosing_the_automatic_backups_folder_asks_for_the_PIN_while_a_note_is_locked()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        await LockedAsync(app, "the door code is 4711");
        var cut = Render<AutoBackupSection>();
        cut.WaitForAssertion(() => cut.Find("button[role=switch][aria-label='Back up automatically']"));

        cut.Find("button[role=switch][aria-label='Back up automatically']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Automatic backups hold your locked notes too", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(app.BackupFolders.Backups);
        EnterPin(cut, "Continue");

        cut.WaitForAssertion(() => Assert.Single(app.BackupFolders.Backups), Slow);
    }

    [Fact]
    public async Task Turning_the_app_lock_on_asks_for_the_PIN_locked_notes_use_instead_of_a_new_one()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        await LockedAsync(app, "the door code is 4711");
        var cut = Render<AppLockSection>();
        cut.WaitForAssertion(() => Assert.Contains("Your locked notes use a PIN.", cut.Markup, StringComparison.Ordinal));
        Button(cut, "Change PIN…"); // the PIN can be changed with the lock off

        cut.Find("button[role=switch][aria-label='Lock Falcon Notes']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Your locked notes already use a PIN", cut.Markup, StringComparison.Ordinal));
        Assert.Single(cut.FindAll("input[type=password]")); // the PIN there is, not "PIN" and "Repeat PIN"
        EnterPin(cut, "Continue", "0000");
        cut.WaitForAssertion(() => Assert.Contains("That PIN is not right.", cut.Markup, StringComparison.Ordinal), Slow);
        Assert.False(app.AppLock.Settings.Enabled);

        EnterPin(cut, "Continue");

        cut.WaitForAssertion(() => Assert.True(app.AppLock.Settings.Enabled), Slow);
        Assert.True(await app.AppLock.TryPinAsync(Pin));
    }

    [Fact]
    public async Task With_no_locked_note_the_app_lock_still_starts_with_a_new_PIN()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var cut = Render<AppLockSection>();

        cut.Find("button[role=switch][aria-label='Lock Falcon Notes']").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("input[type=password]").Count));
        Assert.DoesNotContain("Your locked notes", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Change PIN…");
    }
}
