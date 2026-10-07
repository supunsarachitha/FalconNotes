using FalconNotes.Core.Domain;
using FalconNotes.UI.Pages;

namespace FalconNotes.UI.Tests;

/// <summary>New tests (docs/11): QuickNotesPage has no dedicated reference test; this covers the feature-off
/// state, the composer posting a Quick note, and that it shows up without the "Quick note" kind badge
/// (ShowKind=false, since every note on this page already is one).</summary>
public class QuickTests : BunitContext
{
    private void SetUpJs()
    {
        var editor = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        editor.SetupVoid("bindAutoGrow", _ => true).SetVoidResult();
        editor.SetupVoid("growNow", _ => true).SetVoidResult();
        editor.SetupVoid("focusAtEnd", _ => true).SetVoidResult();
        editor.SetupVoid("positionPopup", _ => true).SetVoidResult();
        editor.SetupVoid("setSelection", _ => true).SetVoidResult();
        editor.SetupVoid("applyEdit", _ => true).SetVoidResult();
        var gestures = JSInterop.SetupModule("./_content/FalconNotes.UI/js/gestures.js");
        gestures.SetupVoid("bindDoubleTap", _ => true).SetVoidResult();
        gestures.SetupVoid("unbindDoubleTap", _ => true).SetVoidResult();
        var markdown = JSInterop.SetupModule("./_content/FalconNotes.UI/js/markdown.js");
        markdown.SetupVoid("bindTaskToggle", _ => true).SetVoidResult();
        markdown.SetupVoid("unbindTaskToggle", _ => true).SetVoidResult();
    }

    [Fact]
    public async Task Shows_feature_off_when_quick_notes_are_turned_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { QuickNotes = false });

        var cut = Render<Quick>();

        Assert.Contains("turned off", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Posts_a_quick_note_without_the_kind_badge()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();

        var cut = Render<Quick>();
        cut.Find("#composer").Input("jot this down");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Post").Click();

        cut.WaitForAssertion(() => Assert.Contains("jot this down", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        // Not just "Quick note" as a substring: the page's own <h1> is "Quick notes". The kind badge, when shown
        // elsewhere (ShowKind=true), is this exact chip text inside the note's header.
        Assert.DoesNotContain(cut.FindAll("article span"), s => s.TextContent.Trim() == "Quick note");
        var created = (await app.Core.Notes.ListAsync(
            new Core.Notes.NoteQuery(NoteState.Feed, [NoteKind.Quick]), cancellationToken: Xunit.TestContext.Current.CancellationToken)).Items.Single();
        Assert.Equal(NoteKind.Quick, created.Kind);
    }
}
