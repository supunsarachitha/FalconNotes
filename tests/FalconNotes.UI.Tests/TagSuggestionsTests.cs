using FalconNotes.UI.Components;
using Microsoft.AspNetCore.Components;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Port of the applicable parts of TagSuggestions.tsx's behaviour (docs/11; covered as part of Composer's 13 cases
/// in the reference). <c>TagSuggest</c>'s own matching rules are already covered by
/// <c>Core.Tests/Text/TagSuggestTests.cs</c>; this covers the component's wiring: showing, moving through and
/// choosing a suggestion, driven the way Composer drives it (UpdateAsync/OnKeyDownAsync), since bUnit cannot type
/// into a text box outside this component's own render tree.
/// </summary>
public class TagSuggestionsTests : BunitContext
{
    private EditorJsStub SetUpEditorJs()
    {
        var module = JSInterop.SetupModule("./_content/FalconNotes.UI/js/editor.js");
        module.SetupVoid("positionPopup", _ => true).SetVoidResult();
        module.SetupVoid("setSelection", _ => true).SetVoidResult();
        return new EditorJsStub(module);
    }

    /// <summary>Stands in for the text box: tells the mocked editor.js module what <c>getSelection</c> should answer.</summary>
    private sealed class EditorJsStub(Bunit.BunitJSModuleInterop module)
    {
        public void Selection(string value, int start, int end) =>
            module.Setup<EditorSelection>("getSelection", _ => true).SetResult(new EditorSelection(value, start, end));
    }

    private async Task<UiTestApp> SetUpTagsAsync()
    {
        var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.PostAsync("Plan the trip #Travel");
        await app.PostAsync("Meeting notes #work/meetings");
        await app.PostAsync("Another #work note");
        return app;
    }

    /// <summary>Renders the component and waits for its background tag fetch (OnInitializedAsync) to settle, since
    /// bUnit's Render returns as soon as the first, pre-fetch render commits.</summary>
    private IRenderedComponent<TagSuggestions> RenderReady(bool enabled, EventCallback<string> onTextChanged)
    {
        var cut = Render<TagSuggestions>(p => p
            .Add(s => s.Target, default)
            .Add(s => s.Enabled, enabled)
            .Add(s => s.OnTextChanged, onTextChanged));
        if (enabled)
        {
            cut.WaitForState(() => cut.RenderCount > 1);
        }

        return cut;
    }

    private static Task UpdateAsync(IRenderedComponent<TagSuggestions> cut) => cut.InvokeAsync(cut.Instance.UpdateAsync);

    private static Task<bool> KeyDownAsync(IRenderedComponent<TagSuggestions> cut, string key) =>
        cut.InvokeAsync(() => cut.Instance.OnKeyDownAsync(key, metaKey: false, ctrlKey: false, shiftKey: false));

    [Fact]
    public async Task Shows_nothing_when_turned_off()
    {
        using var app = await SetUpTagsAsync();
        var js = SetUpEditorJs();
        js.Selection("#wo", 3, 3);
        var cut = RenderReady(enabled: false, EventCallback.Factory.Create<string>(this, _ => { }));

        await UpdateAsync(cut);

        Assert.Empty(cut.FindAll("ul"));
        Assert.False(cut.Instance.IsOpen);
    }

    [Fact]
    public async Task Suggests_matching_tags_while_hash_is_typed()
    {
        using var app = await SetUpTagsAsync();
        var js = SetUpEditorJs();
        js.Selection("#wo", 3, 3);
        var cut = RenderReady(enabled: true, EventCallback.Factory.Create<string>(this, _ => { }));

        await UpdateAsync(cut);

        var options = cut.FindAll("li[role=option]");
        Assert.Equal(["work", "work/meetings"], options.Select(o => o.QuerySelector("span")!.TextContent));
        Assert.Equal("true", options[0].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task Arrow_down_moves_the_active_suggestion()
    {
        using var app = await SetUpTagsAsync();
        var js = SetUpEditorJs();
        js.Selection("#wo", 3, 3);
        var cut = RenderReady(enabled: true, EventCallback.Factory.Create<string>(this, _ => { }));
        await UpdateAsync(cut);

        var handled = await KeyDownAsync(cut, "ArrowDown");

        Assert.True(handled);
        var options = cut.FindAll("li[role=option]");
        Assert.Equal("true", options[1].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task Enter_chooses_the_active_tag_and_moves_the_caret_after_it()
    {
        using var app = await SetUpTagsAsync();
        var js = SetUpEditorJs();
        js.Selection("Plan #wo", 8, 8);
        string? changed = null;
        var cut = RenderReady(enabled: true, EventCallback.Factory.Create<string>(this, text => changed = text));
        await UpdateAsync(cut);

        var handled = await KeyDownAsync(cut, "Enter");

        Assert.True(handled);
        Assert.Equal("Plan #work ", changed);
        Assert.False(cut.Instance.IsOpen);
    }

    [Fact]
    public async Task Escape_dismisses_the_popup_for_the_same_query()
    {
        using var app = await SetUpTagsAsync();
        var js = SetUpEditorJs();
        js.Selection("#wo", 3, 3);
        var cut = RenderReady(enabled: true, EventCallback.Factory.Create<string>(this, _ => { }));
        await UpdateAsync(cut);
        Assert.True(cut.Instance.IsOpen);

        var handled = await KeyDownAsync(cut, "Escape");
        Assert.True(handled);
        Assert.False(cut.Instance.IsOpen);

        await UpdateAsync(cut); // the same query (caret has not moved): stays dismissed
        Assert.False(cut.Instance.IsOpen);
    }
}
