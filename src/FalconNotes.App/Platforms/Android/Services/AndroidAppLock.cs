namespace FalconNotes.App.Services;

/// <summary>
/// <see cref="Core.Platform.IAppLock"/> on Android. Biometric unlock needs AndroidX's <c>BiometricPrompt</c>, which is
/// not yet an approved dependency (docs/02, Dependencies, needs the owner's approval first — the same gap as
/// <c>SkiaSharp</c> for photo shrinking). Until then the PIN is the only way in, which the lock never depends on
/// anything else for.
/// </summary>
public sealed class AndroidAppLock : Core.Platform.IAppLock
{
    /// <inheritdoc />
    public bool IsBiometricAvailable => false;

    /// <inheritdoc />
    public string BiometricName => "fingerprint or face";

    /// <inheritdoc />
    public Task<bool> AuthenticateAsync(string reason) => Task.FromResult(false);
}
