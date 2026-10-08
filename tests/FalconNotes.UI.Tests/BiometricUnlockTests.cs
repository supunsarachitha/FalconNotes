using FalconNotes.Core.Maintenance;
using FalconNotes.UI.Components;
using FalconNotes.UI.Pages.Settings;
using FalconNotes.UI.State;
using FalconNotes.UI.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using LockScreen = FalconNotes.UI.Pages.Lock; // System.Threading has a Lock too

namespace FalconNotes.UI.Tests;

/// <summary>New tests (docs/11): biometric unlock has no reference equivalent, since the app lock is new in this app
/// (docs/01, D5). Covers docs/07's Lock and Settings → Privacy &amp; security and docs/03's App lock: the prompt opens
/// by itself once per lock, is offered only while it is turned on and the device has biometrics, is turned on only
/// after one check succeeds, and never stands in the PIN's way.</summary>
/// <remarks>
/// Every check after a render or a click waits for it (<c>WaitForAssertion</c>). The screens read the database when
/// they open, and a click made while that answer is being rendered waits its turn, so it has not happened yet when
/// <c>Click</c> returns. On a busy machine that was 1 run in 16.
/// </remarks>
public class BiometricUnlockTests : BunitContext
{
    private const string Pin = "2580";
    private const string UseBiometrics = "Use fingerprint or face";
    private const string UnlockWith = "Unlock with fingerprint or face";

    /// <summary>An app with the lock on, on a device with a fingerprint or a face enrolled.</summary>
    private async Task<UiTestApp> StartAsync(bool biometrics)
    {
        var app = await UiTestApp.StartAsync(Services, "Alex");
        app.Biometrics.IsBiometricAvailable = true;
        await app.AppLock.EnableAsync(Pin);
        await app.AppLock.SetBiometricsAsync(biometrics);

        // The Lock screen and the PIN dialogs use these; the tests here never erase or restart.
        var maintenance = new StartupTasks(app.Core.Notes, app.Core.Cleanup, app.Core.Directories, NullLogger<StartupTasks>.Instance);
        Services.AddSingleton(new AppBootstrapper(app.Core.Startup, maintenance, app.Core.Clock));
        Services.AddSingleton(new AppearanceService(new FakeThemeSource(), app.State));
        var dialogs = JSInterop.SetupModule("./_content/FalconNotes.UI/js/dialogs.js");
        dialogs.SetupVoid("showModal", _ => true).SetVoidResult();
        dialogs.SetupVoid("close", _ => true).SetVoidResult();
        return app;
    }

    private static AngleSharp.Dom.IElement? Button<T>(IRenderedComponent<T> cut, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == text);

    [Fact]
    public async Task The_prompt_opens_by_itself_when_the_Lock_screen_appears_and_unlocks()
    {
        using var app = await StartAsync(biometrics: true);
        app.AppLock.LockNow();

        var cut = Render<LockScreen>();

        cut.WaitForAssertion(() => Assert.False(app.AppLock.IsLocked));
        Assert.Equal(["Unlock Falcon Notes"], app.Biometrics.Prompts);
    }

    [Fact]
    public async Task A_cancelled_prompt_leaves_the_screen_locked_without_an_error_and_the_button_asks_again()
    {
        using var app = await StartAsync(biometrics: true);
        app.Biometrics.NextResult = false;
        app.AppLock.LockNow();

        var cut = Render<LockScreen>();

        cut.WaitForAssertion(() => Assert.Single(app.Biometrics.Prompts));
        Assert.True(app.AppLock.IsLocked);
        Assert.Empty(cut.FindAll("[aria-invalid=true]"));

        // Cancelling the prompt the button opened is not a wrong PIN either.
        Button(cut, UseBiometrics)!.Click();
        cut.WaitForAssertion(() => Assert.Equal(2, app.Biometrics.Prompts.Count)); // the button's, not a second one by itself
        Assert.True(app.AppLock.IsLocked);
        Assert.Empty(cut.FindAll("[aria-invalid=true]"));

        app.Biometrics.NextResult = true;
        Button(cut, UseBiometrics)!.Click();

        cut.WaitForAssertion(() => Assert.False(app.AppLock.IsLocked));
        Assert.Equal(3, app.Biometrics.Prompts.Count);
    }

    [Fact]
    public async Task The_PIN_still_unlocks_after_the_prompt_is_cancelled()
    {
        using var app = await StartAsync(biometrics: true);
        app.Biometrics.NextResult = false;
        app.AppLock.LockNow();
        var cut = Render<LockScreen>();

        cut.Find("input[type=password]").Input(Pin);
        cut.Find("form").Submit();

        // Checking a PIN is 210,000 rounds of PBKDF2, off the render thread: allow for a slow machine.
        cut.WaitForAssertion(() => Assert.False(app.AppLock.IsLocked), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task The_Lock_screen_neither_asks_nor_offers_while_biometric_unlock_is_off()
    {
        using var app = await StartAsync(biometrics: false);
        app.AppLock.LockNow();

        var cut = Render<LockScreen>();

        Assert.True(app.AppLock.IsLocked);
        Assert.Empty(app.Biometrics.Prompts);
        Assert.Null(Button(cut, UseBiometrics));
    }

    [Fact]
    public async Task The_Lock_screen_neither_asks_nor_offers_once_the_device_has_no_biometrics()
    {
        using var app = await StartAsync(biometrics: true);
        app.Biometrics.IsBiometricAvailable = false; // the last fingerprint was removed in the device's settings
        app.AppLock.LockNow();

        var cut = Render<LockScreen>();

        Assert.True(app.AppLock.IsLocked);
        Assert.Empty(app.Biometrics.Prompts);
        Assert.Null(Button(cut, UseBiometrics));
    }

    [Fact]
    public async Task A_biometric_cannot_unlock_while_biometric_unlock_is_off()
    {
        using var app = await StartAsync(biometrics: false);
        app.AppLock.LockNow();

        Assert.False(await app.AppLock.TryBiometricAsync("Unlock Falcon Notes"));

        Assert.True(app.AppLock.IsLocked);
        Assert.Empty(app.Biometrics.Prompts);
    }

    [Fact]
    public async Task Settings_offers_the_switch_only_while_the_lock_is_on_and_the_device_has_biometrics()
    {
        using var app = await StartAsync(biometrics: false);

        var cut = Render<AppLockSection>();
        Assert.Equal("false", cut.Find($"button[role=switch][aria-label='{UnlockWith}']").GetAttribute("aria-checked"));
        Assert.Contains("You can always use your PIN instead.", cut.Markup, StringComparison.Ordinal);

        app.Biometrics.IsBiometricAvailable = false;
        cut.Render();
        Assert.Empty(cut.FindAll($"button[aria-label='{UnlockWith}']"));

        app.Biometrics.IsBiometricAvailable = true;
        await app.AppLock.DisableAsync();
        cut.Render();
        Assert.Empty(cut.FindAll($"button[aria-label='{UnlockWith}']"));
    }

    [Fact]
    public async Task A_fingerprint_added_in_the_devices_settings_shows_the_switch_when_the_app_comes_back()
    {
        using var app = await StartAsync(biometrics: false);
        app.Biometrics.IsBiometricAvailable = false;
        var cut = Render<AppLockSection>();
        Assert.Empty(cut.FindAll($"button[aria-label='{UnlockWith}']"));

        app.AppLock.OnBackgrounded(); // off to the device's settings to add a fingerprint…
        app.Biometrics.IsBiometricAvailable = true;
        app.AppLock.OnForegrounded(); // …and back before "Lock after" has passed

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll($"button[aria-label='{UnlockWith}']")));
        Assert.False(app.AppLock.IsLocked);
    }

    [Fact]
    public async Task A_fingerprint_removed_while_the_app_was_away_takes_the_Lock_screens_button_away()
    {
        using var app = await StartAsync(biometrics: true);
        app.Biometrics.NextResult = false;
        app.AppLock.LockNow();
        var cut = Render<LockScreen>();
        cut.WaitForAssertion(() => Assert.NotNull(Button(cut, UseBiometrics)));

        app.AppLock.OnBackgrounded();
        app.Biometrics.IsBiometricAvailable = false;
        app.AppLock.OnForegrounded();

        cut.WaitForAssertion(() => Assert.Null(Button(cut, UseBiometrics)));
        Assert.Single(app.Biometrics.Prompts); // still once per lock
    }

    [Fact]
    public async Task Turning_the_switch_on_runs_one_check_and_saves_only_when_it_succeeds()
    {
        using var app = await StartAsync(biometrics: false);
        var cut = Render<AppLockSection>();

        app.Biometrics.NextResult = false;
        cut.Find($"button[aria-label='{UnlockWith}']").Click();

        cut.WaitForAssertion(() => Assert.Single(app.Biometrics.Prompts));
        Assert.False(app.AppLock.Settings.Biometrics);

        app.Biometrics.NextResult = true;
        cut.Find($"button[aria-label='{UnlockWith}']").Click();

        cut.WaitForAssertion(() => Assert.True(app.AppLock.Settings.Biometrics));
        Assert.Equal(2, app.Biometrics.Prompts.Count);
        Assert.True((await Services.GetRequiredService<Core.Settings.AppLockService>().GetAsync()).Biometrics);
    }

    [Fact]
    public async Task Turning_the_switch_off_asks_for_nothing()
    {
        using var app = await StartAsync(biometrics: true);
        var cut = Render<AppLockSection>();

        cut.Find($"button[aria-label='{UnlockWith}']").Click();

        cut.WaitForAssertion(() => Assert.False(app.AppLock.Settings.Biometrics));
        Assert.Empty(app.Biometrics.Prompts);
    }

    [Fact]
    public async Task A_biometric_check_stands_in_for_the_PIN_where_Settings_asks_for_it()
    {
        using var app = await StartAsync(biometrics: true);
        var verified = 0;
        var cut = Render<VerifyPinDialog>(p => p.Add(d => d.Open, true).Add(d => d.OnVerified, () => verified++));

        app.Biometrics.NextResult = false;
        Button(cut, UseBiometrics)!.Click();
        cut.WaitForAssertion(() => Assert.Single(app.Biometrics.Prompts));
        Assert.Equal(0, verified);

        app.Biometrics.NextResult = true;
        Button(cut, UseBiometrics)!.Click();

        cut.WaitForAssertion(() => Assert.Equal(1, verified));
        Assert.Equal(["Confirm it's you", "Confirm it's you"], app.Biometrics.Prompts);
    }

    [Fact]
    public async Task Settings_asks_for_the_PIN_alone_while_biometric_unlock_is_off()
    {
        using var app = await StartAsync(biometrics: false);

        var cut = Render<VerifyPinDialog>(p => p.Add(d => d.Open, true));

        Assert.Null(Button(cut, UseBiometrics));
    }
}
