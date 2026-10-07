using FalconNotes.UI.Components;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New tests (docs/11): the web app's FileLink is a plain download link, so it has no equivalent case for this
/// adaptation (docs/07, Attachments) — a chip whose menu opens a stored file or saves a copy of it.
/// </summary>
public class FileLinkTests : BunitContext
{
    private IRenderedComponent<FileLink> OpenMenu(Core.Domain.Attachment file)
    {
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("openMenu", _ => true).SetVoidResult();
        dialogs.SetupVoid("closeMenu", _ => true).SetVoidResult();
        var cut = Render<FileLink>(p => p.Add(f => f.File, file));
        cut.Find("button[aria-label='report.pdf actions']").Click();
        return cut;
    }

    [Fact]
    public async Task Open_copies_a_decrypted_file_and_hands_it_to_the_file_opener()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var added = await app.Core.AddFileAsync("report.pdf", type: "application/pdf");
        var note = await app.Core.Notes.CreateAsync("With a file", attachmentIds: [added.Id]);
        var cut = OpenMenu(note.Attachments[0]);

        MenuItem(cut, "Open").Click();

        cut.WaitForAssertion(() => Assert.Single(app.Opener.Opened));
        Assert.EndsWith("report.pdf", app.Opener.Opened[0].Path, StringComparison.Ordinal);
        Assert.Equal("application/pdf", app.Opener.Opened[0].ContentType);
    }

    [Fact]
    public async Task Save_a_copy_decrypts_the_file_and_hands_its_bytes_to_the_file_saver()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var added = await app.Core.AddFileAsync("report.pdf", size: 20, type: "application/pdf");
        var note = await app.Core.Notes.CreateAsync("With a file", attachmentIds: [added.Id]);
        var cut = OpenMenu(note.Attachments[0]);

        MenuItem(cut, "Save a copy").Click();

        cut.WaitForAssertion(() => Assert.Single(app.Saver.Saved));
        Assert.Equal("report.pdf", app.Saver.Saved[0].SuggestedName);
        Assert.Equal(20, app.Saver.Saved[0].Bytes.Length);
    }

    [Fact]
    public async Task Open_tells_the_user_when_no_app_can_open_the_file()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        app.Opener.Succeeds = false;
        var added = await app.Core.AddFileAsync("report.pdf", type: "application/pdf");
        var note = await app.Core.Notes.CreateAsync("With a file", attachmentIds: [added.Id]);
        var cut = OpenMenu(note.Attachments[0]);

        MenuItem(cut, "Open").Click();

        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.IsError));
    }

    private static AngleSharp.Dom.IElement MenuItem(IRenderedComponent<FileLink> cut, string text) =>
        cut.FindAll("button[role=menuitem]").First(b => b.TextContent.Contains(text, StringComparison.Ordinal));
}
