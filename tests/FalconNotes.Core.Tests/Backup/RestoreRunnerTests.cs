using FalconNotes.Core.Attachments;
using FalconNotes.Core.Backup.Restore;
using FalconNotes.Core.Domain;

namespace FalconNotes.Core.Tests.Backup;

// The rules of web/import/importer.ts ("running a restore") and the server's Notes/ImportTests.cs.
public class RestoreRunnerTests
{
    private static RestoreItem Item(string? id, int files = 0, string content = "text", string created = "2025-01-01T00:00:00Z",
        string? updated = null, NoteKind kind = NoteKind.Note, string? daily = null, bool archived = false, string[]? labels = null) =>
        new($"{id ?? "new"}.md", id is null ? null : Guid.Parse(id), content,
            DateTimeOffset.Parse(created, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(updated ?? created, System.Globalization.CultureInfo.InvariantCulture),
            false, archived, kind, daily, labels ?? [],
            Enumerable.Range(0, files).Select(i => new RestoreAttachment($"f{i}.png", "image/png", 1, () => new MemoryStream([(byte)i]))).ToList(), []);

    [Fact]
    public async Task Skips_notes_already_here_stores_files_first_and_reports_failures()
    {
        using var app = await TestApp.StartAsync();
        var runner = RestoreConformanceTests.NewRunner(app);
        await runner.RunAsync([Item("0192f3a2-0000-7000-8000-000000000001")]);
        var updates = new List<int>();

        var result = await runner.RunAsync(
            [Item("0192f3a2-0000-7000-8000-000000000001", 1), Item("0192f3a2-0000-7000-8000-000000000002", 2), Item(null, 0, content: " "), Item(null)],
            new SyncProgress(p => updates.Add(p.Done)));

        Assert.Equal((4, 4, 2, 1, 2), (result.Total, result.Done, result.Restored, result.Skipped, result.Files));
        Assert.Equal([("new.md", "Write something or attach a file.")], result.Failed.Select(f => (f.Source, f.Reason)));
        Assert.Equal(2, (await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000002")))!.Attachments.Count);
        Assert.Equal(2, app.Store.EnumerateStorageKeys().Count()); // the failed note's file was removed again
        Assert.Equal([0, 1, 2, 3, 4, 4], updates);
        Assert.Equal("Restored 2 notes and 2 files. 1 note was already here.", RestoreRunner.Summary(result));
    }

    // Port of "reuses labels of the same name, creates the others in their exported colour, and reports the ones it
    // cannot" in import.test.ts. There a label fails at the account's limit; here also for a name that is too long.
    [Fact]
    public async Task Reuses_labels_of_the_same_name_creates_the_others_in_their_colour_and_reports_the_ones_it_cannot()
    {
        using var app = await TestApp.StartAsync();
        var runner = RestoreConformanceTests.NewRunner(app);
        var work = await app.Labels.CreateAsync("Work", LabelColor.Blue);
        await runner.RunAsync([Item("0192f3a2-0000-7000-8000-000000000001")]);
        var tooLong = new string('x', 41);

        var result = await runner.RunAsync(
            [
                Item("0192f3a2-0000-7000-8000-000000000001", labels: ["Skipped"]), // this note is here already: its label is not needed
                Item("0192f3a2-0000-7000-8000-000000000002", labels: [" work ", "Trip", tooLong, " "]),
                Item("0192f3a2-0000-7000-8000-000000000003", labels: ["trip", "Plain"]),
            ],
            labelColors: new Dictionary<string, LabelColor> { ["trip"] = LabelColor.Teal });

        var labels = (await app.Labels.ListAsync([NoteKind.Note])).Select(l => l.Label).ToList();
        Assert.Equal(
            [("Plain", LabelColor.Amber), ("Trip", LabelColor.Teal), ("Work", LabelColor.Blue)], // "Plain": the third label, so the third colour in turn
            labels.Select(l => (l.Name, l.Color)).OrderBy(l => l.Name, StringComparer.Ordinal));
        Guid Id(string name) => labels.Single(l => l.Name == name).Id;
        Assert.Equal(new[] { work.Id, Id("Trip") }.Order(), (await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000002")))!.LabelIds.Order());
        Assert.Equal(new[] { Id("Trip"), Id("Plain") }.Order(), (await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000003")))!.LabelIds.Order());
        Assert.Empty((await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000001")))!.LabelIds);
        Assert.Equal((2, 1, 2), (result.Restored, result.Skipped, result.Labels));
        Assert.Equal([($"Label “{tooLong}”", "A label's name is 1 to 40 characters long. Notes are restored without it.")], result.Failed);
        Assert.Equal("Restored 2 notes and 0 files. Added 2 labels. 1 note was already here.", RestoreRunner.Summary(result));
    }

    [Fact]
    public async Task A_note_gets_at_most_twenty_labels_and_labels_past_the_limit_of_a_hundred_are_reported()
    {
        using var app = await TestApp.StartAsync();
        for (var i = 0; i < 99; i++)
        {
            await app.Labels.CreateAsync($"Here {i}");
        }

        var result = await RestoreConformanceTests.NewRunner(app).RunAsync(
        [
            Item("0192f3a2-0000-7000-8000-000000000001", labels: Enumerable.Range(0, 25).Select(i => $"Here {i}").ToArray()),
            Item("0192f3a2-0000-7000-8000-000000000002", labels: ["New", "One too many"]),
        ]);

        Assert.Equal(20, (await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000001")))!.LabelIds.Count);
        Assert.Single((await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000002")))!.LabelIds);
        Assert.Equal((2, 1), (result.Restored, result.Labels));
        Assert.Equal([("Label “One too many”", "You can have at most 100 labels. Notes are restored without it.")], result.Failed);
    }

    [Fact]
    public async Task Edit_times_keep_their_order_and_never_lie_in_the_future()
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        await RestoreConformanceTests.NewRunner(app).RunAsync(
        [
            Item("0192f3a2-0000-7000-8000-000000000001", created: "2025-01-02T00:00:00Z", updated: "2025-01-01T00:00:00Z"),
            Item("0192f3a2-0000-7000-8000-000000000002", created: "2026-09-28T13:00:00Z", updated: "2027-01-01T00:00:00Z"),
            Item("0192f3a2-0000-7000-8000-000000000003", created: "2026-09-30T00:00:00Z"),
        ]);

        var first = (await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000001")))!;
        var second = (await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000002")))!;
        Assert.Equal(first.CreatedAtUtc, first.UpdatedAtUtc); // not before it was created
        // Created an hour ahead (within the day's tolerance for clocks): the edit time is no later than now, and no
        // earlier than the creation, which wins, as on the server.
        Assert.Equal(second.CreatedAtUtc, second.UpdatedAtUtc);
        Assert.Null(await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000003"))); // created over a day ahead
    }

    [Fact]
    public async Task A_future_note_is_refused_with_the_reason()
    {
        using var app = await TestApp.StartAsync();

        var result = await RestoreConformanceTests.NewRunner(app).RunAsync([Item(null, created: "2030-01-01T00:00:00Z")]);

        Assert.Equal("A note cannot have been created in the future.", result.Failed.Single().Reason);
    }

    [Fact]
    public async Task A_daily_note_keeps_its_day_unless_the_day_is_taken_and_archived_notes_are_archived()
    {
        using var app = await TestApp.StartAsync();
        await app.Notes.CreateAsync("today", dailyDate: new DateOnly(2025, 1, 1));

        await RestoreConformanceTests.NewRunner(app).RunAsync(
        [
            Item("0192f3a2-0000-7000-8000-000000000001", daily: "2025-01-01"),
            Item("0192f3a2-0000-7000-8000-000000000002", daily: "2025-01-02", archived: true),
            Item("0192f3a2-0000-7000-8000-000000000003", daily: "2025-01-03", kind: NoteKind.Quick),
        ]);

        Assert.Null((await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000001")))!.DailyDate);
        var archived = (await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000002")))!;
        Assert.Equal(new DateOnly(2025, 1, 2), archived.DailyDate);
        Assert.Equal(archived.UpdatedAtUtc, archived.ArchivedAtUtc);
        Assert.Null((await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000003")))!.DailyDate);
    }

    [Fact]
    public async Task Running_out_of_space_stops_the_restore_keeping_what_was_restored()
    {
        using var app = await TestApp.StartAsync();
        var full = new RestoreAttachment("big.png", "image/png", 1, () => new FullDisk());
        var items = new List<RestoreItem>
        {
            Item("0192f3a2-0000-7000-8000-000000000001", 1),
            Item("0192f3a2-0000-7000-8000-000000000002") with { Attachments = [full] },
            Item("0192f3a2-0000-7000-8000-000000000003"),
        };

        var result = await RestoreConformanceTests.NewRunner(app).RunAsync(items);

        Assert.Equal("Not enough space on this device.", result.Stopped);
        Assert.Equal((1, 1, 1), (result.Done, result.Restored, result.Files));
        Assert.NotNull(await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000001")));
        Assert.Null(await app.Notes.GetAsync(Guid.Parse("0192f3a2-0000-7000-8000-000000000003")));
    }

    [Fact]
    public async Task A_cancelled_restore_keeps_the_notes_restored_so_far()
    {
        using var app = await TestApp.StartAsync();
        using var cancel = new CancellationTokenSource();
        var cancelling = new RestoreAttachment("x.png", "image/png", 1, () =>
        {
            cancel.Cancel(); // the user cancels while the third note's file is being stored
            return new MemoryStream([1]);
        });
        var items = Enumerable.Range(1, 5).Select(i => Item($"0192f3a2-0000-7000-8000-00000000000{i}")).ToList();
        items[2] = items[2] with { Attachments = [cancelling] };

        var result = await RestoreConformanceTests.NewRunner(app).RunAsync(items, cancellationToken: cancel.Token);

        Assert.Equal(RestoreRunner.Cancelled, result.Stopped);
        Assert.Equal(2, result.Restored);
        Assert.Equal(2, (await app.Notes.ListAsync(new Core.Notes.NoteQuery(NoteState.Active, [NoteKind.Note]))).Items.Count);
        Assert.Empty(app.Store.EnumerateStorageKeys()); // the half-stored file is gone
    }

    /// <summary>A file whose storing fails as a full disk does (ENOSPC).</summary>
    private sealed class FullDisk : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("No space left on device", 28);

        public override int Read(Span<byte> buffer) => throw new IOException("No space left on device", 28);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("No space left on device", 28);
    }

    private sealed class SyncProgress(Action<RestoreProgress> report) : IProgress<RestoreProgress>
    {
        public void Report(RestoreProgress value) => report(value);
    }
}
