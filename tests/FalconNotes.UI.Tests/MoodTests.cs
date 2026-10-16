using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Components;
using FalconNotes.UI.Pages.Settings;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New tests (docs/11): the mood tracker is this app's own, with no reference to port from (docs/07, Moods). Covers
/// the switch, the smilies where a note is written, and the smiley on the note.
/// </summary>
public class MoodTests : BunitContext
{
    private static readonly Preferences MoodsOn = new() { MoodTracker = true };

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
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<Composer> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text);

    private static AngleSharp.Dom.IElement MoodButton(IRenderedComponent<Composer> cut) => cut.Find("button[aria-controls$='-mood']");

    private static void Choose(IRenderedComponent<Composer> cut, string mood)
    {
        MoodButton(cut).Click();
        cut.Find($"[role=group][aria-label=Mood] button[aria-label='{mood}']").Click();
    }

    private static async Task<Note> OnlyNoteAsync(UiTestApp app, NoteKind kind = NoteKind.Note) =>
        (await app.Core.Notes.ListAsync(new NoteQuery(NoteState.Feed, [kind]))).Items.Single();

    [Fact]
    public async Task The_switch_is_off_as_installed_and_turns_the_mood_tracker_on()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<FeaturesSection>();
        var toggle = cut.Find("button[role=switch][aria-label='Mood tracker']");
        Assert.Equal("false", toggle.GetAttribute("aria-checked"));

        toggle.Click();

        cut.WaitForAssertion(() => Assert.True(app.State.Preferences.MoodTracker));
        Assert.Contains("the moods you chose are kept", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task There_are_no_smilies_where_a_note_is_written_until_the_mood_tracker_is_on()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();

        var cut = Render<Composer>();

        Assert.Empty(cut.FindAll("button[aria-controls$='-mood']"));
        Assert.Empty(cut.FindAll("[role=group][aria-label=Mood]"));
    }

    [Fact]
    public async Task The_smiley_button_opens_five_smilies_worst_to_best_and_shows_the_one_chosen()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", MoodsOn);
        SetUpJs();
        var cut = Render<Composer>();
        Assert.Equal(("Mood", "false"), (MoodButton(cut).GetAttribute("aria-label"), MoodButton(cut).GetAttribute("aria-expanded")));
        Assert.Empty(cut.FindAll("[role=group][aria-label=Mood]")); // closed until asked for

        MoodButton(cut).Click();

        Assert.Equal("true", MoodButton(cut).GetAttribute("aria-expanded"));
        Assert.Equal(MoodButton(cut).GetAttribute("aria-controls"), cut.Find("[role=group][aria-label=Mood]").Id);
        var smilies = cut.FindAll("[role=group][aria-label=Mood] button");
        Assert.Equal(
            [("Awful", "😢"), ("Bad", "🙁"), ("Okay", "😐"), ("Good", "🙂"), ("Great", "😄")],
            smilies.Select(b => (b.GetAttribute("aria-label")!, b.TextContent.Trim())));
        Assert.All(smilies, b => Assert.Equal("false", b.GetAttribute("aria-pressed")));

        cut.Find("[role=group][aria-label=Mood] button[aria-label='Good']").Click();

        Assert.Empty(cut.FindAll("[role=group][aria-label=Mood]")); // chosen, so the row closes
        Assert.Equal(("Mood: Good", "🙂"), (MoodButton(cut).GetAttribute("aria-label"), MoodButton(cut).TextContent.Trim()));

        MoodButton(cut).Click();

        Assert.Equal("true", cut.Find("[role=group][aria-label=Mood] button[aria-label='Good']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Choosing_the_chosen_smiley_again_takes_the_mood_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", MoodsOn);
        SetUpJs();
        var cut = Render<Composer>();
        Choose(cut, "Bad");

        Choose(cut, "Bad");

        Assert.Equal("Mood", MoodButton(cut).GetAttribute("aria-label"));
        Assert.Empty(MoodButton(cut).TextContent.Trim());
    }

    [Theory]
    [InlineData(NoteKind.Note)]
    [InlineData(NoteKind.Quick)]
    public async Task A_note_is_posted_with_the_mood_chosen_and_the_next_one_starts_without(NoteKind kind)
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", MoodsOn);
        SetUpJs();
        var cut = Render<Composer>(p => p.Add(c => c.Kind, kind));
        cut.Find("#composer").Input("passed the exam");
        Choose(cut, "Great");

        Button(cut, "Post").Click();

        // Only a DOM check while waiting (see ComposerTests): the composer clears itself once the note is saved.
        cut.WaitForAssertion(() => Assert.Equal("", cut.Find("#composer").GetAttribute("value")));
        Assert.Equal(("passed the exam", NoteMood.Great), ((await OnlyNoteAsync(app, kind)).Content, (await OnlyNoteAsync(app, kind)).Mood));
        Assert.Equal("Mood", MoodButton(cut).GetAttribute("aria-label"));
    }

    [Fact]
    public async Task A_mood_alone_is_not_enough_to_post()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", MoodsOn);
        SetUpJs();
        var cut = Render<Composer>();

        Choose(cut, "Okay");

        Assert.True(Button(cut, "Post").HasAttribute("disabled"));
    }

    [Fact]
    public async Task The_daily_note_is_started_with_the_mood_chosen()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", MoodsOn);
        SetUpJs();
        var day = new DateOnly(2026, 10, 13);
        var cut = Render<Composer>(p => p.Add(c => c.Daily, new Composer.DailyInfo(day, "2026-10-13")));
        cut.Find("#composer").Input("slept well");
        Choose(cut, "Good");

        Button(cut, "Post").Click();

        cut.WaitForAssertion(() => Assert.Equal("", cut.Find("#composer").GetAttribute("value")));
        Assert.Equal(NoteMood.Good, (await app.Core.Notes.GetDailyAsync(day))!.Mood);
    }

    [Fact]
    public async Task Editing_shows_the_notes_mood_and_changing_it_alone_does_not_make_the_note_edited()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", MoodsOn);
        SetUpJs();
        var note = await app.Core.Notes.CreateAsync("a long day", mood: NoteMood.Bad);
        app.Core.Clock.Advance(TimeSpan.FromHours(2));
        var done = false;
        var cut = Render<Composer>(p => p.Add(c => c.Note, note).Add(c => c.OnDone, () => done = true));
        Assert.Equal("Mood: Bad", MoodButton(cut).GetAttribute("aria-label"));

        Choose(cut, "Okay");
        Button(cut, "Save").Click();

        cut.WaitForAssertion(() => Assert.True(done));
        var saved = (await app.Core.Notes.GetAsync(note.Id))!;
        Assert.Equal(("a long day", NoteMood.Okay, note.UpdatedAtUtc), (saved.Content, saved.Mood, saved.UpdatedAtUtc));
    }

    [Fact]
    public async Task An_edit_saves_the_text_and_the_mood_together()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", MoodsOn);
        SetUpJs();
        var note = await app.Core.Notes.CreateAsync("a long day", mood: NoteMood.Bad);
        app.Core.Clock.Advance(TimeSpan.FromHours(2));
        var done = false;
        var cut = Render<Composer>(p => p.Add(c => c.Note, note).Add(c => c.OnDone, () => done = true));

        cut.Find($"#edit-{note.Id}").Input("a long day, but it ended well");
        Choose(cut, "Good");
        Button(cut, "Save").Click();

        cut.WaitForAssertion(() => Assert.True(done));
        var saved = (await app.Core.Notes.GetAsync(note.Id))!;
        Assert.Equal(("a long day, but it ended well", NoteMood.Good), (saved.Content, saved.Mood));
        Assert.True(saved.UpdatedAtUtc > note.UpdatedAtUtc);
    }

    [Fact]
    public async Task With_the_mood_tracker_off_an_edit_keeps_the_mood_the_note_has()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var note = await app.Core.Notes.CreateAsync("a long day", mood: NoteMood.Bad);
        var done = false;
        var cut = Render<Composer>(p => p.Add(c => c.Note, note).Add(c => c.OnDone, () => done = true));
        Assert.Empty(cut.FindAll("button[aria-controls$='-mood']"));

        cut.Find($"#edit-{note.Id}").Input("a long day indeed");
        Button(cut, "Save").Click();

        cut.WaitForAssertion(() => Assert.True(done));
        Assert.Equal(NoteMood.Bad, (await app.Core.Notes.GetAsync(note.Id))!.Mood);
    }

    [Fact]
    public async Task A_todo_list_edited_as_a_note_is_offered_no_smilies()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", MoodsOn);
        SetUpJs();
        var list = await app.PostAsync("# Groceries\n\n- [ ] oats", NoteKind.Todo);

        var cut = Render<Composer>(p => p.Add(c => c.Note, list));

        Assert.Empty(cut.FindAll("button[aria-controls$='-mood']"));
    }

    [Fact]
    public async Task A_note_shows_its_mood_as_a_smiley_with_its_name()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", MoodsOn);
        SetUpJs();
        var note = await app.Core.Notes.CreateAsync("passed the exam", mood: NoteMood.Great);
        var plain = await app.PostAsync("nothing special");

        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));
        var without = Render<NoteCard>(p => p.Add(c => c.Note, plain));

        var smiley = cut.Find("header [role=img][aria-label^='Mood']");
        Assert.Equal(("Mood: Great", "Mood: Great", "😄"), (smiley.GetAttribute("aria-label"), smiley.GetAttribute("title"), smiley.TextContent.Trim()));
        Assert.Empty(without.FindAll("[role=img][aria-label^='Mood']"));
    }

    [Fact]
    public async Task With_the_mood_tracker_off_a_note_shows_no_smiley_and_keeps_its_mood()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        SetUpJs();
        var note = await app.Core.Notes.CreateAsync("passed the exam", mood: NoteMood.Great);

        var cut = Render<NoteCard>(p => p.Add(c => c.Note, note));

        Assert.Empty(cut.FindAll("[role=img][aria-label^='Mood']"));
        Assert.DoesNotContain("😄", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(NoteMood.Great, (await app.Core.Notes.GetAsync(note.Id))!.Mood);
    }

    [Fact]
    public async Task A_locked_note_hides_its_mood_with_everything_else()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", MoodsOn);
        SetUpJs();
        var note = await app.Core.Notes.CreateAsync("the worst news", mood: NoteMood.Awful);
        var locked = (await app.Core.Notes.PatchAsync(note.Id, new NotePatch(IsLocked: true)))!;

        var cut = Render<NoteCard>(p => p.Add(c => c.Note, locked));

        Assert.Contains("This note is locked.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[role=img][aria-label^='Mood']"));
        Assert.DoesNotContain("😢", cut.Markup, StringComparison.Ordinal);
    }
}
