using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Components;
using Microsoft.AspNetCore.Components;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New tests (docs/11): the web app has no dedicated test for NoteList.tsx (it is exercised through HomePage's own
/// tests, ported with Home in a later step), so this covers the list itself directly: paging, the empty and
/// loading states, HideIds, reloading when notes change elsewhere, and a custom RenderNote.
/// </summary>
public class NoteListTests : BunitContext
{
    private static RenderFragment<Note> PlainText => note => builder =>
    {
        builder.OpenElement(0, "p");
        builder.AddContent(1, note.Content);
        builder.CloseElement();
    };

    [Fact]
    public async Task Shows_the_empty_state_when_nothing_matches()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");

        var cut = Render<NoteList>(p => p
            .Add(l => l.State, NoteState.Feed)
            .Add(l => l.RenderNote, PlainText)
            .Add(l => l.Empty, (RenderFragment)(builder => builder.AddContent(0, "Nothing here"))));

        cut.WaitForAssertion(() => Assert.Contains("Nothing here", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shows_matching_notes()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.PostAsync("first note");
        await app.PostAsync("second note");

        var cut = Render<NoteList>(p => p.Add(l => l.State, NoteState.Feed).Add(l => l.RenderNote, PlainText));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("first note", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("second note", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Filters_by_tag()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.PostAsync("about #work");
        await app.PostAsync("about #home");

        var cut = Render<NoteList>(p => p.Add(l => l.State, NoteState.Active).Add(l => l.Tag, "work").Add(l => l.RenderNote, PlainText));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("about #work", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("about #home", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Hides_notes_shown_elsewhere_on_the_page()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var shownElsewhere = await app.PostAsync("today's daily note");
        await app.PostAsync("an ordinary note");

        var cut = Render<NoteList>(p => p
            .Add(l => l.State, NoteState.Feed)
            .Add(l => l.RenderNote, PlainText)
            .Add(l => l.HideIds, [shownElsewhere.Id]));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("today's daily note", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("an ordinary note", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Pages_past_the_first_twenty_with_load_more()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var observe = JSInterop.SetupModule("./_content/FalconNotes.UI/js/observe.js");
        observe.SetupVoid("bindSentinel", _ => true).SetVoidResult();
        observe.SetupVoid("unbindSentinel", _ => true).SetVoidResult();
        for (var i = 0; i < 21; i++)
        {
            await app.PostAsync($"note {i:D2}");
        }

        var cut = Render<NoteList>(p => p.Add(l => l.State, NoteState.Feed).Add(l => l.RenderNote, PlainText));
        cut.WaitForAssertion(() => Assert.Equal(20, cut.FindAll("p").Count(e => e.TextContent.StartsWith("note ", StringComparison.Ordinal))));
        var loadMore = cut.FindAll("button").First(b => b.TextContent.Trim() == "Load more");

        loadMore.Click();

        cut.WaitForAssertion(() => Assert.Equal(21, cut.FindAll("p").Count(e => e.TextContent.StartsWith("note ", StringComparison.Ordinal))));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Load more");
    }

    [Fact]
    public async Task Reloads_when_a_note_changes_elsewhere()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<NoteList>(p => p.Add(l => l.State, NoteState.Feed).Add(l => l.RenderNote, PlainText));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[aria-busy]")));

        await app.PostAsync("a note posted after the list rendered");

        cut.WaitForAssertion(() => Assert.Contains("a note posted after the list rendered", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shows_the_end_marker_once_a_longer_list_is_fully_loaded()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        for (var i = 0; i < 6; i++)
        {
            await app.PostAsync($"note {i}");
        }

        var cut = Render<NoteList>(p => p.Add(l => l.State, NoteState.Feed).Add(l => l.RenderNote, PlainText));

        cut.WaitForAssertion(() => Assert.Contains("caught up", cut.Markup, StringComparison.Ordinal));
    }
}
