namespace FalconNotes.Core.Platform;

/// <summary>
/// Biometric unlock (docs/02, Platform services; docs/03, App lock): AndroidX <c>BiometricPrompt</c>, Windows Hello or
/// Touch ID. The PIN always works too; this is only ever an alternative to it, never a replacement for the device key.
/// </summary>
public interface IAppLock
{
    /// <summary>Whether the device offers biometrics the app can use now: a sensor, with a finger or face enrolled.</summary>
    bool IsBiometricAvailable { get; }

    /// <summary>What to call it: "fingerprint or face", "Windows Hello" or "Touch ID".</summary>
    string BiometricName { get; }

    /// <summary>Shows the system's biometric prompt and waits for the result.</summary>
    /// <param name="reason">Shown in the prompt, e.g. "Unlock Falcon Notes".</param>
    /// <returns>True when the user was verified; false when they cancelled or it failed.</returns>
    Task<bool> AuthenticateAsync(string reason);
}
