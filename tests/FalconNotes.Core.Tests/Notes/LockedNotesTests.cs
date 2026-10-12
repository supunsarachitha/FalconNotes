using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Settings;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Notes;

/// <summary>
/// New tests (docs/11): locked notes are this app's own, with no reference to port from (docs/04, Locked notes).
/// Covers the lock itself, what a locked note is left out of, and the PIN being in use with the app lock off.
/// </summary>
public class LockedNotesTests
{
    private static readonly NoteKind[] Timeline = [NoteKind.Note];

    private static NoteQuery Active(string? tag = null, string? search = null, Guid? label = null) =>
        new(NoteState.Active, Timeline, tag, label, Search: search);

    [Fact]
    public async Task Locking_and_unlocking_change_the_note_but_not_when_it_was_last_written()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("the door code is 4711");
        app.Clock.Advance(TimeSpan.FromHours(1));

        var locked = await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true));

        Assert.True(locked!.IsLocked);
        Assert.Equal(note.UpdatedAtUtc, locked.UpdatedAtUtc);
        Assert.Equal(note.Revision + 1, locked.Revision);
        Assert.Equal("the door code is 4711", locked.Content); // the text stays, for exports and for the unlocked note
        Assert.True(await app.Notes.AnyLockedAsync());

        var unlocked = await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: false));

        Assert.False(unlocked!.IsLocked);
        Assert.False(await app.Notes.AnyLockedAsync());
    }

    [Fact]
    public async Task Other_changes_leave_the_lock_as_it_is()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("secret");
        await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true));

        var pinned = await app.Notes.PatchAsync(note.Id, new NotePatch(IsPinned: true));
        var trashed = await app.Notes.PatchAsync(note.Id, new NotePatch(IsTrashed: true));

        Assert.True(pinned!.IsLocked);
        Assert.True(trashed!.IsLocked);
        Assert.True(await app.Notes.AnyLockedAsync()); // in the trash it can still be restored, so the PIN stays in use
    }

    [Fact]
    public async Task A_habit_cannot_be_locked()
    {
        using var app = await TestApp.StartAsync();
        var habit = await app.Notes.CreateAsync("Stretch", NoteKind.Habit);

        var error = await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.PatchAsync(habit.Id, new NotePatch(IsLocked: true)));

        Assert.Equal("Habits cannot be locked.", error.Message);
        Assert.False((await app.Notes.GetAsync(habit.Id))!.IsLocked);
    }

    [Fact]
    public async Task A_locked_note_stays_in_its_lists_but_is_never_a_search_match()
    {
        using var app = await TestApp.StartAsync();
        var file = await app.AddFileAsync("passport-scan.png");
        var locked = await app.Notes.CreateAsync("the passport number", attachmentIds: [file.Id]);
        var open = await app.Notes.CreateAsync("renew the passport");
        await app.Notes.PatchAsync(locked.Id, new NotePatch(IsLocked: true));

        var feed = await app.Notes.ListAsync(new NoteQuery(NoteState.Feed, Timeline));
        var byText = await app.Notes.ListAsync(Active(search: "passport"));
        var byFileName = await app.Notes.ListAsync(Active(search: "scan"));

        Assert.Equal(2, feed.Items.Count);
        Assert.Equal([open.Id], byText.Items.Select(n => n.Id));
        Assert.Empty(byFileName.Items);
    }

    [Fact]
    public async Task A_locked_note_is_left_out_of_its_tags_but_not_of_its_labels()
    {
        using var app = await TestApp.StartAsync();
        var label = await app.Labels.CreateAsync("Private", LabelColor.Red);
        var locked = await app.Notes.CreateAsync("bank details #money");
        var open = await app.Notes.CreateAsync("budget #money");
        await app.Notes.PatchAsync(locked.Id, new NotePatch(LabelIds: [label.Id]));
        await app.Notes.PatchAsync(locked.Id, new NotePatch(IsLocked: true));

        var byTag = await app.Notes.ListAsync(Active(tag: "money"));
        var counts = await app.Notes.TagCountsAsync(Timeline);
        var byLabel = await app.Notes.ListAsync(Active(label: label.Id));

        Assert.Equal([open.Id], byTag.Items.Select(n => n.Id));
        Assert.Equal([new TagCount("money", 1)], counts);
        Assert.Equal([locked.Id], byLabel.Items.Select(n => n.Id)); // a hiding label's page is the only way back to it
    }

    [Fact]
    public async Task A_locked_note_s_files_are_opened_only_while_the_note_is_open_to_be_read()
    {
        using var app = await TestApp.StartAsync();
        var file = await app.AddFileAsync("statement.pdf");
        var note = await app.Notes.CreateAsync("statement", attachmentIds: [file.Id]);
        var folder = Path.Combine(app.Directories.CacheDirectory, "open");
        await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true));

        Assert.Null(await app.Attachments.OpenAsync(file.Id));
        Assert.Null(await app.Attachments.CopyDecryptedAsync(file.Id, folder));

        app.Locks.Open(note.Id); // unlocked with the PIN, to be read
        await using (var opened = (await app.Attachments.OpenAsync(file.Id))!.Content)
        {
            Assert.Equal(100, opened.Length);
        }

        Assert.NotNull(await app.Attachments.CopyDecryptedAsync(file.Id, folder));

        app.Locks.CloseAll(); // the page or the app was left
        Assert.Null(await app.Attachments.OpenAsync(file.Id));

        await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: false)); // the lock taken off for good
        await using var unlocked = (await app.Attachments.OpenAsync(file.Id))!.Content;
        Assert.Equal(100, unlocked.Length);
    }

    [Fact]
    public async Task An_open_note_is_still_locked_for_search_and_tags()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("bank details #money");
        await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true));

        app.Locks.Open(note.Id);

        Assert.True((await app.Notes.GetAsync(note.Id))!.IsLocked); // opening never touches the stored lock
        Assert.Empty((await app.Notes.ListAsync(Active(search: "bank"))).Items);
        Assert.Empty((await app.Notes.ListAsync(Active(tag: "money"))).Items);
        Assert.True(await app.Notes.AnyLockedAsync());
    }

    [Fact]
    public void Open_notes_lock_again_together_and_only_in_memory()
    {
        var locks = new NoteLocks();
        var (first, second) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var changes = 0;
        locks.Changed += () => changes++;

        locks.Open(first);
        locks.Open(second);
        Assert.True(locks.IsOpen(first));
        Assert.Equal(2, changes);

        locks.Close(first); // Lock now
        Assert.False(locks.IsOpen(first));
        Assert.True(locks.IsOpen(second));

        locks.CloseAll(); // the page or the app was left
        Assert.False(locks.IsOpen(second));
        Assert.Equal(4, changes);

        locks.CloseAll(); // nothing open: nobody is told
        Assert.Equal(4, changes);
        Assert.False(new NoteLocks().IsOpen(second)); // a new run of the app starts with every note locked
    }

    [Fact]
    public void A_note_being_edited_stays_open_until_the_edit_ends()
    {
        var locks = new NoteLocks();
        var (edited, read) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        locks.Open(edited);
        locks.Open(read);
        locks.Hold(edited);

        locks.CloseAll(); // off to the file picker
        Assert.True(locks.IsOpen(edited));
        Assert.False(locks.IsOpen(read));

        locks.Release(edited); // saved or cancelled: it locks now, because the app was left meanwhile
        Assert.False(locks.IsOpen(edited));
    }

    [Fact]
    public void An_edit_that_ends_without_the_app_being_left_leaves_the_note_open()
    {
        var locks = new NoteLocks();
        var note = Guid.CreateVersion7();
        locks.Open(note);
        locks.Hold(note);

        locks.Release(note);

        Assert.True(locks.IsOpen(note));
        locks.Hold(Guid.CreateVersion7()); // holding a note that is not open opens nothing
        locks.CloseAll();
        Assert.False(locks.IsOpen(note));
    }

    [Fact]
    public async Task A_locked_template_does_not_start_new_daily_notes()
    {
        using var app = await TestApp.StartAsync();
        var template = await app.Notes.CreateAsync("# Template\n\n- [ ] plan the day");
        var preferences = new Preferences { DailyNotes = true, DailyNoteTemplate = template.Id.ToString("D") };
        var daily = new DailyNotes(app.Notes);
        Assert.NotNull(await daily.GetTemplateAsync(preferences));

        await app.Notes.PatchAsync(template.Id, new NotePatch(IsLocked: true));

        Assert.Null(await daily.GetTemplateAsync(preferences));
    }

    [Fact]
    public async Task A_first_PIN_for_locked_notes_leaves_the_app_lock_off()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);

        await lockService.SetPinAsync("2468");

        Assert.False((await lockService.GetAsync()).Enabled);
        Assert.True(await lockService.VerifyPinAsync("2468"));
        Assert.False(await lockService.VerifyPinAsync("1111"));
    }

    [Fact]
    public async Task The_PIN_is_in_use_while_the_app_lock_is_on_or_a_note_is_locked()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);
        var note = await app.Notes.CreateAsync("secret");
        Assert.False(await lockService.PinInUseAsync());

        await lockService.SetPinAsync("2468");
        Assert.False(await lockService.PinInUseAsync()); // a PIN that protects nothing yet

        await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true));
        Assert.True(await lockService.PinInUseAsync());

        await lockService.TurnOnAsync();
        await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: false));
        Assert.True(await lockService.PinInUseAsync());

        await lockService.DisableAsync();
        Assert.False(await lockService.PinInUseAsync()); // left behind by a lock since turned off
    }

    [Fact]
    public async Task A_PIN_in_use_cannot_be_chosen_again_without_the_current_one()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);
        var note = await app.Notes.CreateAsync("secret");
        await lockService.SetPinAsync("2468");
        await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => lockService.SetPinAsync("0000"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lockService.EnableAsync("0000"));

        Assert.False((await lockService.GetAsync()).Enabled);
        Assert.True(await lockService.VerifyPinAsync("2468"));
        Assert.False(await lockService.VerifyPinAsync("0000"));
    }

    [Fact]
    public async Task Turning_the_app_lock_on_and_off_keeps_the_PIN_locked_notes_use()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);
        var note = await app.Notes.CreateAsync("secret");
        await lockService.SetPinAsync("2468");
        await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true));

        await lockService.TurnOnAsync();
        Assert.True((await lockService.GetAsync()).Enabled);
        Assert.True(await lockService.VerifyPinAsync("2468"));

        await lockService.DisableAsync();
        Assert.True(await lockService.PinInUseAsync());
        Assert.True(await lockService.VerifyPinAsync("2468"));
    }

    [Fact]
    public async Task The_app_lock_cannot_be_turned_on_without_a_PIN()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);

        await Assert.ThrowsAsync<InvalidOperationException>(lockService.TurnOnAsync);
    }
}
