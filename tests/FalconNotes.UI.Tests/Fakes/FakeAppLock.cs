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

    /// <summary>The reason given to each prompt shown, in order.</summary>
    public List<string> Prompts { get; } = [];

    /// <inheritdoc />
    public Task<bool> AuthenticateAsync(string reason)
    {
        Prompts.Add(reason);
        return Task.FromResult(NextResult);
    }
}
