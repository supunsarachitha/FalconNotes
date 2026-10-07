using FalconNotes.Core.Platform;

namespace FalconNotes.UI.Tests.Fakes;

/// <summary>A biometric prompt that never asks the user, for App lock tests.</summary>
public sealed class FakeAppLock : IAppLock
{
    /// <summary>Whether the device offers biometrics; false by default.</summary>
    public bool IsBiometricAvailable { get; set; }

    /// <inheritdoc />
    public string BiometricName => "fingerprint or face";

    /// <summary>What the next call returns.</summary>
    public bool NextResult { get; set; } = true;

    /// <inheritdoc />
    public Task<bool> AuthenticateAsync(string reason) => Task.FromResult(NextResult);
}
