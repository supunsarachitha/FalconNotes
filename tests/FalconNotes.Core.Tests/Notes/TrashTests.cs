using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;

namespace FalconNotes.Core.Tests.Notes;

// The rules of the server's Notes/TrashTests.cs.
public class TrashTests
{
    private static readonly NoteKind[] AllKinds = [.. NoteKinds.All];

    [Fact]
    public async Task A_note_in_the_trash_is_listed_only_there()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("#tagged", isPinned: true);
        var label = await app.Labels.CreateAsync("Work");
        await app.Notes.PatchAsync(note.Id, new NotePatch(LabelIds: [label.Id]));

        await app.Notes.PatchAsync(note.Id, new NotePatch(IsTrashed: true));

        foreach (var state in new[] { NoteState.Feed, NoteState.Pinned, NoteState.Active, NoteState.Archived })
        {
            Assert.Empty((await app.Notes.ListAsync(new NoteQuery(state, AllKinds))).Items);
        }

        Assert.Empty((await app.Notes.ListAsync(new NoteQuery(NoteState.Active, AllKinds, Search: "tagged"))).Items);
        Assert.Empty(await app.Notes.TagCountsAsync(AllKinds));
        Assert.Equal(0, (await app.Labels.ListAsync(AllKinds)).Single().NoteCount);
        Assert.Empty(await app.Notes.CalendarAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), AllKinds, TimeZoneInfo.Utc));
        Assert.Equal([note.Id], (await app.Notes.ListAsync(new NoteQuery(NoteState.Trash, AllKinds))).Items.Select(n => n.Id));
    }

    [Fact]
    public async Task Restoring_puts_a_note_back_where_it_was()
    {
        using var app = await TestApp.StartAsync();
        var archived = await app.Notes.CreateAsync("archived");
        await app.Notes.PatchAsync(archived.Id, new NotePatch(IsArchived: true));
        var pinned = await app.Notes.CreateAsync("pinned", isPinned: true);
        await app.Notes.PatchAsync(archived.Id, new NotePatch(IsTrashed: true));
        await app.Notes.PatchAsync(pinned.Id, new NotePatch(IsTrashed: true));

        await app.Notes.PatchAsync(archived.Id, new NotePatch(IsTrashed: false));
        await app.Notes.PatchAsync(pinned.Id, new NotePatch(IsTrashed: false));

        Assert.Equal([archived.Id], (await app.Notes.ListAsync(new NoteQuery(NoteState.Archived, AllKinds))).Items.Select(n => n.Id));
        Assert.Equal([pinned.Id], (await app.Notes.ListAsync(new NoteQuery(NoteState.Pinned, AllKinds))).Items.Select(n => n.Id));
    }

    [Fact]
    public async Task The_trash_lists_the_most_recently_deleted_first_one_page_at_a_time()
    {
        using var app = await TestApp.StartAsync();
        var notes = new List<Note>();
        for (var i = 0; i < 25; i++)
        {
            notes.Add(await app.Notes.CreateAsync($"note {i}"));
        }

        foreach (var note in notes) // deleted oldest first
        {
            app.Clock.Advance(TimeSpan.FromMinutes(1));
            await app.Notes.PatchAsync(note.Id, new NotePatch(IsTrashed: true));
        }

        var first = await app.Notes.ListAsync(new NoteQuery(NoteState.Trash, AllKinds));
        var second = await app.Notes.ListAsync(new NoteQuery(NoteState.Trash, AllKinds), first.Next);

        Assert.Equal(notes.AsEnumerable().Reverse().Select(n => n.Id), first.Items.Concat(second.Items).Select(n => n.Id));
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task A_daily_note_in_the_trash_gives_up_its_day()
    {
        using var app = await TestApp.StartAsync();
        var day = new DateOnly(2026, 9, 28);
        var daily = await app.Notes.CreateAsync("# Monday\n\nfirst", dailyDate: day);

        var trashed = await app.Notes.PatchAsync(daily.Id, new NotePatch(IsTrashed: true));

        Assert.Null(trashed!.DailyDate);
        Assert.Null(await app.Notes.GetDailyAsync(day));
        var again = await app.Notes.CreateAsync("second", dailyDate: day);
        Assert.Equal(again.Id, (await app.Notes.GetDailyAsync(day))!.Id);
        var restored = await app.Notes.PatchAsync(daily.Id, new NotePatch(IsTrashed: false));
        Assert.Null(restored!.DailyDate); // restored, it is an ordinary note
    }

    [Fact]
    public async Task Emptying_the_trash_deletes_its_notes_and_their_files_for_good()
    {
        using var app = await TestApp.StartAsync();
        var kept = await app.Notes.CreateAsync("kept");
        var file = await app.AddFileAsync();
        var withFile = await app.Notes.CreateAsync("with a file", attachmentIds: [file.Id]);
        var plain = await app.Notes.CreateAsync("plain");
        await app.Notes.PatchAsync(withFile.Id, new NotePatch(IsTrashed: true));
        await app.Notes.PatchAsync(plain.Id, new NotePatch(IsTrashed: true));

        Assert.Equal(new DeletedCount(2, 1), await app.Notes.EmptyTrashAsync());

        Assert.Empty((await app.Notes.ListAsync(new NoteQuery(NoteState.Trash, AllKinds))).Items);
        Assert.Empty(app.Store.EnumerateStorageKeys());
        Assert.NotNull(await app.Notes.GetAsync(kept.Id));
    }

    [Fact]
    public async Task Notes_in_the_trash_for_over_30_days_are_deleted_for_good()
    {
        using var app = await TestApp.StartAsync();
        var old = await app.Notes.CreateAsync("old");
        await app.Notes.PatchAsync(old.Id, new NotePatch(IsTrashed: true));
        app.Clock.Advance(TimeSpan.FromDays(20));
        var recent = await app.Notes.CreateAsync("recent");
        await app.Notes.PatchAsync(recent.Id, new NotePatch(IsTrashed: true));
        app.Clock.Advance(TimeSpan.FromDays(10) + TimeSpan.FromMinutes(1));

        Assert.Equal(new DeletedCount(1, 0), await app.Notes.PurgeExpiredTrashAsync());

        Assert.Null(await app.Notes.GetAsync(old.Id));
        Assert.NotNull(await app.Notes.GetAsync(recent.Id));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 30)]       // 29 days and 23 hours left: rounds up
    [InlineData(29 * 24, 1)]
    [InlineData(30 * 24 - 1, 1)]
    [InlineData(30 * 24, 0)]  // deleted for good today
    [InlineData(40 * 24, 0)]
    public void Days_left_in_the_trash_round_up_and_never_go_below_zero(int hoursInTrash, int daysLeft)
    {
        var trashed = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(daysLeft, NoteService.DaysLeftInTrash(trashed, trashed.AddHours(hoursInTrash)));
    }
}
