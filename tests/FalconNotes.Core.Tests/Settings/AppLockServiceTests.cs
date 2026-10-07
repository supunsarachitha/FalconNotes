using FalconNotes.Core.Domain;
using FalconNotes.Core.Settings;

namespace FalconNotes.Core.Tests.Settings;

/// <summary>
/// New tests (docs/11): the app lock has no reference implementation to port from, since it is new in this app
/// (docs/01, D5). Covers PIN hashing and checking, the tries counter surviving a "restart" (a fresh service reading
/// the same database), and the lockout's doubling.
/// </summary>
public class AppLockServiceTests
{
    [Fact]
    public async Task Starts_off_with_the_default_one_minute_delay()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);

        var settings = await lockService.GetAsync();

        Assert.False(settings.Enabled);
        Assert.False(settings.Biometrics);
        Assert.Equal(60, settings.LockAfterSeconds);
        Assert.Null(settings.PinHash);
    }

    [Fact]
    public async Task Enabling_hashes_the_PIN_and_the_same_PIN_then_verifies()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);

        await lockService.EnableAsync("1234");
        var settings = await lockService.GetAsync();

        Assert.True(settings.Enabled);
        Assert.NotNull(settings.PinHash);
        Assert.NotEqual("1234", settings.PinHash); // never stored in the clear
        Assert.True(await lockService.VerifyPinAsync("1234"));
    }

    [Theory]
    [InlineData("123")] // too short
    [InlineData("123456789")] // too long
    [InlineData("12a4")] // not all digits
    public async Task Rejects_a_PIN_that_is_not_4_to_8_digits(string pin)
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);

        var ex = await Assert.ThrowsAsync<UserFacingException>(() => lockService.EnableAsync(pin));
        Assert.Equal("Use 4 to 8 digits.", ex.Message);
    }

    [Fact]
    public async Task A_wrong_PIN_does_not_verify_and_a_correct_one_still_does_afterwards()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);
        await lockService.EnableAsync("1234");

        Assert.False(await lockService.VerifyPinAsync("4321"));
        Assert.True(await lockService.VerifyPinAsync("1234"));
    }

    [Fact]
    public async Task Changing_the_PIN_replaces_the_hash_and_the_old_PIN_no_longer_verifies()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);
        await lockService.EnableAsync("1234");

        await lockService.ChangePinAsync("5678");

        Assert.False(await lockService.VerifyPinAsync("1234"));
        Assert.True(await lockService.VerifyPinAsync("5678"));
    }

    [Fact]
    public async Task Disabling_turns_biometrics_off_too_and_resets_the_tries_counter()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);
        await lockService.EnableAsync("1234");
        await lockService.SetBiometricsAsync(true);
        await lockService.VerifyPinAsync("wrong");

        await lockService.DisableAsync();

        var settings = await lockService.GetAsync();
        Assert.False(settings.Enabled);
        Assert.False(settings.Biometrics);
        Assert.Equal(0, settings.FailedAttempts);
    }

    [Fact]
    public async Task Five_wrong_PINs_lock_it_out_for_30_seconds_and_the_counter_survives_a_restart()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);
        await lockService.EnableAsync("1234");

        for (var i = 0; i < 4; i++)
        {
            Assert.False(await lockService.VerifyPinAsync("0000"));
            Assert.Null(await lockService.LockoutRemainingAsync());
        }

        Assert.False(await lockService.VerifyPinAsync("0000"));
        var remaining = await lockService.LockoutRemainingAsync();
        Assert.NotNull(remaining);
        Assert.Equal(30, remaining.Value.TotalSeconds, precision: 0);

        // A fresh service instance, as after a restart, reads the same counter and lockout from the database.
        var reopened = new AppLockService(app.Storage, app.Clock);
        Assert.Equal(5, (await reopened.GetAsync()).FailedAttempts);
        Assert.False(await reopened.VerifyPinAsync("1234")); // the right PIN is refused while locked out
    }

    [Fact]
    public async Task The_lockout_doubles_every_five_further_wrong_PINs_up_to_15_minutes()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);
        await lockService.EnableAsync("1234");

        async Task<TimeSpan> FailFiveMoreTimesAsync()
        {
            for (var i = 0; i < 5; i++)
            {
                await lockService.VerifyPinAsync("0000");
            }

            return (await lockService.LockoutRemainingAsync())!.Value;
        }

        Assert.Equal(30, (await FailFiveMoreTimesAsync()).TotalSeconds, precision: 0);
        app.Clock.Advance(TimeSpan.FromSeconds(31)); // past the first lockout, so the next failure is counted fresh
        Assert.Equal(60, (await FailFiveMoreTimesAsync()).TotalSeconds, precision: 0);
        app.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(120, (await FailFiveMoreTimesAsync()).TotalSeconds, precision: 0);
    }

    [Fact]
    public async Task The_lockout_expires_and_then_the_right_PIN_works_again()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);
        await lockService.EnableAsync("1234");
        for (var i = 0; i < 5; i++)
        {
            await lockService.VerifyPinAsync("0000");
        }

        Assert.False(await lockService.VerifyPinAsync("1234")); // still locked out, even with the right PIN

        app.Clock.Advance(TimeSpan.FromSeconds(31));

        Assert.Null(await lockService.LockoutRemainingAsync());
        Assert.True(await lockService.VerifyPinAsync("1234"));
        Assert.Equal(0, (await lockService.GetAsync()).FailedAttempts);
    }

    [Fact]
    public async Task Changes_the_background_delay()
    {
        using var app = await TestApp.StartAsync();
        var lockService = new AppLockService(app.Storage, app.Clock);

        await lockService.SetLockAfterAsync(900);

        Assert.Equal(900, (await lockService.GetAsync()).LockAfterSeconds);
    }
}
