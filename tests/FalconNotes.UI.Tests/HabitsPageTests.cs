using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Pages;
using Microsoft.AspNetCore.Components.Web;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Port of HabitsPage.test.tsx (docs/11). The reference's single "renames, archives and deletes a habit from its
/// menu" test relies on a static mocked list that keeps showing the habit as active even after the mocked archive
/// call; against this port's real database, archiving truly moves it out of the active list, so that case is split
/// here into separate tests for rename, archive, trash-and-undo and delete-for-good, each against its own habit.
/// </summary>
public class HabitsPageTests : BunitContext
{
    private static readonly TimeSpan LongWait = TimeSpan.FromSeconds(5);

    /// <summary>A moment that reads as this wall-clock time in the host's own time zone (the habits page's days are
    /// this device's calendar days), so the test does not depend on the host running in UTC.</summary>
    private static DateTimeOffset LocalTime(int year, int month, int day, int hour = 8, int minute = 0)
    {
        var naive = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(naive, TimeZoneInfo.Local.GetUtcOffset(naive));
    }

    private static async Task<Note> CreateHabitAsync(UiTestApp app, string content, DateTimeOffset createdAt, bool archived = false)
    {
        var restoreTo = app.Core.Clock.Now;
        app.Core.Clock.Now = createdAt;
        var note = await app.Core.Notes.CreateAsync(content, NoteKind.Habit);
        if (archived)
        {
            note = (await app.Core.Notes.PatchAsync(note.Id, new NotePatch(IsArchived: true)))!;
        }

        app.Core.Clock.Now = restoreTo;
        return note;
    }

    /// <summary>Opens a habit's actions menu and waits for its items to actually be in the DOM: opening is a plain
    /// state flip, but under load the render that shows the items can land a tick after the click returns.</summary>
    private static void OpenHabitMenu(IRenderedComponent<Habits> cut)
    {
        cut.Find("button[aria-label='Habit actions']").Click();
        cut.WaitForState(() => cut.FindAll("button[role=menuitem]").Count > 0, LongWait);
    }

    private void SetUpJs()
    {
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("openMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("closeMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("showModal", _ => true).SetVoidResult();
        dialogs.SetupVoid("close", _ => true).SetVoidResult();
        var editor = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        editor.SetupVoid("focusAtEnd", _ => true).SetVoidResult();
    }

    /// <summary>Polls with a plain await, never a blocking one (docs/11, the same rule NoteCardTests follows): a
    /// blocking wait on the renderer's own thread would deadlock a pending save that needs that thread to resume on.</summary>
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

    [Fact]
    public async Task Says_when_the_habit_tracker_is_turned_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { HabitTracker = false });

        var cut = Render<Habits>();

        Assert.Contains("The habit tracker is turned off", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("/settings", cut.Find("a").GetAttribute("href"));
    }

    [Fact]
    public async Task Asks_for_a_first_habit()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { HabitTracker = true });

        var cut = Render<Habits>();

        cut.WaitForAssertion(() => Assert.Contains("No habits yet", cut.Markup, StringComparison.Ordinal), LongWait);
    }

    [Fact]
    public async Task Lists_habits_oldest_first_with_the_last_seven_days_and_ticks_a_day()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { HabitTracker = true });
        var read = await CreateHabitAsync(app, "# Read 20 minutes\n\n- 2026-09-27\n- 2026-09-28", LocalTime(2026, 9, 20));
        await CreateHabitAsync(app, "# Walk", LocalTime(2026, 9, 1));
        app.Core.Clock.Now = LocalTime(2026, 9, 29, 10);

        var cut = Render<Habits>();
        cut.WaitForState(() => cut.FindAll("ul[aria-label='Your habits'] > li").Count == 2, LongWait);

        var names = cut.FindAll("ul[aria-label='Your habits'] > li > span.font-medium").Select(s => s.TextContent).ToList();
        Assert.Equal(["Walk", "Read 20 minutes"], names);
        Assert.Contains("Sep 23", cut.Markup, StringComparison.Ordinal);
        var today = cut.Find("button[aria-label='Read 20 minutes, today, Tuesday, September 29']");

        today.Click();

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("button[aria-label='Read 20 minutes, today, Tuesday, September 29']").GetAttribute("aria-pressed")), LongWait);
        await WaitUntilAsync(async () => (await app.Core.Notes.GetAsync(read.Id))!.Content.Contains("- 2026-09-29", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Goes_a_week_back_and_forward_again_never_past_today()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { HabitTracker = true });
        await CreateHabitAsync(app, "# Read 20 minutes", LocalTime(2026, 9, 20));
        app.Core.Clock.Now = LocalTime(2026, 9, 29, 10);
        var cut = Render<Habits>();
        cut.WaitForState(() => cut.FindAll("button[aria-label='Later days']").Count == 1, LongWait);
        Assert.True(cut.Find("button[aria-label='Later days']").HasAttribute("disabled"));

        cut.Find("button[aria-label='Earlier days']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Sep 16", cut.Markup, StringComparison.Ordinal), LongWait);
        cut.Find("button[aria-label='Read 20 minutes, Wednesday, September 16']");
        Assert.False(cut.Find("button[aria-label='Later days']").HasAttribute("disabled"));

        cut.Find("button[aria-label='Later days']").Click();

        cut.WaitForAssertion(() => cut.Find("button[aria-label='Read 20 minutes, today, Tuesday, September 29']"), LongWait);
    }

    [Fact]
    public async Task Adds_a_habit()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { HabitTracker = true });
        app.Core.Clock.Now = LocalTime(2026, 9, 29, 10);
        var cut = Render<Habits>();
        cut.WaitForAssertion(() => Assert.Contains("No habits yet", cut.Markup, StringComparison.Ordinal), LongWait);

        cut.Find("input[aria-label='New habit name']").Input("  Stretch ");
        cut.Find("form").Submit();

        await WaitUntilAsync(async () => (await app.Core.Notes.ListHabitsAsync()).Count == 1);
        var created = (await app.Core.Notes.ListHabitsAsync()).Single();
        Assert.Equal("# Stretch", created.Content);
        cut.WaitForAssertion(() => Assert.Equal("", cut.Find("input[aria-label='New habit name']").GetAttribute("value")), LongWait);
    }

    [Fact]
    public async Task Renames_a_habit_from_its_menu()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { HabitTracker = true });
        SetUpJs();
        var read = await CreateHabitAsync(app, "# Read 20 minutes\n\n- 2026-09-27\n- 2026-09-28", LocalTime(2026, 9, 20));
        app.Core.Clock.Now = LocalTime(2026, 9, 29, 10);
        var cut = Render<Habits>();
        cut.WaitForState(() => cut.FindAll("button[aria-label='Habit actions']").Count == 1, LongWait);

        OpenHabitMenu(cut);
        cut.FindAll("button[role=menuitem]").First(b => b.TextContent.Trim() == "Rename").Click();
        var name = cut.Find("input[aria-label='Habit name']");
        name.Input("Read 30 minutes");
        name.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        await WaitUntilAsync(async () => (await app.Core.Notes.GetAsync(read.Id))!.Content.StartsWith("# Read 30 minutes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Archives_a_habit_moving_it_into_archived_habits()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { HabitTracker = true });
        SetUpJs();
        await CreateHabitAsync(app, "# Read 20 minutes", LocalTime(2026, 9, 20));
        app.Core.Clock.Now = LocalTime(2026, 9, 29, 10);
        var cut = Render<Habits>();
        cut.WaitForState(() => cut.FindAll("button[aria-label='Habit actions']").Count == 1, LongWait);

        OpenHabitMenu(cut);
        cut.FindAll("button[role=menuitem]").First(b => b.TextContent.Trim() == "Archive").Click();

        await WaitUntilAsync(() => Task.FromResult(app.Toasts.Current.Any(t => t.Message == "Archived. It keeps its history under Archived habits.")));
        cut.WaitForAssertion(() => Assert.Contains("No habits yet", cut.Markup, StringComparison.Ordinal), LongWait);
        cut.WaitForState(() => cut.FindAll("button").Any(b => b.TextContent.Contains("Archived habits", StringComparison.Ordinal)), LongWait);
        var toggle = cut.FindAll("button").First(b => b.TextContent.Contains("Archived habits", StringComparison.Ordinal));
        Assert.Equal("Archived habits (1)", toggle.TextContent.Trim());
        Assert.DoesNotContain("Read 20 minutes", cut.Markup, StringComparison.Ordinal);

        toggle.Click();

        Assert.Contains("Read 20 minutes", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moves_a_habit_to_the_trash_and_undo_restores_it()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { HabitTracker = true });
        SetUpJs();
        var read = await CreateHabitAsync(app, "# Read 20 minutes", LocalTime(2026, 9, 20));
        app.Core.Clock.Now = LocalTime(2026, 9, 29, 10);
        var cut = Render<Habits>();
        cut.WaitForState(() => cut.FindAll("button[aria-label='Habit actions']").Count == 1, LongWait);

        OpenHabitMenu(cut);
        cut.FindAll("button[role=menuitem]").First(b => b.TextContent.Trim() == "Move to trash").Click();

        await WaitUntilAsync(() => Task.FromResult(app.Toasts.Current.Any(t => t.Message == "Habit moved to the trash.")));
        Assert.True((await app.Core.Notes.GetAsync(read.Id))!.IsTrashed);

        var toast = app.Toasts.Current.Single(t => t.Message == "Habit moved to the trash.");
        await toast.Action!.OnClick();

        await WaitUntilAsync(async () => !(await app.Core.Notes.GetAsync(read.Id))!.IsTrashed);
    }

    [Fact]
    public async Task Deletes_a_habit_for_good_after_asking_when_the_trash_is_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { HabitTracker = true, Trash = false });
        SetUpJs();
        var read = await CreateHabitAsync(app, "# Read 20 minutes\n\n- 2026-09-27\n- 2026-09-28", LocalTime(2026, 9, 20));
        app.Core.Clock.Now = LocalTime(2026, 9, 29, 10);
        var cut = Render<Habits>();
        cut.WaitForState(() => cut.FindAll("button[aria-label='Habit actions']").Count == 1, LongWait);

        OpenHabitMenu(cut);
        cut.FindAll("button[role=menuitem]").First(b => b.TextContent.Trim() == "Delete…").Click();

        cut.WaitForAssertion(() => cut.Find("h2"), LongWait);
        Assert.Equal("Delete this habit?", cut.Find("h2").TextContent);
        Assert.Contains("\"Read 20 minutes\" and its history (2 days done) will be deleted permanently.", cut.Markup, StringComparison.Ordinal);
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Delete").Click();

        await WaitUntilAsync(async () => await app.Core.Notes.GetAsync(read.Id) is null);
    }

    [Fact]
    public async Task Keeps_archived_habits_aside_to_restore_or_delete()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { HabitTracker = true });
        SetUpJs();
        var stretch = await CreateHabitAsync(app, "# Stretch\n\n- 2026-08-01\n- 2026-08-02\n- 2026-08-03", LocalTime(2026, 8, 1), archived: true);
        app.Core.Clock.Now = LocalTime(2026, 9, 29, 10);
        var cut = Render<Habits>();
        cut.WaitForState(() => cut.FindAll("button").Any(b => b.TextContent.Contains("Archived habits", StringComparison.Ordinal)), LongWait);
        var toggle = cut.FindAll("button").First(b => b.TextContent.Contains("Archived habits", StringComparison.Ordinal));
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("li")); // collapsed: no archived row, and no active ones either

        toggle.Click();

        Assert.Contains(cut.FindAll("li"), li => li.TextContent.Contains("Stretch", StringComparison.Ordinal));
        Assert.Contains("3 days", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button[aria-label^='Stretch, ']"));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Restore").Click();

        await WaitUntilAsync(async () => !(await app.Core.Notes.GetAsync(stretch.Id))!.IsArchived);
    }
}
