using FalconNotes.Core.Text;
using FalconNotes.UI.Components;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Port of the applicable parts of FormatToolbar.tsx's behaviour (docs/11; most of its 13 Composer cases cover the
/// composer around it, ported with Composer itself). <c>MarkdownEdit.Format</c>'s own rules are already covered by
/// <c>Core.Tests/Text/MarkdownEditTests.cs</c>; this covers the toolbar's wiring: every button present, a click
/// computing and applying the edit through editor.js, and the keyboard shortcuts.
/// </summary>
public class FormatToolbarTests : BunitContext
{
    [Fact]
    public void Shows_every_tool_with_its_shortcut_in_the_title()
    {
        var cut = Render<FormatToolbar>(p => p.Add(t => t.Target, default));

        var buttons = cut.FindAll("button");
        Assert.Equal(8, buttons.Count);
        Assert.Contains(buttons, b => b.GetAttribute("aria-label") == "Bold" && b.GetAttribute("title")!.Contains('B'));
        Assert.Contains(buttons, b => b.GetAttribute("aria-label") == "Link" && b.GetAttribute("title")!.Contains('K'));
        Assert.Contains(buttons, b => b.GetAttribute("aria-label") == "Heading" && !b.GetAttribute("title")!.Contains('('));
    }

    [Fact]
    public void Clicking_bold_reads_the_selection_and_applies_the_computed_edit()
    {
        var module = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        module.Setup<EditorSelection>("getSelection", _ => true).SetResult(new EditorSelection("hello world", 0, 5));
        module.SetupVoid("applyEdit", _ => true).SetVoidResult();
        var cut = Render<FormatToolbar>(p => p.Add(t => t.Target, default));

        cut.Find("button[aria-label=Bold]").Click();

        var expected = MarkdownEdit.Format(MarkdownFormat.Bold, "hello world", 0, 5);
        cut.WaitForAssertion(() => Assert.Single(module.Invocations["applyEdit"]));
        Assert.Equal(expected, module.Invocations["applyEdit"][0].Arguments[1]);
    }

    [Theory]
    [InlineData("b", MarkdownFormat.Bold)]
    [InlineData("i", MarkdownFormat.Italic)]
    [InlineData("k", MarkdownFormat.Link)]
    public void Shortcut_format_matches_the_mod_key_plus_letter(string key, MarkdownFormat expected)
    {
        var modIsMeta = OperatingSystem.IsMacOS() || OperatingSystem.IsIOS();

        var format = FormatToolbar.ShortcutFormat(key, metaKey: modIsMeta, ctrlKey: !modIsMeta, altKey: false, shiftKey: false);

        Assert.Equal(expected, format);
    }

    [Fact]
    public void Shortcut_format_ignores_other_keys_and_modifiers()
    {
        var modIsMeta = OperatingSystem.IsMacOS() || OperatingSystem.IsIOS();

        Assert.Null(FormatToolbar.ShortcutFormat("x", modIsMeta, !modIsMeta, false, false));
        Assert.Null(FormatToolbar.ShortcutFormat("b", metaKey: false, ctrlKey: false, altKey: false, shiftKey: false));
        Assert.Null(FormatToolbar.ShortcutFormat("b", modIsMeta, !modIsMeta, altKey: true, shiftKey: false));
    }
}
