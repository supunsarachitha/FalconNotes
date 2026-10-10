using System.Diagnostics;
using System.Globalization;
using FalconNotes.Core.Backup.Restore;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Tests.Performance;

/// <summary>
/// The budgets of docs/11 (Performance budgets) on a 50,000-note database. Run with <c>FALCON_PERF=1</c> (the CI Mac
/// does): they take a while to set up and depend on the machine. Times are medians of ten runs after a warm-up;
/// <c>FALCON_PERF_REPORT=path</c> also writes them to a file. <c>FALCON_PERF_SLACK=1.5</c> multiplies the budgets: the CI
/// Mac is a shared virtual machine whose speed varies between runs.
/// </summary>
[Trait("Category", "Performance")]
public sealed class PerformanceBudgetTests(ITestOutputHelper output)
{
    private const int NoteCount = 50_000;
    private static readonly NoteKind[] Enabled = [NoteKind.Note, NoteKind.Todo, NoteKind.Quick];

    [Fact]
    public async Task Every_list_count_filter_and_search_stays_within_its_budget()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FALCON_PERF") == "1", "Set FALCON_PERF=1 to run the performance budgets.");
        using var app = await TestApp.StartAsync();
        var fill = Stopwatch.StartNew();
        await LargeDatabase.FillAsync(app.Storage.Database, NoteCount, app.Clock.Now.UtcDateTime);
        output.WriteLine($"Generated {NoteCount:N0} notes in {fill.Elapsed.TotalSeconds:F1} s.");
        var feed = new NoteQuery(NoteState.Feed, [NoteKind.Note]);
        var middle = (await app.Notes.ListAsync(feed, pageSize: 1000)).Next;
        var labels = await app.Labels.ListAsync(Enabled);
        var today = DateOnly.FromDateTime(app.Clock.Now.UtcDateTime);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var slack = double.TryParse(Environment.GetEnvironmentVariable("FALCON_PERF_SLACK"), NumberStyles.Float, CultureInfo.InvariantCulture, out var factor)
            ? factor
            : 1.0;
        var failures = new List<string>();
        var report = new List<string> { $"{NoteCount:N0} notes, generated in {fill.Elapsed.TotalSeconds:F1} s" };
        async Task Measure(string name, double budgetMs, Func<Task> operation)
        {
            await operation();
            var times = new List<double>();
            for (var i = 0; i < 10; i++)
            {
                var watch = Stopwatch.StartNew();
                await operation();
                times.Add(watch.Elapsed.TotalMilliseconds);
            }

            var median = times.Order().ElementAt(times.Count / 2);
            output.WriteLine($"{name,-40} {median,8:F2} ms   (budget {budgetMs} ms)");
            report.Add($"{name,-40} {median,8:F2} ms   (budget {budgetMs} ms)");
            if (median > budgetMs * slack)
            {
                failures.Add($"{name}: {median:F1} ms > {budgetMs} ms");
            }
        }

        await Measure("Open database + first feed page", 20, async () =>
        {
            SqliteConnection.ClearAllPools();
            await app.Notes.ListAsync(feed);
        });
        await Measure("Feed page after 1,000 notes", 5, () => app.Notes.ListAsync(feed, middle));
        await Measure("Pinned list", 5, () => app.Notes.ListAsync(new NoteQuery(NoteState.Pinned, [NoteKind.Note])));
        await Measure("Habits page", 5, () => app.Notes.ListHabitsAsync());
        await Measure("Calendar month", 5, () => app.Notes.CalendarAsync(monthStart, monthStart.AddMonths(1).AddDays(-1), Enabled, TimeZoneInfo.Utc));
        await Measure("Tag counts", 80, () => app.Notes.TagCountsAsync(Enabled));
        await Measure("Label counts", 80, () => app.Labels.ListAsync(Enabled));
        await Measure("Tag filter #work (nested): first page", 100, () => app.Notes.ListAsync(new NoteQuery(NoteState.Active, Enabled, Tag: "work")));
        await Measure("Label filter: first page", 100, () => app.Notes.ListAsync(new NoteQuery(NoteState.Active, Enabled, Label: labels[0].Label.Id)));
        await Measure("Search with no match", 250, () => app.Notes.ListAsync(new NoteQuery(NoteState.Active, Enabled, Search: "no such words anywhere")));

        Note? posted = null;
        await Measure("Post a note", 5, async () => posted = await app.Notes.CreateAsync("A new note #work"));
        await Measure("Edit a note", 5, () => app.Notes.UpdateAsync(posted!.Id, "Edited #home"));
        await Measure("Pin a note", 5, () => app.Notes.PatchAsync(posted!.Id, new NotePatch(IsPinned: true)));

        // Last, because it changes what the lists above read: with labels on and one label hiding its notes, Home's
        // lists check every note's labels (docs/04, Labels).
        await app.Preferences.SaveAsync(new Preferences { Labels = true });
        await app.Labels.UpdateAsync(labels[0].Label.Id, hideNotes: true);
        await Measure("Feed page, a label hiding its notes", 5, () => app.Notes.ListAsync(feed, middle));
        await Measure("Pinned list, a label hiding its notes", 5, () => app.Notes.ListAsync(new NoteQuery(NoteState.Pinned, [NoteKind.Note])));

        // Restore of 10,000 notes without files, into an empty database: 5 s.
        using (var empty = await TestApp.StartAsync())
        {
            var items = Enumerable.Range(0, 10_000).Select(i => new RestoreItem(
                $"{i}.md", Guid.CreateVersion7(), $"Restored note {i} #topic{i % 50}", empty.Clock.Now.AddDays(-i), empty.Clock.Now.AddDays(-i),
                false, i % 10 == 0, NoteKind.Note, null, [], [], [])).ToList();
            var watch = Stopwatch.StartNew();
            var restored = await new RestoreRunner(empty.Storage, empty.Attachments, empty.Labels, empty.Feed, empty.Clock).RunAsync(items);
            var seconds = watch.Elapsed.TotalSeconds;
            Assert.Equal(10_000, restored.Restored);
            report.Add($"{"Restore of 10,000 notes without files",-40} {seconds * 1000,8:F0} ms   (budget 5000 ms)");
            if (seconds > 5 * slack)
            {
                failures.Add($"Restore of 10,000 notes: {seconds:F1} s > 5 s");
            }
        }

        if (Environment.GetEnvironmentVariable("FALCON_PERF_REPORT") is { Length: > 0 } path)
        {
            await File.WriteAllLinesAsync(path, report);
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
