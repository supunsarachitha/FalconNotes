using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;

namespace FalconNotes.Core.Tests.Notes;

/// <summary>
/// New tests (docs/11): moods are this app's own, with no reference to port from (docs/04, Moods). Covers the mood
/// on a note, which notes can have one, and what leaves it as it is.
/// </summary>
public class MoodTests
{
    [Fact]
    public async Task A_note_has_no_mood_unless_one_is_chosen_when_it_is_written()
    {
        using var app = await TestApp.StartAsync();

        var plain = await app.Notes.CreateAsync("nothing special");
        var good = await app.Notes.CreateAsync("the sun came out", mood: NoteMood.Good);
        var quick = await app.Notes.CreateAsync("missed the bus", NoteKind.Quick, mood: NoteMood.Bad);

        Assert.Equal(NoteMood.None, plain.Mood);
        Assert.Equal(NoteMood.Good, good.Mood);
        Assert.Equal(NoteMood.Bad, quick.Mood);
        Assert.Equal(NoteMood.Good, (await app.Notes.ListAsync(new NoteQuery(NoteState.Feed, [NoteKind.Note]))).Items.Single(n => n.Id == good.Id).Mood);
    }

    [Fact]
    public async Task Changing_the_mood_alone_changes_the_note_but_not_when_it_was_last_written()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("a long day");
        app.Clock.Advance(TimeSpan.FromHours(1));

        var changed = await app.Notes.PatchAsync(note.Id, new NotePatch(Mood: NoteMood.Awful));

        Assert.Equal(NoteMood.Awful, changed!.Mood);
        Assert.Equal(note.UpdatedAtUtc, changed.UpdatedAtUtc);
        Assert.Equal(note.Revision + 1, changed.Revision);

        var cleared = await app.Notes.PatchAsync(note.Id, new NotePatch(Mood: NoteMood.None));

        Assert.Equal(NoteMood.None, cleared!.Mood);
    }

    [Fact]
    public async Task An_edit_saves_the_mood_with_the_text_and_keeps_it_when_none_is_given()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("first words", mood: NoteMood.Okay);

        var kept = await app.Notes.UpdateAsync(note.Id, "first words, and more");
        var changed = await app.Notes.UpdateAsync(note.Id, "better now", mood: NoteMood.Great);
        var cleared = await app.Notes.UpdateAsync(note.Id, "better now", mood: NoteMood.None);

        Assert.Equal(NoteMood.Okay, kept!.Mood);
        Assert.Equal(("better now", NoteMood.Great), (changed!.Content, changed.Mood));
        Assert.Equal(NoteMood.None, cleared!.Mood);
    }

    [Fact]
    public async Task Other_changes_leave_the_mood_as_it_is()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("a good walk", mood: NoteMood.Good);

        var pinned = await app.Notes.PatchAsync(note.Id, new NotePatch(IsPinned: true));
        var quick = await app.Notes.PatchAsync(note.Id, new NotePatch(Kind: NoteKind.Quick));
        var home = await app.Notes.PatchAsync(note.Id, new NotePatch(Kind: NoteKind.Note));
        var archived = await app.Notes.PatchAsync(note.Id, new NotePatch(IsArchived: true));
        var locked = await app.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true));
        var trashed = await app.Notes.PatchAsync(note.Id, new NotePatch(IsTrashed: true));

        Assert.All([pinned, quick, home, archived, locked, trashed], changed => Assert.Equal(NoteMood.Good, changed!.Mood));
    }

    [Theory]
    [InlineData(NoteKind.Todo)]
    [InlineData(NoteKind.Habit)]
    public async Task Only_notes_and_quick_notes_can_have_a_mood(NoteKind kind)
    {
        using var app = await TestApp.StartAsync();
        var existing = await app.Notes.CreateAsync("# Stretch", kind);

        var created = await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.CreateAsync("# Read", kind, mood: NoteMood.Good));
        var patched = await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.PatchAsync(existing.Id, new NotePatch(Mood: NoteMood.Good)));
        var edited = await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.UpdateAsync(existing.Id, "# Stretch more", mood: NoteMood.Good));

        Assert.All([created, patched, edited], error => Assert.Equal("Only notes and quick notes can have a mood.", error.Message));
        Assert.Equal(("# Stretch", NoteMood.None), ((await app.Notes.GetAsync(existing.Id))!.Content, (await app.Notes.GetAsync(existing.Id))!.Mood));

        // Taking a mood off is never refused, and neither is leaving it alone.
        Assert.Equal(NoteMood.None, (await app.Notes.PatchAsync(existing.Id, new NotePatch(Mood: NoteMood.None)))!.Mood);
    }

    [Fact]
    public async Task A_note_that_becomes_a_todo_list_gives_up_its_mood()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("- [ ] oats", mood: NoteMood.Great);

        var list = await app.Notes.PatchAsync(note.Id, new NotePatch(Kind: NoteKind.Todo));
        var back = await app.Notes.PatchAsync(note.Id, new NotePatch(Kind: NoteKind.Note));

        Assert.Equal(NoteMood.None, list!.Mood);
        Assert.Equal(NoteMood.None, back!.Mood);
    }

    [Fact]
    public async Task The_daily_note_takes_the_mood_chosen_with_its_first_words_and_keeps_it_when_more_are_added_without_one()
    {
        using var app = await TestApp.StartAsync();
        var daily = new DailyNotes(app.Notes);
        var day = new DateOnly(2026, 10, 13);

        var started = await daily.SaveAsync(day, "2026-10-13", "slept well", [], NoteMood.Good);
        var added = await daily.SaveAsync(day, "2026-10-13", "long meeting", []); // the day's note appeared meanwhile
        var changed = await daily.SaveAsync(day, "2026-10-13", "it rained", [], NoteMood.Bad);

        Assert.Equal(NoteMood.Good, started.Mood);
        Assert.Equal((started.Id, NoteMood.Good), (added.Id, added.Mood));
        Assert.Equal(NoteMood.Bad, changed.Mood);
    }

    [Fact]
    public void Moods_are_written_by_name_and_read_back_in_any_case()
    {
        Assert.Equal(
            ["awful", "bad", "okay", "good", "great"],
            NoteMoods.All.Select(NoteMoods.Name));
        Assert.Null(NoteMoods.Name(NoteMood.None));
        Assert.All(NoteMoods.All, mood => Assert.Equal(mood, NoteMoods.Parse(NoteMoods.Name(mood))));
        Assert.Equal(NoteMood.Great, NoteMoods.Parse(" Great "));
        Assert.All(new[] { null, "", "none", "ecstatic", "5", "😄" }, name => Assert.Equal(NoteMood.None, NoteMoods.Parse(name)));
    }

    [Fact]
    public void The_stored_numbers_never_change_and_one_this_version_does_not_know_reads_as_none()
    {
        Assert.Equal(
            [(NoteMood.None, 0), (NoteMood.Awful, 1), (NoteMood.Bad, 2), (NoteMood.Okay, 3), (NoteMood.Good, 4), (NoteMood.Great, 5)],
            Enum.GetValues<NoteMood>().Select(mood => (mood, (int)mood)));
        Assert.All(NoteMoods.All, mood => Assert.Equal(mood, NoteMoods.FromStored((int)mood)));
        Assert.All(new long[] { 0, -1, 6, 99 }, stored => Assert.Equal(NoteMood.None, NoteMoods.FromStored(stored)));
    }
}
