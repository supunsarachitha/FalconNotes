using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Notes;

// The rules of the server's Notes/NoteKindsTests.cs.
public class NoteKindsTests
{
    [Fact]
    public async Task Todo_lists_and_quick_notes_stay_out_of_the_timeline()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("note");
        var todo = await app.Notes.CreateAsync("# List\n\n- [ ] item", NoteKind.Todo);
        var quick = await app.Notes.CreateAsync("quick", NoteKind.Quick);

        Assert.Equal([note.Id], (await app.Notes.ListAsync(new NoteQuery(NoteState.Feed, [NoteKind.Note]))).Items.Select(n => n.Id));
        Assert.Equal([todo.Id], (await app.Notes.ListAsync(new NoteQuery(NoteState.Feed, [NoteKind.Todo]))).Items.Select(n => n.Id));
        Assert.Equal([quick.Id], (await app.Notes.ListAsync(new NoteQuery(NoteState.Feed, [NoteKind.Quick]))).Items.Select(n => n.Id));
    }

    [Fact]
    public async Task Tags_searches_and_the_archive_cover_the_requested_kinds()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("#shared note");
        var todo = await app.Notes.CreateAsync("# List\n\n- [ ] #shared item", NoteKind.Todo);
        foreach (var id in new[] { note.Id, todo.Id })
        {
            await app.Notes.PatchAsync(id, new NotePatch(IsArchived: true));
        }

        await app.Notes.CreateAsync("#shared active", NoteKind.Quick);

        Assert.Single((await app.Notes.ListAsync(new NoteQuery(NoteState.Archived, [NoteKind.Note]))).Items);
        Assert.Equal(2, (await app.Notes.ListAsync(new NoteQuery(NoteState.Archived, [NoteKind.Note, NoteKind.Todo]))).Items.Count);
        Assert.Equal([new TagCount("shared", 1)], await app.Notes.TagCountsAsync([NoteKind.Note, NoteKind.Todo, NoteKind.Quick]));
        Assert.Empty(await app.Notes.TagCountsAsync([NoteKind.Note, NoteKind.Todo])); // the archived ones do not count
        Assert.Single((await app.Notes.ListAsync(new NoteQuery(NoteState.Active, [NoteKind.Quick], Search: "shared"))).Items);
        Assert.Empty((await app.Notes.ListAsync(new NoteQuery(NoteState.Active, [NoteKind.Note], Search: "shared"))).Items);
    }

    [Fact]
    public async Task A_quick_note_moves_to_the_timeline_and_back_keeping_its_times()
    {
        using var app = await TestApp.StartAsync();
        var quick = await app.Notes.CreateAsync("quick", NoteKind.Quick);
        app.Clock.Advance(TimeSpan.FromHours(1));

        var moved = await app.Notes.PatchAsync(quick.Id, new NotePatch(Kind: NoteKind.Note));

        Assert.Equal(NoteKind.Note, moved!.Kind);
        Assert.Equal(quick.UpdatedAtUtc, moved.UpdatedAtUtc);
        Assert.Equal(NoteKind.Quick, (await app.Notes.PatchAsync(quick.Id, new NotePatch(Kind: NoteKind.Quick)))!.Kind);
    }

    [Fact]
    public async Task Habits_are_listed_only_when_asked_for_and_leave_tag_counts_and_the_calendar_alone()
    {
        using var app = await TestApp.StartAsync();
        var habit = await app.Notes.CreateAsync("# Read #books\n\n- 2026-09-27", NoteKind.Habit);
        app.Clock.Advance(TimeSpan.FromSeconds(1));
        var archived = await app.Notes.CreateAsync("# Old habit", NoteKind.Habit);
        await app.Notes.PatchAsync(archived.Id, new NotePatch(IsArchived: true));
        var trashed = await app.Notes.CreateAsync("# Gone", NoteKind.Habit);
        await app.Notes.PatchAsync(trashed.Id, new NotePatch(IsTrashed: true));
        IReadOnlyList<NoteKind> enabled = [NoteKind.Note, NoteKind.Todo, NoteKind.Quick];

        Assert.Empty((await app.Notes.ListAsync(new NoteQuery(NoteState.Active, enabled))).Items);
        Assert.Empty(await app.Notes.TagCountsAsync(enabled));
        Assert.Empty(await app.Notes.CalendarAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), enabled, TimeZoneInfo.Utc));
        Assert.Equal([habit.Id, archived.Id], (await app.Notes.ListHabitsAsync()).Select(n => n.Id)); // oldest first, archived too
    }

    [Theory]
    [InlineData(NoteKind.Note, NoteKind.Habit)]
    [InlineData(NoteKind.Habit, NoteKind.Quick)]
    public async Task Habits_never_change_kind_and_notes_never_become_habits(NoteKind from, NoteKind to)
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("# x", from);

        var error = await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.PatchAsync(note.Id, new NotePatch(Kind: to)));

        Assert.Equal("Habits cannot become other kinds of notes, and notes cannot become habits.", error.Message);
        Assert.Equal(from, (await app.Notes.GetAsync(note.Id))!.Kind);
    }
}
