using FalconNotes.UI.Components;
using Microsoft.AspNetCore.Components;

namespace FalconNotes.UI.Tests;

/// <summary>
/// Markdown.tsx's own rendering rules (raw HTML dropped, tags linked, safe links, task offsets) are Core's rules,
/// already covered by <c>Core.Tests/Markdown/MarkdownRendererTests.cs</c> (docs/11). This covers only what
/// <see cref="Markdown"/> itself adds: disabling checkboxes without a callback, and wiring markdown.js to report
/// which item was ticked.
/// </summary>
public class MarkdownTests : BunitContext
{
    [Fact]
    public async Task Renders_the_notes_markdown()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var note = await app.PostAsync("**bold** #work");

        var cut = Render<Markdown>(p => p.Add(m => m.Note, note).Add(m => m.Content, note.Content));

        Assert.Equal("bold", cut.Find("strong").TextContent);
        Assert.Equal("/?tag=work", cut.Find("a.tag").GetAttribute("href"));
    }

    [Fact]
    public async Task Disables_every_checkbox_when_there_is_no_toggle_callback()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var note = await app.PostAsync("- [ ] book flights");

        var cut = Render<Markdown>(p => p.Add(m => m.Note, note).Add(m => m.Content, note.Content));

        Assert.True(cut.Find("input[type=checkbox]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Binds_the_task_toggle_script_once_a_callback_is_given()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var note = await app.PostAsync("- [ ] book flights");
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/FalconNotes.UI/js/markdown.js");
        module.SetupVoid("bindTaskToggle", _ => true);

        var cut = Render<Markdown>(p => p
            .Add(m => m.Note, note)
            .Add(m => m.Content, note.Content)
            .Add(m => m.OnToggleTask, EventCallback.Factory.Create<int>(this, _ => { })));

        cut.Find("input[type=checkbox]:not([disabled])");
        module.VerifyInvoke("bindTaskToggle");
    }

    [Fact]
    public async Task Reports_the_items_offset_when_the_script_calls_back()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var note = await app.PostAsync("- [ ] book flights");
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/FalconNotes.UI/js/markdown.js");
        module.SetupVoid("bindTaskToggle", _ => true);
        int? toggled = null;
        var cut = Render<Markdown>(p => p
            .Add(m => m.Note, note)
            .Add(m => m.Content, note.Content)
            .Add(m => m.OnToggleTask, EventCallback.Factory.Create<int>(this, offset => toggled = offset)));
        var marker = note.Content.IndexOf("- [ ]", StringComparison.Ordinal);

        await cut.Instance.ToggleTask(marker);

        Assert.Equal(marker, toggled);
    }
}
