using FalconNotes.Core.Attachments;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Platform;
using FalconNotes.Core.Text;
using FalconNotes.UI.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Port of Composer.test.tsx (docs/11), adapted for the native file picker instead of a hidden HTML input. One
/// reference case does not port: "shows the server's validation message when saving fails" relies on the client and
/// server validating independently and briefly disagreeing; here <see cref="Composer"/>'s own CanSave already
/// enforces the same rule <c>NoteService</c> would (docs/04), so that path is not reachable from the UI — a
/// stronger guarantee than the thing the reference test checks.
/// </summary>
public class ComposerTests : BunitContext
{
    private EditorJsStub SetUpEditorJs()
    {
        var module = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        module.SetupVoid("bindAutoGrow", _ => true).SetVoidResult();
        module.SetupVoid("growNow", _ => true).SetVoidResult();
        module.SetupVoid("focusAtEnd", _ => true).SetVoidResult();
        module.SetupVoid("focus", _ => true).SetVoidResult();
        module.SetupVoid("positionPopup", _ => true).SetVoidResult();
        module.SetupVoid("setSelection", _ => true).SetVoidResult();
        module.SetupVoid("applyEdit", _ => true).SetVoidResult();
        return new EditorJsStub(module);
    }

    /// <summary>Stands in for the text box: tells the mocked editor.js module what <c>getSelection</c> should answer.</summary>
    private sealed class EditorJsStub(Bunit.BunitJSModuleInterop module)
    {
        public Bunit.BunitJSModuleInterop Module => module;

        public void Selection(string value, int start, int end) =>
            module.Setup<EditorSelection>("getSelection", _ => true).SetResult(new EditorSelection(value, start, end));
    }

    private static AngleSharp.Dom.IElement PostButton(IRenderedComponent<Composer> cut) => Button(cut, "Post");

    private static AngleSharp.Dom.IElement SaveButton(IRenderedComponent<Composer> cut) => Button(cut, "Save");

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<Composer> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text);

    private static Task<IReadOnlyList<Note>> ActiveNotesAsync(UiTestApp app) =>
        app.Core.Notes.ListAsync(new NoteQuery(NoteState.Feed, [NoteKind.Note])).ContinueWith(t => t.Result.Items);

    [Fact]
    public async Task Has_no_title_field_unless_note_titles_are_on()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpEditorJs();

        var cut = Render<Composer>();

        Assert.Empty(cut.FindAll("input[type=text]"));
    }

    [Fact]
    public async Task Cannot_post_an_empty_note()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpEditorJs();

        var cut = Render<Composer>();

        Assert.True(PostButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Posts_with_ctrl_enter_and_clears_itself()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpEditorJs();
        var cut = Render<Composer>();
        var box = cut.Find("#composer");

        box.Input("hello #world");
        box.KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        // Only a DOM check here: a blocking DB call inside WaitForAssertion's predicate would deadlock the
        // renderer's own dispatcher thread, which the pending save itself needs in order to complete.
        cut.WaitForAssertion(() => Assert.Equal("", cut.Find("#composer").GetAttribute("value")));
        var note = (await ActiveNotesAsync(app)).Single();
        Assert.Equal("hello #world", note.Content);
    }

    [Fact]
    public async Task Edits_an_existing_note_and_reports_completion()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var created = await app.PostAsync("hello");
        SetUpEditorJs();
        var done = false;
        var cut = Render<Composer>(p => p
            .Add(c => c.Note, created)
            .Add(c => c.OnDone, () => done = true));
        var box = cut.Find($"#edit-{created.Id}");

        box.Input("hello again");
        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.True(done));
        Assert.Equal("hello again", (await app.Core.Notes.GetAsync(created.Id))!.Content);
    }

    [Fact]
    public async Task Has_a_title_field_when_note_titles_are_on_and_saves_it_as_a_heading()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { NoteTitles = true });
        SetUpEditorJs();
        var cut = Render<Composer>();

        cut.Find("input[aria-label=Title]").Input("Weekend plans");
        cut.Find("#composer").Input("Hike the #trail");
        PostButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Equal("", cut.Find("#composer").GetAttribute("value")));
        var note = (await ActiveNotesAsync(app)).Single();
        Assert.Equal("# Weekend plans\n\nHike the #trail", note.Content);
    }

    [Fact]
    public async Task Starts_the_title_with_todays_date_which_alone_is_not_enough_to_post()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences
        {
            NoteTitles = true, DateInTitles = true, DateFormat = "yyyy-MM-dd",
        });
        SetUpEditorJs();
        var cut = Render<Composer>();
        var today = DateOnly.FromDateTime(app.Core.Clock.Now.UtcDateTime).ToString("yyyy-MM-dd");

        var title = cut.Find("input[aria-label=Title]");
        Assert.Equal(today, title.GetAttribute("value"));
        Assert.True(PostButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Edits_a_notes_title_and_text_separately()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { NoteTitles = true });
        var created = await app.PostAsync("# Groceries\n\n- [ ] oats");
        SetUpEditorJs();
        var done = false;
        var cut = Render<Composer>(p => p
            .Add(c => c.Note, created)
            .Add(c => c.OnDone, () => done = true));

        var title = cut.Find("input[aria-label='Edit title']");
        Assert.Equal("Groceries", title.GetAttribute("value"));
        Assert.Equal("- [ ] oats", cut.Find($"#edit-{created.Id}").GetAttribute("value"));

        title.Input("Weekend groceries");
        SaveButton(cut).Click();

        cut.WaitForAssertion(() => Assert.True(done));
        Assert.Equal("# Weekend groceries\n\n- [ ] oats", (await app.Core.Notes.GetAsync(created.Id))!.Content);
    }

    [Fact]
    public async Task Suggests_nothing_while_the_setting_is_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.PostAsync("#garden notes");
        var js = SetUpEditorJs();
        js.Selection("#g", 2, 2);
        var cut = Render<Composer>();

        cut.Find("#composer").Input("#g");

        Assert.Empty(cut.FindAll("ul[aria-label='Tag suggestions']"));
    }

    [Fact]
    public async Task Suggests_existing_tags_while_a_tag_is_typed_and_enter_chooses_one()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { TagSuggestions = true });
        await app.PostAsync("#garden notes");
        await app.PostAsync("#groceries list");
        var js = SetUpEditorJs();
        js.Selection("Buy #g", 6, 6);
        var cut = Render<Composer>();
        var box = cut.Find("#composer");

        // Retried: TagSuggestions loads its tag list in the background, after Composer's own render settles.
        cut.WaitForAssertion(() =>
        {
            box.Input("Buy #g");
            Assert.NotEmpty(cut.FindAll("ul[aria-label='Tag suggestions']"));
        });
        var list = cut.Find("ul[aria-label='Tag suggestions']");
        Assert.Equal(2, list.QuerySelectorAll("li[role=option]").Length);

        box.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        box.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("ul[aria-label='Tag suggestions']")));
    }

    [Fact]
    public async Task Formats_the_selection_from_the_toolbar()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var js = SetUpEditorJs();
        js.Selection("Buy maple syrup", 4, 9); // "maple"
        var cut = Render<Composer>();
        cut.Find("#composer").Input("Buy maple syrup");

        cut.Find("button[aria-label=Bold]").Click();

        cut.WaitForAssertion(() => Assert.Single(js.Module.Invocations["applyEdit"]));
        var edit = Assert.IsType<TextEdit>(js.Module.Invocations["applyEdit"][0].Arguments[1]);
        Assert.Equal("**maple**", edit.Insert);
    }

    // The next three are not in Composer.test.tsx: the web app shrinks in the browser, inside its upload call. Here
    // the composer itself asks PhotoShrinker, so the switch and the missing codec are checked where they are wired.

    private static PickedFile Photo(string name, int size) =>
        new(name, "image/png", () => Task.FromResult<Stream>(new MemoryStream(new byte[size])));

    private async Task<Attachment> AttachAndPostAsync(UiTestApp app, IRenderedComponent<Composer> cut, PickedFile file, string shownAs)
    {
        app.Picker.Enqueue(file);
        cut.Find("button[aria-label='Attach files']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"img[alt='{shownAs}']")));

        cut.Find("#composer").Input("With a photo");
        PostButton(cut).Click();
        cut.WaitForAssertion(() => Assert.Equal("", cut.Find("#composer").GetAttribute("value")));
        return (await ActiveNotesAsync(app)).Single().Attachments.Single();
    }

    [Fact]
    public async Task Shrinks_a_photo_before_adding_it_when_the_setting_is_on()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { ShrinkPhotos = true });
        Services.AddSingleton(new PhotoShrinker(new FakeImageCodec(jpegSize: 100)));
        SetUpEditorJs();
        var cut = Render<Composer>();

        var added = await AttachAndPostAsync(app, cut, Photo("holiday.png", 1000), "holiday.jpg");

        Assert.Equal(("holiday.jpg", "image/jpeg", 100), (added.FileName, added.ContentType, added.SizeBytes));
    }

    [Fact]
    public async Task Adds_a_photo_as_it_is_while_the_setting_is_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var codec = new FakeImageCodec(jpegSize: 100);
        Services.AddSingleton(new PhotoShrinker(codec));
        SetUpEditorJs();
        var cut = Render<Composer>();

        var added = await AttachAndPostAsync(app, cut, Photo("holiday.png", 1000), "holiday.png");

        Assert.Equal(("holiday.png", "image/png", 1000), (added.FileName, added.ContentType, added.SizeBytes));
        Assert.Equal(0, codec.Calls);
    }

    [Fact]
    public async Task Adds_a_photo_as_it_is_on_a_platform_with_no_image_codec()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { ShrinkPhotos = true });
        SetUpEditorJs();
        var cut = Render<Composer>();

        var added = await AttachAndPostAsync(app, cut, Photo("holiday.png", 1000), "holiday.png");

        Assert.Equal(("holiday.png", "image/png", 1000), (added.FileName, added.ContentType, added.SizeBytes));
    }

    /// <summary>Answers with a JPEG of a given size, without decoding anything.</summary>
    private sealed class FakeImageCodec(int jpegSize) : IImageCodec
    {
        public int Calls { get; private set; }

        public Task<EncodedImage?> EncodeJpegAsync(Stream source, Func<int, int, (int Width, int Height)> fit, int quality, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<EncodedImage?>(new EncodedImage(new byte[jpegSize], false));
        }
    }
}
