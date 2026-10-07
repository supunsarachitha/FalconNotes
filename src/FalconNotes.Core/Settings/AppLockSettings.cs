namespace FalconNotes.Core.Settings;

/// <summary>
/// The app lock's settings (docs/03, App lock): <c>Settings['appLock']</c>. It is a privacy screen, not a key — the
/// device key never depends on the PIN, so a forgotten PIN can never lose notes.
/// </summary>
/// <param name="Enabled">Whether the lock is on.</param>
/// <param name="Biometrics">Whether fingerprint, face, Windows Hello or Touch ID may unlock instead of the PIN.</param>
/// <param name="LockAfterSeconds">How long the app may stay in the background before it locks: 0 for immediately.</param>
/// <param name="PinHash">The PIN's PBKDF2 hash, base64, or null when the lock has never been set up.</param>
/// <param name="PinSalt">The hash's random salt, base64.</param>
/// <param name="PinIterations">The hash's iteration count, recorded so a later change of it can still check old hashes.</param>
/// <param name="FailedAttempts">Wrong PINs in a row, kept across restarts.</param>
/// <param name="LockedUntilUtc">While in the future, the PIN field is disabled.</param>
public sealed record AppLockSettings(
    bool Enabled,
    bool Biometrics,
    int LockAfterSeconds,
    string? PinHash,
    string? PinSalt,
    int PinIterations,
    int FailedAttempts,
    DateTime? LockedUntilUtc)
{
    /// <summary>The lock off, with the default "after 1 minute" delay ready for when it is turned on.</summary>
    public static readonly AppLockSettings Default = new(false, false, 60, null, null, 0, 0, null);

    /// <summary>The delays offered under "Lock after" (docs/07).</summary>
    public static readonly IReadOnlyList<int> LockAfterChoices = [0, 60, 300, 900, 3600];
}
