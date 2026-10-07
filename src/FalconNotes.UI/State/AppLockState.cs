using FalconNotes.Core.Platform;
using FalconNotes.Core.Settings;

namespace FalconNotes.UI.State;

/// <summary>
/// The app lock's session state (docs/03, App lock; docs/07, Lock): whether the Lock screen shows now, independent of
/// the encryption key. Locks at every cold start when the lock is on (docs/02, step 3), and again after the chosen
/// delay in the background (<see cref="OnBackgrounded"/>, <see cref="OnForegrounded"/>, called from the App project's
/// window lifecycle — Core and UI define the state, but only MAUI knows when it happens).
/// </summary>
/// <param name="service">Reads, checks and changes the PIN and its settings.</param>
/// <param name="biometrics">The platform's biometric prompt.</param>
/// <param name="time">The clock, for the background timer.</param>
public sealed class AppLockState(AppLockService service, IAppLock biometrics, TimeProvider time)
{
    private DateTimeOffset? _backgroundedAt;

    /// <summary>Raised when the lock state or its settings change; may run on any thread.</summary>
    public event Action? Changed;

    /// <summary>The current settings, refreshed by every change here and by <see cref="RefreshAsync"/>.</summary>
    public AppLockSettings Settings { get; private set; } = AppLockSettings.Default;

    /// <summary>Whether the Lock screen is showing now.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Whether the device can offer biometric unlock at all.</summary>
    public bool IsBiometricAvailable => biometrics.IsBiometricAvailable;

    /// <summary>What to call it on this device: "fingerprint or face", "Windows Hello" or "Touch ID".</summary>
    public string BiometricName => biometrics.BiometricName;

    /// <summary>Loads the settings at start-up and locks at once if the lock is on.</summary>
    /// <returns>A task that completes when loaded.</returns>
    public async Task LoadAsync()
    {
        Settings = await service.GetAsync();
        IsLocked = Settings.Enabled;
        Changed?.Invoke();
    }

    /// <summary>Re-reads the settings, e.g. after Settings → Privacy &amp; security changes them.</summary>
    /// <returns>A task that completes when refreshed.</returns>
    public async Task RefreshAsync()
    {
        Settings = await service.GetAsync();
        Changed?.Invoke();
    }

    /// <summary>Turns the lock on with a new PIN (Settings → Privacy &amp; security → Set a PIN).</summary>
    /// <param name="pin">The PIN.</param>
    /// <returns>A task that completes when it is saved.</returns>
    public async Task EnableAsync(string pin)
    {
        await service.EnableAsync(pin);
        await RefreshAsync();
    }

    /// <summary>Turns the lock off. The caller has already checked the PIN or biometrics.</summary>
    /// <returns>A task that completes when it is saved.</returns>
    public async Task DisableAsync()
    {
        await service.DisableAsync();
        await RefreshAsync();
    }

    /// <summary>Changes the PIN. The caller has already checked the current one.</summary>
    /// <param name="pin">The new PIN.</param>
    /// <returns>A task that completes when it is saved.</returns>
    public async Task ChangePinAsync(string pin)
    {
        await service.ChangePinAsync(pin);
        await RefreshAsync();
    }

    /// <summary>Turns biometric unlock on or off.</summary>
    /// <param name="on">Whether it is allowed.</param>
    /// <returns>A task that completes when it is saved.</returns>
    public async Task SetBiometricsAsync(bool on)
    {
        await service.SetBiometricsAsync(on);
        await RefreshAsync();
    }

    /// <summary>Changes how long the app may stay in the background before it locks.</summary>
    /// <param name="seconds">One of <see cref="AppLockSettings.LockAfterChoices"/>.</param>
    /// <returns>A task that completes when it is saved.</returns>
    public async Task SetLockAfterAsync(int seconds)
    {
        await service.SetLockAfterAsync(seconds);
        await RefreshAsync();
    }

    /// <summary>
    /// Runs one biometric check with no PIN fallback, used only to confirm the device can authenticate before turning
    /// biometric unlock on (<see cref="TryBiometricAsync"/> requires it to already be on, since it is also how a
    /// locked session is actually unlocked).
    /// </summary>
    /// <param name="reason">Shown in the prompt.</param>
    /// <returns>Whether it succeeded.</returns>
    public Task<bool> VerifyBiometricAsync(string reason) => biometrics.AuthenticateAsync(reason);

    /// <summary>Locks now: the sidebar's Lock button, or the background timer.</summary>
    public void LockNow()
    {
        if (!Settings.Enabled || IsLocked)
        {
            return;
        }

        IsLocked = true;
        Changed?.Invoke();
    }

    /// <summary>Checks a typed PIN; unlocks on success.</summary>
    /// <param name="pin">What was typed.</param>
    /// <returns>Whether it matched.</returns>
    public async Task<bool> TryPinAsync(string pin)
    {
        var ok = await service.VerifyPinAsync(pin);
        Settings = await service.GetAsync();
        if (ok)
        {
            IsLocked = false;
        }

        Changed?.Invoke();
        return ok;
    }

    /// <summary>Shows the biometric prompt; unlocks on success.</summary>
    /// <param name="reason">Shown in the prompt.</param>
    /// <returns>Whether it succeeded.</returns>
    public async Task<bool> TryBiometricAsync(string reason)
    {
        if (!Settings.Biometrics || !(await biometrics.AuthenticateAsync(reason)))
        {
            return false;
        }

        IsLocked = false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>How long until the PIN field may be tried again.</summary>
    /// <returns>The remaining time, or null when it is not locked out.</returns>
    public Task<TimeSpan?> LockoutRemainingAsync() => service.LockoutRemainingAsync();

    /// <summary>Called when the app goes to the background.</summary>
    public void OnBackgrounded() => _backgroundedAt = time.GetUtcNow();

    /// <summary>
    /// Called when the app returns to the foreground: locks again if the lock is on and the app stayed away at least
    /// as long as "Lock after" (0 means every time).
    /// </summary>
    public void OnForegrounded()
    {
        var since = _backgroundedAt;
        _backgroundedAt = null;
        if (since is null || !Settings.Enabled || IsLocked)
        {
            return;
        }

        if ((time.GetUtcNow() - since.Value).TotalSeconds >= Settings.LockAfterSeconds)
        {
            LockNow();
        }
    }
}
