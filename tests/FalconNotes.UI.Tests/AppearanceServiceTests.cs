using FalconNotes.Core.Domain;
using FalconNotes.UI.State;
using FalconNotes.UI.Tests.Fakes;

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
}
