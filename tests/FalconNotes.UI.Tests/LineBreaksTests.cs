using FalconNotes.Core.Domain;
using FalconNotes.Core.Pdf;
using FalconNotes.UI.Components;
using FalconNotes.UI.Pages.Settings;
using FalconNotes.UI.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New: Keep line breaks (docs/04, Markdown rendering; docs/07, Settings); the web app always joins the lines of a
/// single line break, so there is no reference test to port. The switch in Settings → Writing, off by default, and
/// what it changes: a note on the screen, and its PDF.
/// </summary>
public class LineBreaksTests : BunitContext
{
    private const string Lines = "milk\neggs";

    private void SetUpJs()
    {
        var gestures = JSInterop.SetupModule("./_content/FalconNotes.UI/js/gestures.js");
        gestures.SetupVoid("bindDoubleTap", _ => true).SetVoidResult();
        gestures.SetupVoid("unbindDoubleTap", _ => true).SetVoidResult();
        var markdown = JSInterop.SetupModule("./_content/FalconNotes.UI/js/markdown.js");
        markdown.SetupVoid("bindTaskToggle", _ => true).SetVoidResult();
        markdown.SetupVoid("unbindTaskToggle", _ => true).SetVoidResult();
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("openMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("closeMenu", _ => true).SetVoidResult();
    }

    [Fact]
    public async Task The_switch_is_off_as_installed_and_turning_it_on_is_saved()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<EditingSection>();
        var toggle = cut.Find("button[role=switch][aria-label='Keep line breaks']");
        Assert.Equal("false", toggle.GetAttribute("aria-checked"));

        toggle.Click();

        cut.WaitForAssertion(() => Assert.True(app.State.Preferences.LineBreaks));
        Assert.True((await app.Core.Preferences.GetAsync()).LineBreaks);
    }

    [Fact]
    public async Task A_note_joins_its_lines_as_installed()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var note = await app.PostAsync(Lines);

        var cut = Render<Markdown>(p => p.Add(m => m.Note, note).Add(m => m.Content, note.Content));

        Assert.Empty(cut.FindAll("br"));
        Assert.Equal("milk\neggs", cut.Find("p").TextContent);
    }

    [Fact]
    public async Task A_note_shows_each_line_on_its_own_once_the_switch_is_on()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var note = await app.PostAsync(Lines);
        Assert.Empty(Render<Markdown>(p => p.Add(m => m.Note, note).Add(m => m.Content, note.Content)).FindAll("br"));

        // The same note at the same revision: the switch alone changes how it is shown.
        await app.State.UpdatePreferencesAsync(p => p with { LineBreaks = true });
        var cut = Render<Markdown>(p => p.Add(m => m.Note, note).Add(m => m.Content, note.Content));

        Assert.Single(cut.FindAll("p > br"));
        Assert.Equal(note.Content, (await app.Core.Notes.GetAsync(note.Id))!.Content); // nothing is rewritten
    }

    [Fact]
    public async Task A_notes_pdf_keeps_its_line_breaks_only_with_the_switch_on()
    {
        var writer = new FakePdfWriter();
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { PdfExport = true });
        Services.AddSingleton(new PdfExportService(writer, app.Saver, app.Core.Directories, app.Core.Clock));
        SetUpJs();
        var note = await app.PostAsync(Lines);

        await ExportAsync(note);
        await app.State.UpdatePreferencesAsync(p => p with { LineBreaks = true });
        await ExportAsync(note);

        Assert.Equal(["milk eggs", "milk\neggs"], writer.Written.Select(pdf => pdf.Blocks.OfType<PdfText>().Last().Spans.Single().Text));

        async Task ExportAsync(Note exported)
        {
            var saved = app.Saver.Saved.Count;
            var cut = Render<NoteCard>(p => p.Add(c => c.Note, exported));
            cut.Find("button[aria-label='Note actions']").Click();
            await cut.FindAll("button[role=menuitem]").First(item => item.TextContent.Trim() == "Export to PDF…").ClickAsync(new());
            cut.WaitForAssertion(() => Assert.Equal(saved + 1, app.Saver.Saved.Count));
        }
    }
}
