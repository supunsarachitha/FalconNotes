using FalconNotes.Core.Domain;
using FalconNotes.Core.Tests;
using FalconNotes.UI.State;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New tests (docs/11): the web app has no dedicated test for <c>lib/noteEditor.ts</c> (it is exercised through
/// TodoCard and HabitRow, ported in a later phase), so this covers <see cref="NoteEditor{T}"/> directly: optimistic
/// commits, sequential background saves, and that a stale reload never undoes an edit on its way to the database.
/// </summary>
public class NoteEditorTests
{
    private static readonly Func<string, string> Same = content => content;

    [Fact]
    public async Task Starts_with_the_notes_parsed_content()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("one");

        var editor = new NoteEditor<string>(app.Notes, note, Same, Same, _ => { });

        Assert.Equal("one", editor.Value);
    }

    [Fact]
    public async Task Commit_shows_the_change_at_once_and_saves_it_in_the_background()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("one");
        var editor = new NoteEditor<string>(app.Notes, note, Same, Same, _ => { });

        editor.Commit("two");

        Assert.Equal("two", editor.Value);
        await WaitUntilAsync(async () => (await app.Notes.GetAsync(note.Id))!.Content == "two");
    }

    [Fact]
    public async Task Two_commits_save_one_after_another_and_both_land()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("one");
        var editor = new NoteEditor<string>(app.Notes, note, Same, Same, _ => { });

        editor.Commit("two");
        editor.Commit("three");

        Assert.Equal("three", editor.Value);
        await WaitUntilAsync(async () => (await app.Notes.GetAsync(note.Id))!.Content == "three");
    }

    [Fact]
    public async Task A_failed_save_calls_onError_and_does_not_touch_the_value()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("one");
        Exception? error = null;
        var editor = new NoteEditor<string>(app.Notes, note, Same, Same, e => error = e);

        editor.Commit(""); // empty content with no attachments: NoteService rejects it

        await WaitUntilAsync(() => Task.FromResult(error is not null));
        Assert.IsType<UserFacingException>(error);
        Assert.Equal("", editor.Value); // shown at once regardless; only the save failed
    }

    [Fact]
    public async Task Reload_is_ignored_while_a_save_is_still_in_flight()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("one");
        var editor = new NoteEditor<string>(app.Notes, note, Same, Same, _ => { });

        editor.Commit("two");
        editor.Reload(note with { Content = "stale from another read", UpdatedAtUtc = note.UpdatedAtUtc.AddSeconds(5) });

        Assert.Equal("two", editor.Value);
        await WaitUntilAsync(async () => (await app.Notes.GetAsync(note.Id))!.Content == "two");
    }

    [Fact]
    public async Task Reload_is_ignored_when_the_note_is_not_newer_than_our_last_save()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("one");
        var editor = new NoteEditor<string>(app.Notes, note, Same, Same, _ => { });
        editor.Commit("two");
        await WaitUntilAsync(async () => (await app.Notes.GetAsync(note.Id))!.Content == "two");

        editor.Reload(note); // the same stale read as before the commit: older than our last save

        Assert.Equal("two", editor.Value);
    }

    [Fact]
    public async Task Reload_applies_a_newer_note_read_elsewhere()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("one");
        var editor = new NoteEditor<string>(app.Notes, note, Same, Same, _ => { });
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        var fromAnotherDevice = (await app.Notes.UpdateAsync(note.Id, "changed elsewhere"))!;

        editor.Reload(fromAnotherDevice);

        Assert.Equal("changed elsewhere", editor.Value);
    }

    [Fact]
    public async Task Commit_after_a_reload_saves_with_the_reloaded_attachments()
    {
        using var app = await TestApp.StartAsync();
        var file = await app.AddFileAsync();
        var note = await app.Notes.CreateAsync("one", attachmentIds: [file.Id]);
        var editor = new NoteEditor<string>(app.Notes, note, Same, Same, _ => { });
        var removed = (await app.Notes.UpdateAsync(note.Id, "one", attachmentIds: []))!; // the file removed elsewhere
        editor.Reload(removed);

        editor.Commit("two");

        await WaitUntilAsync(async () => (await app.Notes.GetAsync(note.Id))!.Content == "two");
        Assert.Empty((await app.Notes.GetAsync(note.Id))!.Attachments);
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
