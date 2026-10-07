using FalconNotes.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace FalconNotes.UI.Tests;

/// <summary>The app lock's session state (docs/03, App lock) around erasing, which removes the lock with the data.</summary>
public class AppLockStateTests : BunitContext
{
    [Fact]
    public async Task Loading_again_after_the_lock_is_gone_unlocks_and_tells_the_router()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.AppLock.EnableAsync("2580");
        app.AppLock.LockNow();
        Assert.True(app.AppLock.IsLocked);
        var changes = 0;
        app.AppLock.Changed += () => changes++;

        // Erase all data replaces the database; turning the lock off in storage stands in for that here.
        await Services.GetRequiredService<AppLockService>().DisableAsync();
        await app.AppLock.LoadAsync();

        Assert.False(app.AppLock.IsLocked);
        Assert.False(app.AppLock.Settings.Enabled);
        Assert.Equal(1, changes);
    }
}
