namespace FalconNotes.Core.Backup.Export;

/// <summary>
/// Automatic backups' settings and state (docs/05, Automatic backups): <c>Settings['autoExport']</c>. Off unless the
/// user turns them on, because the backups are not encrypted.
/// </summary>
/// <param name="Enabled">Whether backups are made automatically. Only ever true with a <paramref name="Folder"/>.</param>
/// <param name="Folder">The chosen folder, as the platform refers to it; null while off.</param>
/// <param name="FolderName">The folder's name, to show.</param>
/// <param name="EveryDays">How many days apart the backups are: one of <see cref="EveryDaysChoices"/>.</param>
/// <param name="Keep">How many automatic backups stay in the folder: one of <see cref="KeepChoices"/>.</param>
/// <param name="LastRunAt">When the last backup was written to this folder; null when there has been none.</param>
/// <param name="LastError">Why the last try failed, as a sentence to show; null when it worked.</param>
public sealed record AutoExportSettings(
    bool Enabled,
    string? Folder,
    string? FolderName,
    int EveryDays,
    int Keep,
    DateTimeOffset? LastRunAt,
    string? LastError)
{
    /// <summary>Off, with "every week" and "the newest 3" ready for when it is turned on.</summary>
    public static readonly AutoExportSettings Default = new(false, null, null, 7, 3, null, null);

    /// <summary>The choices under "How often" (docs/07): every day, week or month.</summary>
    public static readonly IReadOnlyList<int> EveryDaysChoices = [1, 7, 30];

    /// <summary>The choices under "Keep" (docs/07).</summary>
    public static readonly IReadOnlyList<int> KeepChoices = [3, 5, 10];

    /// <summary>The settings with anything that is not offered put back to its default, and off without a folder.</summary>
    /// <returns>Settings safe to act on.</returns>
    public AutoExportSettings Normalised() => this with
    {
        Enabled = Enabled && Folder is not null,
        EveryDays = EveryDaysChoices.Contains(EveryDays) ? EveryDays : Default.EveryDays,
        Keep = KeepChoices.Contains(Keep) ? Keep : Default.Keep,
    };
}

/// <summary>How one automatic backup ended.</summary>
/// <param name="Result">What was written, when it worked.</param>
/// <param name="Error">Why it failed, as a sentence to show; null when it worked.</param>
public sealed record AutoExportOutcome(ExportResult? Result, string? Error);
