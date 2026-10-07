using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Pages;

namespace FalconNotes.UI.Tests;

/// <summary>New tests (docs/11): ArchivePage has no dedicated reference test; this covers the feature-off state
/// and that archived notes show, each through a real NoteCard.</summary>
public class ArchiveTests : BunitContext
{
    private void SetUpJs()
    {
        var gestures = JSInterop.SetupModule("./_content/FalconNotes.UI/js/gestures.js");
        gestures.SetupVoid("bindDoubleTap", _ => true).SetVoidResult();
        gestures.SetupVoid("unbindDoubleTap", _ => true).SetVoidResult();
        var markdown = JSInterop.SetupModule("./_content/FalconNotes.UI/js/markdown.js");
        markdown.SetupVoid("bindTaskToggle", _ => true).SetVoidResult();
        markdown.SetupVoid("unbindTaskToggle", _ => true).SetVoidResult();
    }

    [Fact]
    public async Task Shows_feature_off_when_the_archive_is_turned_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Archive = false });

        var cut = Render<Archive>();

        Assert.Contains("turned off", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Turn it on in Settings.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_archived_notes()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var note = await app.PostAsync("an archived note");
        await app.Core.Notes.PatchAsync(note.Id, new NotePatch(IsArchived: true));

        var cut = Render<Archive>();

        cut.WaitForAssertion(() => Assert.Contains("an archived note", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Shows_the_empty_state_with_nothing_archived()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");

        var cut = Render<Archive>();

        cut.WaitForAssertion(() => Assert.Contains("The archive is empty", cut.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
    }
}
