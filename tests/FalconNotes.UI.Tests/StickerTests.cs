using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Platform;
using FalconNotes.Core.Tests.Stickers;
using FalconNotes.UI.Components;
using FalconNotes.UI.Pages.Settings;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New tests (docs/11): stickers are this app's own, with no reference to port from (docs/07, Stickers). Covers the
/// switch and the packs under it, the sticker picker where a note is written, and the stickers on the note.
/// </summary>
public class StickerTests : BunitContext
{
    private static readonly Preferences StickersOn = new() { Stickers = true };

    private void SetUpJs()
    {
        var editor = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        editor.SetupVoid("bindAutoGrow", _ => true).SetVoidResult();
        editor.SetupVoid("growNow", _ => true).SetVoidResult();
        editor.SetupVoid("focusAtEnd", _ => true).SetVoidResult();
        editor.SetupVoid("focus", _ => true).SetVoidResult();
        var gestures = JSInterop.SetupModule("./_content/FalconNotes.UI/js/gestures.js");
        gestures.SetupVoid("bindDoubleTap", _ => true).SetVoidResult();
        gestures.SetupVoid("unbindDoubleTap", _ => true).SetVoidResult();
        var markdown = JSInterop.SetupModule("./_content/FalconNotes.UI/js/markdown.js");
        markdown.SetupVoid("bindTaskToggle", _ => true).SetVoidResult();
        markdown.SetupVoid("unbindTaskToggle", _ => true).SetVoidResult();
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.Mode = JSRuntimeMode.Loose;
    }

    private static Task AddPackAsync(UiTestApp app, string fileName, params (string Name, byte[] Bytes)[] entries) =>
        app.Core.Stickers.AddAsync(new MemoryStream(StickerPackServiceTests.Zip(entries)), fileName);

    private static PickedFile Zip(string fileName, params (string Name, byte[] Bytes)[] entries)
    {
        var bytes = StickerPackServiceTests.Zip(entries);
        return new PickedFile(fileName, "application/zip", () => Task.FromResult<Stream>(new MemoryStream(bytes)));
    }

    private static AngleSharp.Dom.IElement StickerButton(IRenderedComponent<Composer> cut) => cut.Find("button[aria-controls$='-stickers']");

    private static async Task<Note> OnlyNoteAsync(UiTestApp app, NoteKind kind = NoteKind.Note) =>
        (await app.Core.Notes.ListAsync(new NoteQuery(NoteState.Feed, [kind]))).Items.Single();

    [Fact]
    public async Task The_switch_is_off_as_installed_and_turns_stickers_on_with_the_packs_under_it()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var cut = Render<FeaturesSection>();
        var toggle = cut.Find("button[role=switch][aria-label='Stickers']");
        Assert.Equal("false", toggle.GetAttribute("aria-checked"));
        Assert.DoesNotContain("Sticker packs", cut.Markup, StringComparison.Ordinal);

        toggle.Click();

        cut.WaitForAssertion(() => Assert.True(app.State.Preferences.Stickers));
        cut.WaitForAssertion(() => Assert.Contains("No sticker packs yet.", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("stickers already on your notes stay", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pack_is_added_from_a_zip_file_and_listed_with_its_details()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", StickersOn);
        SetUpJs();
        var cut = Render<StickerPacksSetting>();
        app.Picker.Enqueue(Zip("Cats.zip",
            ("sticker-pack.json", """{"name":"Happy cats","author":"Mina","licence":"CC0-1.0"}"""u8.ToArray()),
            ("a.png", StickerPackServiceTests.Png()), ("b.gif", StickerPackServiceTests.Gif())));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Add pack…").Click();

        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.Message == "Added “Happy cats”: 2 stickers."));
        cut.WaitForAssertion(() => Assert.Contains("2 stickers · by Mina · CC0-1.0", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("/_stickers/happy-cats/001.png?v=" + (await app.Core.Stickers.ListAsync()).Single().Added,
            cut.Find("ul[aria-label='Sticker packs'] img").GetAttribute("src"));
    }

    [Fact]
    public async Task A_file_that_is_not_a_pack_is_refused_with_the_reason()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", StickersOn);
        SetUpJs();
        var cut = Render<StickerPacksSetting>();
        app.Picker.Enqueue(Zip("notes.zip", ("notes.txt", "hello"u8.ToArray())));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Add pack…").Click();

        cut.WaitForAssertion(() => Assert.Contains(app.Toasts.Current, t => t.IsError && t.Message == "notes.zip: This sticker pack has no pictures in it."));
        Assert.Empty(await app.Core.Stickers.ListAsync());
    }

    [Fact]
    public async Task Removing_a_pack_asks_first_and_leaves_the_stickers_on_notes()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", StickersOn);
        SetUpJs();
        await AddPackAsync(app, "cats.zip", ("a.png", StickerPackServiceTests.Png()));
        var sticker = await app.Core.Attachments.AddAsync(new MemoryStream(StickerPackServiceTests.Png()), "a.png", "image/png", cancellationToken: Xunit.TestContext.Current.CancellationToken, sticker: true);
        var note = await app.Core.Notes.CreateAsync("hi", attachmentIds: [sticker.Id]);
        var cut = Render<StickerPacksSetting>();

        cut.WaitForElement("button[aria-label='Remove cats']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Stickers already on your notes stay where they are.", cut.Markup, StringComparison.Ordinal));
        cut.FindAll("dialog button").First(b => b.TextContent.Trim() == "Remove").Click();

        cut.WaitForAssertion(() => Assert.Contains("No sticker packs yet.", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(await app.Core.Stickers.ListAsync());
        Assert.True((await app.Core.Notes.GetAsync(note.Id))!.Attachments.Single().IsSticker);
    }

    [Fact]
    public async Task There_is_no_sticker_button_until_stickers_are_on()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();

        var cut = Render<Composer>();

        Assert.Empty(cut.FindAll("button[aria-controls$='-stickers']"));
    }

    [Fact]
    public async Task With_no_pack_the_picker_says_where_to_add_one()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", StickersOn);
        SetUpJs();
        var cut = Render<Composer>();

        StickerButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("No sticker packs yet. Add one in", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("/settings/features", cut.Find("[role=group][aria-label=Stickers] a").GetAttribute("href"));
    }

    [Fact]
    public async Task The_picker_has_a_tab_for_each_pack()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", StickersOn);
        SetUpJs();
        await AddPackAsync(app, "cats.zip", ("wave.png", StickerPackServiceTests.Png()), ("nap.png", StickerPackServiceTests.Png()));
        await AddPackAsync(app, "birds.zip", ("tweet.gif", StickerPackServiceTests.Gif()));
        var cut = Render<Composer>();

        StickerButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Equal(["Add sticker: tweet"], cut.FindAll("[role=group][aria-label=Stickers] li button").Select(b => b.GetAttribute("aria-label"))));
        cut.FindAll("[aria-label='Sticker packs'] button").Single(b => b.TextContent.Trim() == "cats").Click();
        cut.WaitForAssertion(() => Assert.Equal(
            ["Add sticker: nap", "Add sticker: wave"], cut.FindAll("[role=group][aria-label=Stickers] li button").Select(b => b.GetAttribute("aria-label"))));
    }

    [Theory]
    [InlineData(NoteKind.Note)]
    [InlineData(NoteKind.Quick)]
    public async Task Stickers_chosen_are_posted_as_the_notes_own_sticker_files(NoteKind kind)
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", StickersOn);
        SetUpJs();
        await AddPackAsync(app, "cats.zip", ("wave.png", StickerPackServiceTests.Png(1)), ("nap.gif", StickerPackServiceTests.Gif()));
        var cut = Render<Composer>(p => p.Add(c => c.Kind, kind));
        StickerButton(cut).Click();

        cut.WaitForElement("button[aria-label='Add sticker: wave']").Click();
        cut.Find("button[aria-label='Add sticker: nap']").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("ul[aria-label='Attached files'] img").Count));
        Assert.NotEmpty(cut.FindAll("[role=group][aria-label=Stickers]")); // stays open for more
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Post").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("ul[aria-label='Attached files']")));
        var note = await OnlyNoteAsync(app, kind);
        Assert.Equal([("nap.gif", "image/gif", true), ("wave.png", "image/png", true)],
            note.Attachments.Select(a => (a.FileName, a.ContentType, a.IsSticker)).Order());
        Assert.Empty(cut.FindAll("[role=group][aria-label=Stickers]"));
    }

    [Fact]
    public async Task Opening_the_stickers_closes_the_smilies_and_back()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Stickers = true, MoodTracker = true });
        SetUpJs();
        var cut = Render<Composer>();

        cut.Find("button[aria-controls$='-mood']").Click();
        StickerButton(cut).Click();

        Assert.Empty(cut.FindAll("[role=group][aria-label=Mood]"));
        Assert.NotEmpty(cut.FindAll("[role=group][aria-label=Stickers]"));

        cut.Find("button[aria-controls$='-mood']").Click();

        Assert.Empty(cut.FindAll("[role=group][aria-label=Stickers]"));
    }

    [Fact]
    public async Task Stickers_show_in_a_row_of_their_own_and_stay_when_stickers_are_turned_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var photo = await app.Core.AddFileAsync("beach.png");
        var sticker = await app.Core.Attachments.AddAsync(new MemoryStream(StickerPackServiceTests.Png()), "Waving cat.png", "image/png", cancellationToken: Xunit.TestContext.Current.CancellationToken, sticker: true);

        IReadOnlyList<Attachment> files = [photo, sticker];

        var cut = Render<AttachmentGallery>(p => p.Add(g => g.Attachments, files));

        var shown = cut.Find("ul[aria-label=Stickers] img");
        Assert.Equal(($"/_media/{sticker.Id:D}?v=0", "Waving cat"), (shown.GetAttribute("src"), shown.GetAttribute("alt")));
        Assert.Equal(["View beach.png"], cut.FindAll("button[aria-label^='View']").Select(b => b.GetAttribute("aria-label")));
    }
}
