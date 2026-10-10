using FalconNotes.Core.Domain;
using FalconNotes.UI.Layout;

namespace FalconNotes.UI.Tests;

/// <summary>New tests (docs/11): the side menu's labels list has no dedicated reference test. Covers the mark on a
/// label that hides its notes (Maple Notes 1.16.0).</summary>
public class LabelLinksTests : BunitContext
{
    [Fact]
    public async Task Marks_the_labels_that_hide_their_notes()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Labels = true });
        var hidden = await app.Core.Labels.CreateAsync("Private");
        await app.Core.Labels.CreateAsync("Work");
        var cut = Render<LabelLinks>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("nav[aria-label=Labels] a").Count));
        Assert.Empty(cut.FindAll("[aria-label='hidden from Home and Quick notes']"));

        await app.Core.Labels.UpdateAsync(hidden.Id, hideNotes: true);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[aria-label='hidden from Home and Quick notes']")));
        var marked = cut.FindAll("nav[aria-label=Labels] a").Single(a => a.QuerySelector("[role=img]") is not null);
        Assert.Contains("Private", marked.TextContent, StringComparison.Ordinal);
    }
}
