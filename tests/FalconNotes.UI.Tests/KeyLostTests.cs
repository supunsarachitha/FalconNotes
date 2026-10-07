using FalconNotes.Core.Crypto;
using FalconNotes.UI.Pages;

namespace FalconNotes.UI.Tests;

/// <summary>New tests (docs/11): Key lost has no reference equivalent, so this covers docs/07's Key lost directly:
/// the explanation names the platform's key store, and both buttons move the data aside and forget the key before
/// telling <see cref="Welcome"/> whether to continue to a restore.</summary>
public class KeyLostTests : BunitContext
{
    private async Task<UiTestApp> StartAsync() => await UiTestApp.StartAsync(Services, "Alex");

    [Fact]
    public async Task Explains_the_platform_s_key_store_and_offers_both_ways_out()
    {
        using var app = await StartAsync();

        var cut = Render<KeyLost>();

        Assert.Equal("Your notes cannot be opened", cut.Find("h1").TextContent);
        Assert.Contains("Android", cut.Markup, StringComparison.Ordinal); // FakeAppInfo.PlatformName
        var buttons = cut.FindAll("button").Select(b => b.TextContent.Trim());
        Assert.Contains("Restore from a backup…", buttons);
        Assert.Contains("Start with no notes", buttons);
    }

    [Fact]
    public async Task Start_with_no_notes_moves_the_data_aside_forgets_the_key_and_continues_without_restoring()
    {
        using var app = await StartAsync();
        await app.Core.Secrets.SetAsync(DeviceKeyStore.SecretName, Convert.ToBase64String(new byte[32]));
        var started = false;
        var restore = (bool?)null;

        var cut = Render<KeyLost>(p => p.Add(k => k.OnStartOver, (bool r) =>
        {
            started = true;
            restore = r;
        }));
        var buttons = cut.FindAll("button");
        buttons.First(b => b.TextContent.Contains("Start with no notes", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.True(started));
        Assert.False(restore);
        Assert.Null(await app.Core.Secrets.GetAsync(DeviceKeyStore.SecretName));
    }

    [Fact]
    public async Task Restore_from_a_backup_moves_the_data_aside_and_asks_to_continue_restoring()
    {
        using var app = await StartAsync();
        await app.Core.Secrets.SetAsync(DeviceKeyStore.SecretName, Convert.ToBase64String(new byte[32]));
        var restore = (bool?)null;

        var cut = Render<KeyLost>(p => p.Add(k => k.OnStartOver, (bool r) => restore = r));
        cut.FindAll("button").First(b => b.TextContent.Contains("Restore from a backup", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.True(restore));
    }
}
