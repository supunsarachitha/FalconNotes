using FalconNotes.Core.Domain;
using FalconNotes.UI.Components;

namespace FalconNotes.UI.Tests;

/// <summary>Port of the "Labels on notes" cases of Labels.test.tsx that concern the chips (docs/11); the rest
/// (choosing labels, the Labels page filter) are covered once NoteCard and Home exist in a later step of the phase.</summary>
public class LabelChipsTests : BunitContext
{
    [Fact]
    public async Task Shows_nothing_while_labels_are_turned_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = false });
        var work = await app.Core.Labels.CreateAsync("Work");

        var cut = Render<LabelChips>(p => p.Add(c => c.Ids, [work.Id]));

        Assert.Empty(cut.FindAll("ul"));
    }

    [Fact]
    public async Task Shows_nothing_for_a_note_with_no_labels()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = true });

        var cut = Render<LabelChips>(p => p.Add(c => c.Ids, []));

        Assert.Empty(cut.FindAll("ul"));
    }

    [Fact]
    public async Task Shows_a_notes_labels_as_chips_sorted_by_name_leaving_out_deleted_ones()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = true });
        var work = await app.Core.Labels.CreateAsync("Work");
        var urgent = await app.Core.Labels.CreateAsync("Urgent");

        var cut = Render<LabelChips>(p => p.Add(c => c.Ids, [work.Id, urgent.Id, Guid.NewGuid()]));
        cut.WaitForState(() => cut.FindAll("ul").Count > 0);

        var links = cut.Find("ul[aria-label=Labels]").QuerySelectorAll("a");
        Assert.Equal(["Urgent", "Work"], links.Select(a => a.TextContent.Trim()));
        Assert.Equal($"/?label={Uri.EscapeDataString(urgent.Id.ToString())}", links[0].GetAttribute("href"));
    }
}
