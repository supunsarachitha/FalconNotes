using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.UI.Components;

namespace FalconNotes.UI.Tests;

/// <summary>Port of the "chooses a note's labels" cases of Labels.test.tsx (docs/11), against the picker directly
/// rather than through NoteCard's menu, which is built in a later step of the phase.</summary>
public class LabelPickerTests : BunitContext
{
    private static bool IsChecked(IRenderedComponent<LabelPicker> cut, string label) =>
        cut.FindAll("label").First(l => l.TextContent.Contains(label, StringComparison.Ordinal))
            .QuerySelector("input[type=checkbox]")!.HasAttribute("checked");

    private static void Toggle(IRenderedComponent<LabelPicker> cut, string label) =>
        cut.FindAll("label").First(l => l.TextContent.Contains(label, StringComparison.Ordinal))
            .QuerySelector("input[type=checkbox]")!.Change(true);

    private void SetUpDialogsJs()
    {
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("showModal", _ => true);
        dialogs.SetupVoid("close", _ => true);
    }

    [Fact]
    public async Task Shows_the_notes_current_labels_checked()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var work = await app.Core.Labels.CreateAsync("Work");
        await app.Core.Labels.CreateAsync("Urgent");
        var created = await app.PostAsync("Quarterly report");
        var note = (await app.Core.Notes.PatchAsync(created.Id, new NotePatch(LabelIds: [work.Id])))!;

        SetUpDialogsJs();
        var cut = Render<LabelPicker>(p => p.Add(x => x.Note, note).Add(x => x.Open, true));
        cut.WaitForState(() => cut.FindAll("label").Count == 2);

        Assert.True(IsChecked(cut, "Work"));
        Assert.False(IsChecked(cut, "Urgent"));
    }

    [Fact]
    public async Task Filters_the_list_as_typed_and_offers_no_second_label_of_a_name_already_taken()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.Core.Labels.CreateAsync("Work");
        var note = await app.PostAsync("Quarterly report");
        SetUpDialogsJs();
        var cut = Render<LabelPicker>(p => p.Add(x => x.Note, note).Add(x => x.Open, true));
        cut.WaitForState(() => cut.FindAll("label").Count == 1);

        cut.Find("input[aria-label='Find or create a label']").Input(" work ");

        Assert.Single(cut.FindAll("label"));
        Assert.DoesNotContain(cut.FindAll("button[type=button]"), b => b.TextContent.Contains("Create label", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Creates_a_label_on_the_way_and_selects_it()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var note = await app.PostAsync("Quarterly report");
        SetUpDialogsJs();
        var cut = Render<LabelPicker>(p => p.Add(x => x.Note, note).Add(x => x.Open, true));
        cut.WaitForState(() => !cut.Markup.Contains("Loading labels", StringComparison.Ordinal));

        cut.Find("input[aria-label='Find or create a label']").Input("Travel");
        cut.WaitForState(() => cut.FindAll("button").Any(b => b.TextContent.Contains("Create label", StringComparison.Ordinal)));
        cut.FindAll("button").First(b => b.TextContent.Contains("Create label", StringComparison.Ordinal)).Click();

        cut.WaitForState(() => cut.FindAll("label").Count == 1);
        Assert.True(IsChecked(cut, "Travel"));
    }

    [Fact]
    public async Task Save_with_the_same_selection_closes_without_changing_the_note()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var work = await app.Core.Labels.CreateAsync("Work");
        var created = await app.PostAsync("Quarterly report");
        var note = (await app.Core.Notes.PatchAsync(created.Id, new NotePatch(LabelIds: [work.Id])))!;
        var closed = false;
        SetUpDialogsJs();
        var cut = Render<LabelPicker>(p => p
            .Add(x => x.Note, note)
            .Add(x => x.Open, true)
            .Add(x => x.OpenChanged, open => closed = !open));
        cut.WaitForState(() => cut.FindAll("label").Count == 1);

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();

        cut.WaitForState(() => closed);
        Assert.Equal([work.Id], (await app.Core.Notes.GetAsync(note.Id))!.LabelIds);
    }

    [Fact]
    public async Task Save_with_a_changed_selection_patches_the_note_in_the_labels_name_order()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var work = await app.Core.Labels.CreateAsync("Work");
        var urgent = await app.Core.Labels.CreateAsync("Urgent");
        var created = await app.PostAsync("Quarterly report");
        var note = (await app.Core.Notes.PatchAsync(created.Id, new NotePatch(LabelIds: [work.Id])))!;
        var closed = false;
        SetUpDialogsJs();
        var cut = Render<LabelPicker>(p => p
            .Add(x => x.Note, note)
            .Add(x => x.Open, true)
            .Add(x => x.OpenChanged, open => closed = !open));
        cut.WaitForState(() => cut.FindAll("label").Count == 2);

        Toggle(cut, "Work"); // off
        Toggle(cut, "Urgent"); // on
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();

        cut.WaitForState(() => closed); // only a plain flag: a blocking DB call here would deadlock the renderer thread
        Assert.Equal([urgent.Id], (await app.Core.Notes.GetAsync(note.Id))!.LabelIds);
    }
}
