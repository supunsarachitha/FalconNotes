using FalconNotes.UI.Components;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Port of the applicable "opens images in a viewer" cases of AttachmentGallery.test.tsx, against ImageViewer
/// directly. Save a copy replaces the web app's download link, and Share (new, Android only) hands a decrypted
/// copy to the system share sheet (docs/07, Attachments; docs/12, Share).
/// </summary>
public class ImageViewerTests : BunitContext
{
    private void SetUpDialogsJs()
    {
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("showModal", _ => true).SetVoidResult();
        dialogs.SetupVoid("close", _ => true).SetVoidResult();
    }

    private async Task<(UiTestApp App, IReadOnlyList<Core.Domain.Attachment> Images)> SetUpAsync(int count)
    {
        var app = await UiTestApp.StartAsync(Services, "Alex");
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var added = await app.Core.AddFileAsync($"photo-{i}.png", type: "image/png");
            ids.Add(added.Id);
        }

        var note = await app.Core.Notes.CreateAsync("Pictures", attachmentIds: ids);
        return (app, note.Attachments);
    }

    [Fact]
    public async Task Is_closed_when_index_is_null()
    {
        var (app, images) = await SetUpAsync(1);
        using var owner = app;

        var cut = Render<ImageViewer>(p => p.Add(v => v.Images, images));

        Assert.Empty(cut.FindAll("h2"));
    }

    [Fact]
    public async Task Shows_the_image_at_the_given_index_without_paging_controls_when_there_is_only_one()
    {
        var (app, images) = await SetUpAsync(1);
        using var owner = app;
        SetUpDialogsJs();

        var cut = Render<ImageViewer>(p => p.Add(v => v.Images, images).Add(v => v.Index, 0));

        Assert.Contains(images[0].FileName, cut.Find("h2").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button[aria-label='Next image']"));
    }

    [Fact]
    public async Task Pages_forward_and_wraps_around()
    {
        var (app, images) = await SetUpAsync(3);
        using var owner = app;
        SetUpDialogsJs();
        var current = 1;
        var cut = Render<ImageViewer>(p => p
            .Add(v => v.Images, images)
            .Add(v => v.Index, current)
            .Add(v => v.IndexChanged, i => current = i));

        cut.Find("button[aria-label='Next image']").Click();
        Assert.Equal(2, current);

        cut.Render(p => p.Add(v => v.Index, current));
        cut.Find("button[aria-label='Next image']").Click();
        Assert.Equal(0, current); // wraps around
    }

    [Fact]
    public async Task Save_a_copy_hands_the_images_bytes_to_the_file_saver()
    {
        var (app, images) = await SetUpAsync(1);
        using var owner = app;
        SetUpDialogsJs();
        var cut = Render<ImageViewer>(p => p.Add(v => v.Images, images).Add(v => v.Index, 0));

        cut.Find($"button[aria-label='Save a copy of {images[0].FileName}']").Click();

        cut.WaitForAssertion(() => Assert.Single(app.Saver.Saved));
        Assert.Equal(images[0].FileName, app.Saver.Saved[0].SuggestedName);
    }

    [Fact]
    public async Task Share_hands_a_decrypted_copy_to_the_share_sheet()
    {
        var (app, images) = await SetUpAsync(1);
        using var owner = app;
        SetUpDialogsJs();
        var cut = Render<ImageViewer>(p => p.Add(v => v.Images, images).Add(v => v.Index, 0));

        cut.Find($"button[aria-label='Share {images[0].FileName}']").Click();

        cut.WaitForAssertion(() => Assert.Single(app.Share.Shared));
        Assert.Equal(images[0].ContentType, app.Share.Shared[0].ContentType);
        Assert.True(File.Exists(app.Share.Shared[0].Path));
    }

    [Fact]
    public async Task Close_button_tells_the_caller_to_close()
    {
        var (app, images) = await SetUpAsync(1);
        using var owner = app;
        SetUpDialogsJs();
        var closed = false;
        var cut = Render<ImageViewer>(p => p
            .Add(v => v.Images, images)
            .Add(v => v.Index, 0)
            .Add(v => v.OnClose, () => closed = true));

        cut.Find("button[aria-label=Close]").Click();

        Assert.True(closed);
    }
}
