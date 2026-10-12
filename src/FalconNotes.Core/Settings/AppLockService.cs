using System.Security.Cryptography;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;
using FalconNotes.Core.Storage;

namespace FalconNotes.Core.Settings;

/// <summary>
/// Sets up, checks and changes the app lock's PIN (docs/03, App lock): PBKDF2-HMAC-SHA256 with 210,000 iterations and
/// a 16-byte random salt, compared in constant time. Five wrong PINs in a row lock the screen for 30 s; each further
/// five doubles the wait, up to 15 minutes. The counter and the lockout survive a restart, since they are stored.
/// The same PIN takes the lock off a locked note (docs/04, Locked notes), so it can be in use with the app lock off.
/// </summary>
/// <param name="storage">The open database.</param>
/// <param name="time">The clock, for the lockout.</param>
public sealed class AppLockService(StorageContext storage, TimeProvider time)
{
    private const string Key = "appLock";
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    /// <summary>The PIN's allowed length.</summary>
    public const int MinPinLength = 4;

    /// <summary>The PIN's allowed length.</summary>
    public const int MaxPinLength = 8;

    /// <summary>The current settings.</summary>
    /// <returns>The settings, or the defaults when the lock has never been set up.</returns>
    public Task<AppLockSettings> GetAsync() =>
        storage.Database.ReadAsync(connection => SettingsStore.Get<AppLockSettings>(connection, Key) ?? AppLockSettings.Default);

    /// <summary>
    /// Whether a PIN is in use: the app lock is on, or a note is locked. While it is, a new PIN may only replace it
    /// after the current one was checked (<see cref="ChangePinAsync"/>). A PIN left behind by a lock since turned off,
    /// with no locked note, protects nothing and is not in use.
    /// </summary>
    /// <returns>Whether the PIN is in use.</returns>
    public Task<bool> PinInUseAsync() =>
        storage.Database.ReadAsync(connection =>
            SettingsStore.Get<AppLockSettings>(connection, Key) is { PinHash: not null } settings
            && (settings.Enabled || NoteRepository.AnyLocked(connection)));

    /// <summary>Turns the lock on with a new PIN (docs/07, Set a PIN).</summary>
    /// <param name="pin">The PIN, checked for 4–8 digits first.</param>
    /// <returns>A task that completes when it is saved.</returns>
    /// <exception cref="UserFacingException">The PIN is not 4–8 digits.</exception>
    /// <exception cref="InvalidOperationException">Locked notes use a PIN already: check it and call
    /// <see cref="TurnOnAsync"/>.</exception>
    public async Task EnableAsync(string pin)
    {
        await SetPinAsync(pin);
        await TurnOnAsync();
    }

    /// <summary>
    /// Saves a first PIN and leaves the app lock as it is: locking a note needs a PIN, not the Lock screen (docs/04,
    /// Locked notes).
    /// </summary>
    /// <param name="pin">The PIN, checked for 4–8 digits first.</param>
    /// <returns>A task that completes when it is saved.</returns>
    /// <exception cref="UserFacingException">The PIN is not 4–8 digits.</exception>
    /// <exception cref="InvalidOperationException">A PIN is in use: it is changed, after a check, not set.</exception>
    public async Task SetPinAsync(string pin)
    {
        ValidatePin(pin);
        if (await PinInUseAsync())
        {
            throw new InvalidOperationException("A PIN is in use already. Check it, then change it.");
        }

        var current = await GetAsync();
        var (hash, salt) = Hash(pin);
        await SaveAsync(current with { PinHash = hash, PinSalt = salt, PinIterations = Iterations, FailedAttempts = 0, LockedUntilUtc = null });
    }

    /// <summary>
    /// Turns the lock on with the PIN there is: the one locked notes use. The caller has already checked it.
    /// </summary>
    /// <returns>A task that completes when it is saved.</returns>
    /// <exception cref="InvalidOperationException">There is no PIN.</exception>
    public async Task TurnOnAsync()
    {
        var current = await GetAsync();
        if (current.PinHash is null)
        {
            throw new InvalidOperationException("There is no PIN to turn the lock on with.");
        }

        await SaveAsync(current with { Enabled = true });
    }

    /// <summary>
    /// Turns the lock off. The caller has already checked the PIN or biometrics. The PIN stays, and stays in use while
    /// a note is locked.
    /// </summary>
    /// <returns>A task that completes when it is saved.</returns>
    public async Task DisableAsync()
    {
        var current = await GetAsync();
        await SaveAsync(current with { Enabled = false, Biometrics = false, FailedAttempts = 0, LockedUntilUtc = null });
    }

    /// <summary>Changes the PIN. The caller has already checked the current one.</summary>
    /// <param name="pin">The new PIN, checked for 4–8 digits first.</param>
    /// <returns>A task that completes when it is saved.</returns>
    /// <exception cref="UserFacingException">The PIN is not 4–8 digits.</exception>
    public async Task ChangePinAsync(string pin)
    {
        ValidatePin(pin);
        var current = await GetAsync();
        var (hash, salt) = Hash(pin);
        await SaveAsync(current with { PinHash = hash, PinSalt = salt, PinIterations = Iterations, FailedAttempts = 0, LockedUntilUtc = null });
    }

    /// <summary>Turns biometric unlock on or off. Turning it on is done only after one biometric check succeeds.</summary>
    /// <param name="on">Whether it is allowed.</param>
    /// <returns>A task that completes when it is saved.</returns>
    public async Task SetBiometricsAsync(bool on)
    {
        var current = await GetAsync();
        await SaveAsync(current with { Biometrics = on });
    }

    /// <summary>Changes how long the app may stay in the background before it locks.</summary>
    /// <param name="seconds">One of <see cref="AppLockSettings.LockAfterChoices"/>.</param>
    /// <returns>A task that completes when it is saved.</returns>
    public async Task SetLockAfterAsync(int seconds)
    {
        var current = await GetAsync();
        await SaveAsync(current with { LockAfterSeconds = seconds });
    }

    /// <summary>How long until the PIN field may be tried again.</summary>
    /// <returns>The remaining time, or null when it is not locked out.</returns>
    public async Task<TimeSpan?> LockoutRemainingAsync()
    {
        var settings = await GetAsync();
        if (settings.LockedUntilUtc is not { } until)
        {
            return null;
        }

        var remaining = until - time.GetUtcNow().UtcDateTime;
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    /// <summary>Checks a typed PIN, updating the tries counter and lockout.</summary>
    /// <param name="pin">What was typed.</param>
    /// <returns>Whether it matches.</returns>
    public async Task<bool> VerifyPinAsync(string pin)
    {
        var settings = await GetAsync();
        if (settings.LockedUntilUtc > time.GetUtcNow().UtcDateTime || settings.PinHash is null || settings.PinSalt is null)
        {
            return false;
        }

        var ok = CryptographicOperations.FixedTimeEquals(
            Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(settings.PinSalt), settings.PinIterations, HashAlgorithmName.SHA256, HashSize),
            Convert.FromBase64String(settings.PinHash));

        if (ok)
        {
            await SaveAsync(settings with { FailedAttempts = 0, LockedUntilUtc = null });
            return true;
        }

        var attempts = settings.FailedAttempts + 1;
        var lockout = LockoutFor(attempts);
        await SaveAsync(settings with
        {
            FailedAttempts = attempts,
            LockedUntilUtc = lockout is { } wait ? time.GetUtcNow().UtcDateTime + wait : settings.LockedUntilUtc,
        });
        return false;
    }

    /// <summary>Checks a PIN is 4–8 digits.</summary>
    /// <param name="pin">The PIN.</param>
    /// <exception cref="UserFacingException">It is not 4–8 digits.</exception>
    public static void ValidatePin(string pin)
    {
        if (pin.Length is < MinPinLength or > MaxPinLength || !pin.All(char.IsAsciiDigit))
        {
            throw new UserFacingException("Use 4 to 8 digits.");
        }
    }

    /// <summary>
    /// The lockout a tries count starts, at the 5th wrong PIN and every 5th after: 30 s, 1 min, 2 min, …, capped at
    /// 15 min. Between those, the field stays locked out until the earlier wait expires.
    /// </summary>
    /// <param name="failedAttempts">Wrong PINs in a row, after the latest one.</param>
    /// <returns>A new wait, or null when this attempt does not start one.</returns>
    private static TimeSpan? LockoutFor(int failedAttempts)
    {
        if (failedAttempts < 5 || failedAttempts % 5 != 0)
        {
            return null;
        }

        var level = failedAttempts / 5;
        var seconds = Math.Min(30 * Math.Pow(2, level - 1), 900);
        return TimeSpan.FromSeconds(seconds);
    }

    private static (string Hash, string Salt) Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    private Task SaveAsync(AppLockSettings settings) =>
        storage.Database.InTransactionAsync((connection, transaction) =>
        {
            SettingsStore.Set(connection, Key, settings, transaction);
            return true;
        });
}
