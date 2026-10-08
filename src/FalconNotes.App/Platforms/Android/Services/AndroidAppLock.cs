using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;

namespace FalconNotes.App.Services;

/// <summary>
/// <see cref="Core.Platform.IAppLock"/> on Android: AndroidX's <c>BiometricPrompt</c>, asking for a strong or a weak
/// biometric (docs/12, Biometrics), so a fingerprint or a face works on every supported version (API 26 and later).
/// </summary>
/// <remarks>
/// The prompt has no <c>CryptoObject</c> and does not offer the device's own PIN or pattern. The app lock is a privacy
/// screen, not a key (docs/03, App lock), and its fallback is the app's PIN on the screen behind the prompt, so the
/// prompt's second button only closes it.
/// </remarks>
public sealed class AndroidAppLock : Core.Platform.IAppLock
{
    private const int Biometrics = BiometricManager.Authenticators.BiometricStrong | BiometricManager.Authenticators.BiometricWeak;

    private TaskCompletionSource<bool>? _pending;
    private BiometricPrompt? _prompt;

    /// <summary>
    /// Asked again on every read, not cached: a fingerprint added or removed in the device's settings shows in the
    /// app the next time a screen renders. False when there is no sensor or nothing is enrolled.
    /// </summary>
    public bool IsBiometricAvailable =>
        BiometricManager.From(Platform.AppContext).CanAuthenticate(Biometrics) == BiometricManager.BiometricSuccess;

    /// <inheritdoc />
    public string BiometricName => "fingerprint or face";

    /// <inheritdoc />
    public Task<bool> AuthenticateAsync(string reason) => MainThread.InvokeOnMainThreadAsync(() => ShowAsync(reason));

    private Task<bool> ShowAsync(string reason)
    {
        // BiometricPrompt shows nothing, and calls nothing back, once the activity has saved its state (it is on its
        // way to the background). Answer "not verified" rather than leave the caller waiting.
        if (Platform.CurrentActivity is not FragmentActivity activity || activity.IsFinishing ||
            activity.SupportFragmentManager.IsStateSaved)
        {
            return Task.FromResult(false);
        }

        // Only one prompt can show. A second request replaces the first, which counts as cancelled.
        _prompt?.CancelAuthentication();
        _pending?.TrySetResult(false);

        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        _prompt = new BiometricPrompt(activity, ContextCompat.GetMainExecutor(activity)!, new Callback(pending));
        _prompt.Authenticate(new BiometricPrompt.PromptInfo.Builder()
            .SetTitle(reason)
            .SetNegativeButtonText("Cancel")
            .SetAllowedAuthenticators(Biometrics)
            // A recognised face unlocks at once, as a fingerprint does, without a second tap on "Confirm".
            .SetConfirmationRequired(false)
            .Build());
        return pending.Task;
    }

    /// <summary>Completes the request when the prompt closes. A finger or face that is not recognised leaves the
    /// prompt open for another try, so <c>OnAuthenticationFailed</c> is not an answer.</summary>
    /// <param name="pending">The waiting request.</param>
    private sealed class Callback(TaskCompletionSource<bool> pending) : BiometricPrompt.AuthenticationCallback
    {
        /// <inheritdoc />
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result) => pending.TrySetResult(true);

        /// <summary>The prompt closed without a match: cancelled, timed out, or the sensor is locked out after too
        /// many tries. Every case leaves the app locked, with the PIN as the way in.</summary>
        /// <param name="errorCode">Why it closed.</param>
        /// <param name="errString">The system's message, which the prompt has already shown.</param>
        public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString) => pending.TrySetResult(false);
    }
}
