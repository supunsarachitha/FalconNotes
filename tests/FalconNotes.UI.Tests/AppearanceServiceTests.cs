using FalconNotes.Core.Domain;
using FalconNotes.UI.State;
using FalconNotes.UI.Tests.Fakes;
using Microsoft.JSInterop;

namespace FalconNotes.UI.Tests;

/// <summary>
/// The page follows the preferences and the device (docs/06, Light and dark), as <c>useAppearance</c> keeps it in the
/// web app: the router calls <see cref="AppearanceService.ApplyAsync"/> after every change.
/// </summary>
public class AppearanceServiceTests : BunitContext
{
    [Fact]
    public async Task Applies_the_theme_and_accent_again_only_when_they_change()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var module = JSInterop.SetupModule("./_content/FalconNotes.UI/js/appearance.js");
        module.SetupVoid("apply", _ => true).SetVoidResult();
        var device = new FakeThemeSource();
        var appearance = new AppearanceService(device, app.State);

        await appearance.ApplyAsync(JSInterop.JSRuntime);
        await app.State.UpdatePreferencesAsync(p => p with { Theme = Theme.Dark, Accent = Accent.Ocean });
        await appearance.ApplyAsync(JSInterop.JSRuntime);
        await app.State.UpdatePreferencesAsync(p => p with { NoteTitles = !p.NoteTitles });
        await appearance.ApplyAsync(JSInterop.JSRuntime);

        var calls = module.Invocations["apply"].Select(i => (i.Arguments[0], i.Arguments[1])).ToList();
        Assert.Equal([(false, "falcon"), (true, "ocean")], calls);
        Assert.Equal([false, true], device.Chrome);
    }

    // New: the backgrounds are this app's own (docs/06, Backgrounds).
    [Fact]
    public async Task Applies_the_background_by_its_name_and_plain_as_none()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var module = JSInterop.SetupModule("./_content/FalconNotes.UI/js/appearance.js");
        module.SetupVoid("apply", _ => true).SetVoidResult();
        var appearance = new AppearanceService(new FakeThemeSource(), app.State);

        await appearance.ApplyAsync(JSInterop.JSRuntime);
        await app.State.UpdatePreferencesAsync(p => p with { Background = Background.LunarNewYear });
        await appearance.ApplyAsync(JSInterop.JSRuntime);
        await appearance.ApplyAsync(JSInterop.JSRuntime); // nothing changed: not applied again
        await app.State.UpdatePreferencesAsync(p => p with { Background = Background.None });
        await appearance.ApplyAsync(JSInterop.JSRuntime);

        Assert.Equal(["none", "lunarnewyear", "none"], module.Invocations["apply"].Select(i => i.Arguments[2]).ToList());
    }

    // New: found by the owner on a Pixel 8 Pro, 2026-10-13 (docs/12, Edge to edge). Android makes the activity again
    // after Back has left the app: the same service, a new page. The module of the page that is gone reaches nothing.
    [Fact]
    public async Task A_new_page_gets_the_appearance_again_and_a_module_of_its_own()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Theme = Theme.Dark });
        var device = new FakeThemeSource();
        var appearance = new AppearanceService(device, app.State);
        var first = new FakePage();
        var second = new FakePage();

        await appearance.ApplyAsync(first);
        await (await appearance.ModuleAsync(first)).InvokeVoidAsync("setInsets", 24, 0, 16, 0);
        await appearance.ApplyAsync(second);
        await (await appearance.ModuleAsync(second)).InvokeVoidAsync("setInsets", 24, 0, 16, 0);
        await appearance.ApplyAsync(second); // nothing changed on this page: not applied again

        Assert.Equal(["apply", "setInsets"], first.Calls);
        Assert.Equal(["apply", "setInsets"], second.Calls);
        Assert.Equal([true, true], device.Chrome); // the new window's status bar is told too
    }

    [Fact]
    public async Task Device_theme_follows_the_device()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex", new Preferences { Theme = Theme.System });
        var module = JSInterop.SetupModule("./_content/FalconNotes.UI/js/appearance.js");
        module.SetupVoid("apply", _ => true).SetVoidResult();
        var device = new FakeThemeSource();
        var appearance = new AppearanceService(device, app.State);

        await appearance.ApplyAsync(JSInterop.JSRuntime);
        device.SetDevice(dark: true);
        await appearance.ApplyAsync(JSInterop.JSRuntime);

        Assert.Equal([false, true], module.Invocations["apply"].Select(i => i.Arguments[0]).ToList());
    }

    /// <summary>A page of its own: a JS runtime whose module records what is called on it.</summary>
    private sealed class FakePage : IJSRuntime
    {
        public List<string> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult((TValue)(object)new Module(this));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        private sealed class Module(FakePage page) : IJSObjectReference
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            {
                page.Calls.Add(identifier);
                return ValueTask.FromResult(default(TValue)!);
            }

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
                InvokeAsync<TValue>(identifier, args);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
