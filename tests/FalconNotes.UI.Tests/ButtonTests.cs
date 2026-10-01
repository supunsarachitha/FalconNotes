using FalconNotes.UI.Components.Ui;

namespace FalconNotes.UI.Tests;

public class ButtonTests : BunitContext
{
    [Fact]
    public void Primary_button_has_the_reference_classes()
    {
        var button = Render<Button>(p => p.AddChildContent("Post")).Find("button");

        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal(
            "inline-flex h-10 items-center justify-center gap-2 rounded-full px-4 text-sm font-medium transition-colors " +
            "disabled:cursor-not-allowed disabled:opacity-50 " +
            "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-maple-500 " +
            "bg-maple-600 text-white hover:bg-maple-700",
            button.GetAttribute("class"));
        Assert.Equal("Post", button.TextContent.Trim());
    }

    [Fact]
    public void Busy_button_is_disabled_marked_busy_and_shows_a_spinner()
    {
        var button = Render<Button>(p => p.Add(b => b.Busy, true).AddChildContent("Saving")).Find("button");

        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.NotNull(button.QuerySelector("svg.animate-spin"));
    }

    [Fact]
    public void Idle_button_has_no_aria_busy_and_passes_extra_attributes()
    {
        var button = Render<Button>(p => p.Add(b => b.Variant, ButtonVariant.Danger)
            .AddUnmatched("aria-label", "Delete")).Find("button");

        Assert.False(button.HasAttribute("aria-busy"));
        Assert.False(button.HasAttribute("disabled"));
        Assert.Equal("Delete", button.GetAttribute("aria-label"));
        Assert.Contains("bg-red-700", button.GetAttribute("class"));
    }

    [Fact]
    public void Click_calls_the_handler()
    {
        var clicks = 0;
        var cut = Render<Button>(p => p.Add(b => b.OnClick, () => clicks++));

        cut.Find("button").Click();

        Assert.Equal(1, clicks);
    }
}
