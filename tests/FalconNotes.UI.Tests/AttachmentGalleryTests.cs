using FalconNotes.UI.Components;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Port of the applicable cases of AttachmentGallery.test.tsx (docs/11). The web app's cases about end-to-end
/// decryption, blob URLs and viewport-gated loading do not apply here: the media handler serves every file from its
/// own URL (docs/02), so there is no decrypt-and-wait step and no need to defer loading by hand.
/// </summary>
public class AttachmentGalleryTests : BunitContext
{
    private async Task<(UiTestApp App, IReadOnlyList<Core.Domain.Attachment> Files)> SetUpAsync(params (string Name, string Type)[] files)
    {
        var app = await UiTestApp.StartAsync(Services, "Alex");
        var ids = new List<Guid>();
        foreach (var (name, type) in files)
        {
            var added = await app.Core.AddFileAsync(name, type: type);
            ids.Add(added.Id);
        }

        var note = await app.Core.Notes.CreateAsync("With files", attachmentIds: ids);
        return (app, note.Attachments);
    }

    [Fact]
    public async Task Shows_nothing_for_a_note_with_no_files()
    {
        var (app, files) = await SetUpAsync();
        using var owner = app;

        var cut = Render<AttachmentGallery>(p => p.Add(g => g.Attachments, files));

        Assert.Empty(cut.Nodes);
    }

    [Fact]
    public async Task Shows_a_single_image_at_its_own_shape()
    {
        var (app, files) = await SetUpAsync(("sunset.png", "image/png"));
        using var owner = app;

        var cut = Render<AttachmentGallery>(p => p.Add(g => g.Attachments, files));

        var img = cut.Find("img[alt='sunset.png']");
        Assert.Contains("object-contain", img.GetAttribute("class"));
        Assert.Equal(MediaUrl.For(files[0]), img.GetAttribute("src"));
        Assert.Equal("lazy", img.GetAttribute("loading"));
        cut.Find("button[aria-label='View sunset.png']");
    }

    [Fact]
    public async Task Lays_out_several_images_as_a_grid_of_thumbnails()
    {
        var (app, files) = await SetUpAsync(("a.png", "image/png"), ("b.png", "image/png"));
        using var owner = app;

        var cut = Render<AttachmentGallery>(p => p.Add(g => g.Attachments, files));

        var grid = cut.Find("button[aria-label='View a.png']").ParentElement!;
        Assert.Contains("grid", grid.GetAttribute("class"));
        var img = cut.Find("img[alt='a.png']");
        Assert.Contains("object-cover", img.GetAttribute("class"));
    }

    [Fact]
    public async Task Plays_video_and_audio_with_native_players()
    {
        var (app, files) = await SetUpAsync(("film.mp4", "video/mp4"), ("clip.mp3", "audio/mpeg"));
        using var owner = app;

        var cut = Render<AttachmentGallery>(p => p.Add(g => g.Attachments, files));

        var video = cut.Find("video[aria-label='film.mp4']");
        Assert.Equal(MediaUrl.For(files[0]), video.GetAttribute("src"));
        Assert.Contains("clip.mp3", cut.Markup, StringComparison.Ordinal);
        cut.Find("audio");
    }

    [Fact]
    public async Task Shows_other_files_as_chips_with_an_actions_menu()
    {
        var (app, files) = await SetUpAsync(("report.pdf", "application/pdf"));
        using var owner = app;

        var cut = Render<AttachmentGallery>(p => p.Add(g => g.Attachments, files));

        var trigger = cut.Find("button[aria-label='report.pdf actions']");
        Assert.Contains("report.pdf", trigger.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opens_the_viewer_and_pages_through_a_notes_images()
    {
        var (app, files) = await SetUpAsync(("a.png", "image/png"), ("b.png", "image/png"), ("c.png", "image/png"));
        using var owner = app;
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("showModal", _ => true).SetVoidResult();
        dialogs.SetupVoid("close", _ => true).SetVoidResult();
        var cut = Render<AttachmentGallery>(p => p.Add(g => g.Attachments, files));

        cut.Find("button[aria-label='View b.png']").Click();

        Assert.Contains("2 / 3", cut.Find("dialog h2").TextContent, StringComparison.Ordinal);
        cut.Find("button[aria-label='Next image']").Click();
        Assert.Contains("c.png", cut.Find("dialog h2").TextContent, StringComparison.Ordinal);
    }
}
