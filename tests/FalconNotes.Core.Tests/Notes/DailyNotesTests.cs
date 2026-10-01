using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;

namespace FalconNotes.Core.Tests.Notes;

// The rules of the server's Notes/DailyNotesTests.cs and web/lib/daily.ts.
public class DailyNotesTests
{
    private static readonly DateOnly Day = new(2026, 9, 28);

    [Fact]
    public async Task A_day_has_at_most_one_daily_note_which_is_found_by_date()
    {
        using var app = await TestApp.StartAsync();

        var note = await app.Notes.CreateAsync("# Monday\n\nfirst words", dailyDate: Day);

        Assert.Equal(note.Id, (await app.Notes.GetDailyAsync(Day))!.Id);
        Assert.Equal(Day, note.DailyDate);
        Assert.Null(await app.Notes.GetDailyAsync(Day.AddDays(1)));
        var error = await Assert.ThrowsAsync<DailyNoteExistsException>(() => app.Notes.CreateAsync("again", dailyDate: Day));
        Assert.Equal(Day, error.Date);
    }

    [Fact]
    public async Task Moving_or_deleting_a_daily_note_frees_its_day()
    {
        using var app = await TestApp.StartAsync();
        var moved = await app.Notes.CreateAsync("moved", dailyDate: Day);

        var quick = await app.Notes.PatchAsync(moved.Id, new NotePatch(Kind: NoteKind.Quick));
        Assert.Null(quick!.DailyDate);
        var deleted = await app.Notes.CreateAsync("deleted", dailyDate: Day);
        await app.Notes.DeleteAsync(deleted.Id);

        Assert.Equal(Day, (await app.Notes.CreateAsync("third", dailyDate: Day)).DailyDate);
        var back = await app.Notes.PatchAsync(moved.Id, new NotePatch(Kind: NoteKind.Note));
        Assert.Null(back!.DailyDate); // moved back, it is an ordinary note
    }

    [Fact]
    public async Task Only_timeline_notes_can_be_daily_notes()
    {
        using var app = await TestApp.StartAsync();

        var error = await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.CreateAsync("x", NoteKind.Todo, dailyDate: Day));

        Assert.Equal("Only timeline notes can be daily notes.", error.Message);
    }

    [Fact]
    public async Task The_first_words_become_the_day_s_note_titled_with_the_date()
    {
        using var app = await TestApp.StartAsync();

        var note = await new DailyNotes(app.Notes).SaveAsync(Day, "2026-09-28", "first words", []);

        Assert.Equal("# 2026-09-28\n\nfirst words", note.Content);
        Assert.Equal(Day, note.DailyDate);
    }

    [Fact]
    public async Task Words_for_a_day_that_has_its_note_already_are_added_to_it()
    {
        using var app = await TestApp.StartAsync();
        var first = await app.AddFileAsync("a.png");
        var existing = await app.Notes.CreateAsync("# 2026-09-28\n\nmorning  \n", attachmentIds: [first.Id], dailyDate: Day);
        var second = await app.AddFileAsync("b.png");

        var note = await new DailyNotes(app.Notes).SaveAsync(Day, "2026-09-28", "evening", [second.Id]);

        Assert.Equal(existing.Id, note.Id);
        Assert.Equal("# 2026-09-28\n\nmorning\n\nevening", note.Content);
        Assert.Equal([first.Id, second.Id], note.Attachments.Select(a => a.Id));
    }
}
