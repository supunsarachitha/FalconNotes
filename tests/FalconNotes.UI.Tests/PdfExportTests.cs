using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Pdf;
using FalconNotes.UI.Components;
using FalconNotes.UI.Pages.Settings;
using FalconNotes.UI.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New: Export to PDF (docs/07, Export to PDF); the web app has nothing like it, so there is no reference test to
/// port. The switch in Settings → Features, off by default and only where the platform can write a PDF, and the item
/// it adds to the menu of a note and of a todo list.
/// </summary>
public class PdfExportTests : BunitContext
{
    private readonly FakePdfWriter _writer = new();

    /// <summary>Starts the app as a platform that can write PDFs: it registers the export, as Android does.</summary>
    private async Task<UiTestApp> StartWithPdfAsync(Preferences? preferences = null)
    {
        var app = await UiTestApp.StartAsync(Services, "Alex", preferences);
        Services.AddSingleton(new PdfExportService(_writer, app.Saver, app.Core.Directories, app.Core.Clock));
        return app;
    }

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
        dialogs.SetupVoid("showModal", _ => true).SetVoidResult();
        dialogs.SetupVoid("close", _ => true).SetVoidResult();
    }

    private static List<string> Menu<T>(IRenderedComponent<T> cut, string label)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        cut.Find($"button[aria-label='{label}']").Click();
        return cut.FindAll("button[role=menuitem]").Select(item => item.TextContent.Trim()).ToList();
    }

    private static AngleSharp.Dom.IElement Item<T>(IRenderedComponent<T> cut)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("button[role=menuitem]").First(item => item.TextContent.Trim() == "Export to PDF…");

    [Fact]
    public async Task The_switch_is_off_as_installed_and_turning_it_on_is_saved()
    {
        using var app = await StartWithPdfAsync();
        var cut = Render<FeaturesSection>();
        var toggle = cut.Find("button[role=switch][aria-label='Export to PDF']");
        Assert.Equal("false", toggle.GetAttribute("aria-checked"));
        Assert.Contains("The PDF is not encrypted, so keep it somewhere safe.", cut.Markup, StringComparison.Ordinal);

        toggle.Click();

        cut.WaitForAssertion(() => Assert.True(app.State.Preferences.PdfExport));
        Assert.True((await app.Core.Preferences.GetAsync()).PdfExport);
    }

    [Fact]
    public async Task A_platform_that_cannot_write_a_pdf_offers_neither_the_switch_nor_the_menu_item()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { PdfExport = true });
        SetUpJs();

        Assert.Empty(Render<FeaturesSection>().FindAll("button[role=switch][aria-label='Export to PDF']"));
        var note = await app.PostAsync("oats");
        var card = Render<NoteCard>(p => p.Add(c => c.Note, note));
        Assert.DoesNotContain("Export to PDF…", Menu(card, "Note actions"));
    }

    [Fact]
    public async Task The_menu_has_no_export_until_the_switch_is_on()
    {
        using var app = await StartWithPdfAsync();
        SetUpJs();
        var note = await app.PostAsync("oats");

        var off = Render<NoteCard>(p => p.Add(c => c.Note, note));
        Assert.DoesNotContain("Export to PDF…", Menu(off, "Note actions"));

        await app.State.UpdatePreferencesAsync(p => p with { PdfExport = true });
        var on = Render<NoteCard>(p => p.Add(c => c.Note, note));
        var items = Menu(on, "Note actions");
        Assert.Equal(items.IndexOf("Copy text") + 1, items.IndexOf("Export to PDF…"));
    }

    [Fact]
    public async Task Exporting_a_note_saves_its_pdf_under_its_first_line()
    {
        using var app = await StartWithPdfAsync(new Preferences { PdfExport = true });
        SetUpJs();
        var note = await app.PostAsync("# Call the plumber\n\n- [ ] book a time");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));
        Menu(cut, "Note actions");

        await Item(cut).ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, toast => toast.Message == "Saved as a PDF." && !toast.IsError));
        var (name, bytes) = Assert.Single(app.Saver.Saved);
        Assert.Equal("Call the plumber.pdf", name);
        Assert.Equal("%PDF-fake"u8.ToArray(), bytes);
        var blocks = Assert.Single(_writer.Written).Blocks;
        Assert.Equal(PdfTextStyle.Heading1, Assert.IsType<PdfText>(blocks[0]).Style);
        Assert.False(Assert.IsType<PdfText>(blocks[^1]).Checked);
        Assert.Empty(Directory.GetFiles(Path.Combine(app.Core.Directories.CacheDirectory, "export")));
    }

    [Fact]
    public async Task Cancelling_the_save_dialog_says_nothing_and_a_failure_says_so()
    {
        using var app = await StartWithPdfAsync(new Preferences { PdfExport = true });
        SetUpJs();
        var note = await app.PostAsync("oats");
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        app.Saver.Succeeds = false;
        Menu(cut, "Note actions");
        await Item(cut).ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Single(app.Saver.Saved));
        Assert.Empty(app.Toasts.Current);

        _writer.Fails = true;
        Menu(cut, "Note actions");
        await Item(cut).ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Contains(
            app.Toasts.Current, toast => toast.Message == "The PDF could not be made. Please try again." && toast.IsError));
        Assert.Single(app.Saver.Saved); // nothing was offered for saving
    }

    [Fact]
    public async Task A_locked_note_cannot_be_exported_until_it_is_unlocked_to_be_read()
    {
        using var app = await StartWithPdfAsync(new Preferences { PdfExport = true, NoteLock = true });
        SetUpJs();
        var note = await app.PostAsync("a secret");
        var locked = (await app.Core.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true)))!;
        var cut = Render<NoteCard>(p => p.Add(c => c.Note, locked));
        Assert.DoesNotContain("Export to PDF…", Menu(cut, "Note actions"));

        app.Core.Locks.Open(locked.Id);

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button[role=menuitem]"), item => item.TextContent.Trim() == "Export to PDF…"));
    }

    [Fact]
    public async Task A_todo_list_is_exported_with_its_ticks_as_they_show()
    {
        using var app = await StartWithPdfAsync(new Preferences { PdfExport = true });
        SetUpJs();
        var note = await app.PostAsync("# Groceries\n\n- [x] oats\n- [ ] maple syrup", NoteKind.Todo);
        var cut = Render<TodoCard>(p => p.Add(c => c.Note, note));
        Assert.Contains("Export to PDF…", Menu(cut, "List actions"));

        await Item(cut).ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Equal("Groceries.pdf", Assert.Single(app.Saver.Saved).SuggestedName));
        var blocks = Assert.Single(_writer.Written).Blocks.OfType<PdfText>().ToList();
        Assert.Equal([true, false], blocks.Where(block => block.Checked is not null).Select(block => block.Checked!.Value));
    }

    [Fact]
    public async Task A_note_in_the_trash_has_no_export()
    {
        using var app = await StartWithPdfAsync(new Preferences { PdfExport = true });
        SetUpJs();
        var note = await app.PostAsync("oats");
        var trashed = (await app.Core.Notes.PatchAsync(note.Id, new NotePatch(IsTrashed: true)))!;

        var cut = Render<TrashCard>(p => p.Add(c => c.Note, trashed));

        Assert.DoesNotContain("Export to PDF", cut.Markup, StringComparison.Ordinal);
    }
}
