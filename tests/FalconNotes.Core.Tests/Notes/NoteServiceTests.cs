using System.Text;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Startup;
using FalconNotes.Core.Text;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Tests.Notes;

// The rules of the server's Notes/NotesApiTests.cs, at the service level.
public class NoteServiceTests
{
    private static readonly NoteKind[] Timeline = [NoteKind.Note];

    private static NoteQuery Feed(params NoteKind[] kinds) => new(NoteState.Feed, kinds.Length == 0 ? Timeline : kinds);

    [Fact]
    public async Task A_created_note_round_trips_and_is_encrypted_at_rest()
    {
        using var app = await TestApp.StartAsync();

        var note = await app.Notes.CreateAsync("Buy maple syrup #groceries #Shopping/Weekly");

        var read = await app.Notes.GetAsync(note.Id);
        Assert.NotNull(read);
        Assert.Equal("Buy maple syrup #groceries #Shopping/Weekly", read.Content);
        Assert.Equal(["groceries", "shopping/weekly"], read.Tags);
        Assert.Equal(NoteKind.Note, read.Kind);
        Assert.Equal(app.Clock.Now.UtcDateTime, read.CreatedAtUtc);
        Assert.Equal(read.CreatedAtUtc, read.UpdatedAtUtc);
        Assert.Equal(7, note.Id.Version);

        SqliteConnection.ClearAllPools();
        var bytes = await File.ReadAllBytesAsync(Path.Combine(app.Directories.DataDirectory, DatabaseStartup.DatabaseFileName), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("maple syrup", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_feed_pages_newest_first_without_gaps_or_duplicates()
    {
        using var app = await TestApp.StartAsync();
        var created = new List<(DateTime Time, Guid Id)>();
        for (var i = 0; i < 45; i++)
        {
            var note = await app.Notes.CreateAsync($"note {i}");
            created.Add((note.CreatedAtUtc, note.Id));
            if (i % 3 == 0)
            {
                app.Clock.Advance(TimeSpan.FromSeconds(1)); // some notes share a creation time: the ID breaks the tie
            }
        }

        var seen = new List<Guid>();
        NoteCursor? cursor = null;
        do
        {
            var page = await app.Notes.ListAsync(Feed(), cursor);
            Assert.True(page.Items.Count <= NoteService.PageSize);
            seen.AddRange(page.Items.Select(n => n.Id));
            cursor = page.Next;
        }
        while (cursor is not null);

        // Newest first; notes created at the same instant follow their IDs (docs/04, Lists).
        Assert.Equal(
            created.OrderByDescending(n => n.Time).ThenByDescending(n => n.Id.ToString("D"), StringComparer.Ordinal).Select(n => n.Id),
            seen);
    }

    [Fact]
    public async Task Pinned_notes_are_listed_separately_from_the_feed()
    {
        using var app = await TestApp.StartAsync();
        var plain = await app.Notes.CreateAsync("plain");
        var pinned = await app.Notes.CreateAsync("pinned", isPinned: true);

        Assert.Equal([plain.Id], (await app.Notes.ListAsync(Feed())).Items.Select(n => n.Id));
        Assert.Equal([pinned.Id], (await app.Notes.ListAsync(new NoteQuery(NoteState.Pinned, Timeline))).Items.Select(n => n.Id));

        await app.Notes.PatchAsync(pinned.Id, new NotePatch(IsPinned: false));
        Assert.Equal(2, (await app.Notes.ListAsync(Feed())).Items.Count);
    }

    [Fact]
    public async Task Archiving_hides_a_note_and_restoring_brings_it_back_without_changing_it()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("keep me", isPinned: true);
        app.Clock.Advance(TimeSpan.FromHours(1));

        var archived = await app.Notes.PatchAsync(note.Id, new NotePatch(IsArchived: true));

        Assert.True(archived!.IsArchived);
        Assert.True(archived.IsPinned); // the pinned state is kept
        Assert.Equal(note.UpdatedAtUtc, archived.UpdatedAtUtc); // state changes leave UpdatedAt alone
        Assert.Equal(note.Revision + 1, archived.Revision);
        Assert.Empty((await app.Notes.ListAsync(new NoteQuery(NoteState.Pinned, Timeline))).Items);
        Assert.Single((await app.Notes.ListAsync(new NoteQuery(NoteState.Archived, Timeline))).Items);

        var again = await app.Notes.PatchAsync(note.Id, new NotePatch(IsArchived: true));
        Assert.Equal(archived.ArchivedAtUtc, again!.ArchivedAtUtc); // archiving twice keeps the first time

        await app.Notes.PatchAsync(note.Id, new NotePatch(IsArchived: false));
        Assert.Single((await app.Notes.ListAsync(new NoteQuery(NoteState.Pinned, Timeline))).Items);
    }

    [Fact]
    public async Task Editing_updates_text_tags_and_the_edit_time()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("#old text");
        app.Clock.Advance(TimeSpan.FromMinutes(5));

        var edited = await app.Notes.UpdateAsync(note.Id, "#new text");

        Assert.Equal("#new text", edited!.Content);
        Assert.Equal(["new"], edited.Tags);
        Assert.Equal(note.CreatedAtUtc, edited.CreatedAtUtc);
        Assert.Equal(app.Clock.Now.UtcDateTime, edited.UpdatedAtUtc);
        Assert.Equal([new TagCount("new", 1)], await app.Notes.TagCountsAsync(Timeline)); // "old" is gone
        Assert.Null(await app.Notes.UpdateAsync(Guid.NewGuid(), "x"));
    }

    [Fact]
    public async Task The_tag_filter_includes_nested_tags()
    {
        using var app = await TestApp.StartAsync();
        var work = await app.Notes.CreateAsync("#work");
        var meetings = await app.Notes.CreateAsync("#work/meetings");
        await app.Notes.CreateAsync("#workshop");
        await app.Notes.CreateAsync("#home");

        var page = await app.Notes.ListAsync(new NoteQuery(NoteState.Active, Timeline, Tag: " #Work "));

        Assert.Equal([meetings.Id, work.Id], page.Items.Select(n => n.Id));
    }

    [Fact]
    public async Task Search_finds_text_and_file_names_case_insensitively()
    {
        using var app = await TestApp.StartAsync();
        var text = await app.Notes.CreateAsync("Pick up MAPLE syrup");
        var file = await app.AddFileAsync("Maple-trip.jpg");
        var withFile = await app.Notes.CreateAsync("photos", attachmentIds: [file.Id]);
        await app.Notes.CreateAsync("nothing here");

        var page = await app.Notes.ListAsync(new NoteQuery(NoteState.Active, Timeline, Search: "  maple "));

        Assert.Equal([withFile.Id, text.Id], page.Items.Select(n => n.Id));
    }

    [Fact]
    public async Task Search_results_page_with_cursors_and_stop_at_the_end()
    {
        using var app = await TestApp.StartAsync();
        for (var i = 0; i < 450; i++)
        {
            await app.Notes.CreateAsync(i % 10 == 0 ? $"match {i}" : $"other {i}");
            app.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        var query = new NoteQuery(NoteState.Active, Timeline, Search: "match");
        var first = await app.Notes.ListAsync(query, pageSize: 20);
        var second = await app.Notes.ListAsync(query, first.Next, pageSize: 20);
        var third = await app.Notes.ListAsync(query, second.Next, pageSize: 20);

        Assert.Equal(20, first.Items.Count);
        Assert.Equal(20, second.Items.Count);
        Assert.Equal(5, third.Items.Count);
        Assert.Null(third.Next);
        Assert.Equal("match 440", first.Items[0].Content);
        Assert.Equal("match 0", third.Items[^1].Content);
    }

    [Fact]
    public async Task A_cancelled_search_stops()
    {
        using var app = await TestApp.StartAsync();
        await app.Notes.CreateAsync("x");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            app.Notes.ListAsync(new NoteQuery(NoteState.Active, Timeline, Search: "x"), cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task Deleting_a_note_removes_it_its_files_and_unused_tags()
    {
        using var app = await TestApp.StartAsync();
        var file = await app.AddFileAsync();
        var note = await app.Notes.CreateAsync("#gone", attachmentIds: [file.Id]);

        Assert.True(await app.Notes.DeleteAsync(note.Id));

        Assert.Null(await app.Notes.GetAsync(note.Id));
        Assert.Null(await app.Attachments.GetAsync(file.Id));
        Assert.Empty(app.Store.EnumerateStorageKeys());
        Assert.Empty(await app.Notes.TagCountsAsync(Timeline));
        Assert.False(await app.Notes.DeleteAsync(note.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t")]
    public async Task Empty_notes_are_rejected_unless_they_have_a_file(string content)
    {
        using var app = await TestApp.StartAsync();

        var error = await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.CreateAsync(content));
        Assert.Equal("Write something or attach a file.", error.Message);

        var file = await app.AddFileAsync();
        var note = await app.Notes.CreateAsync(content, attachmentIds: [file.Id]);
        Assert.Single(note.Attachments);
    }

    [Fact]
    public async Task Overlong_notes_are_rejected()
    {
        using var app = await TestApp.StartAsync();

        await app.Notes.CreateAsync(new string('a', NoteService.MaxContentLength));
        var error = await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.CreateAsync(new string('a', NoteService.MaxContentLength + 1)));

        Assert.Equal("A note can be at most 100,000 characters long.", error.Message);
    }

    [Fact]
    public async Task The_day_filter_lists_notes_created_in_a_local_day()
    {
        using var app = await TestApp.StartAsync();
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        app.Clock.Now = new DateTimeOffset(2026, 9, 27, 21, 59, 0, TimeSpan.Zero); // 23:59 on the 27th in Paris
        await app.Notes.CreateAsync("before");
        app.Clock.Now = new DateTimeOffset(2026, 9, 27, 22, 0, 0, TimeSpan.Zero); // 00:00 on the 28th
        var inside = await app.Notes.CreateAsync("inside");
        app.Clock.Now = new DateTimeOffset(2026, 9, 28, 22, 0, 0, TimeSpan.Zero); // 00:00 on the 29th
        await app.Notes.CreateAsync("after");
        var (from, before) = DateFormats.DayRange(new DateOnly(2026, 9, 28), paris);

        var page = await app.Notes.ListAsync(new NoteQuery(NoteState.Active, Timeline, CreatedFromUtc: from, CreatedBeforeUtc: before));

        Assert.Equal([inside.Id], page.Items.Select(n => n.Id));
    }

    [Fact]
    public async Task Changes_raise_NotesChanged_after_they_commit()
    {
        using var app = await TestApp.StartAsync();
        var raised = 0;
        app.Feed.NotesChanged += () => raised++;

        var note = await app.Notes.CreateAsync("a");
        await app.Notes.UpdateAsync(note.Id, "b");
        await app.Notes.PatchAsync(note.Id, new NotePatch(IsPinned: true));
        await app.Notes.DeleteAsync(note.Id);
        await app.Notes.EmptyTrashAsync(); // nothing to delete: no event

        Assert.Equal(4, raised);
    }
}
