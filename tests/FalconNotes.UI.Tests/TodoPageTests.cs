using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Pages;

namespace FalconNotes.UI.Tests;

/// <summary>Port of the TodoPage cases of TodoCard.test.tsx (docs/11): creating a list, and the feature-off state.
/// The menu-driven cases ("puts labels on it", renaming, Markdown editing) are TodoCard's own and live in
/// TodoCardTests.</summary>
public class TodoPageTests : BunitContext
{
    private void SetUpJs()
    {
        var editor = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        editor.SetupVoid("bindAutoGrow", _ => true).SetVoidResult();
        editor.SetupVoid("focusAtEnd", _ => true).SetVoidResult();
        editor.SetupVoid("focus", _ => true).SetVoidResult();
    }

    [Fact]
    public async Task Says_when_todo_lists_are_turned_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { TodoLists = false });

        var cut = Render<Todo>();

        Assert.Contains("Todo lists are turned off", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input[aria-label='New list name']"));
    }

    [Fact]
    public async Task Creates_a_list()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();

        var cut = Render<Todo>();
        cut.WaitForAssertion(() => Assert.Contains("No lists yet", cut.Markup, StringComparison.Ordinal));

        cut.Find("input[aria-label='New list name']").Input("Packing");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Packing", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        var created = (await app.Core.Notes.ListAsync(new NoteQuery(NoteState.Feed, [NoteKind.Todo]),
            cancellationToken: Xunit.TestContext.Current.CancellationToken)).Items.Single();
        Assert.Equal("# Packing", created.Content);
    }
}
