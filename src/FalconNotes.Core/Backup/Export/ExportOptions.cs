using FalconNotes.Core.Domain;

namespace FalconNotes.Core.Backup.Export;

/// <summary>Export options (Settings → Backup &amp; data; docs/05, Options).</summary>
/// <param name="Format">The notes' file format.</param>
/// <param name="Layout">The folder layout.</param>
/// <param name="IncludeArchived">Whether archived notes are included.</param>
/// <param name="IncludeAttachments">Whether files are included.</param>
/// <param name="From">The first creation date included, in <paramref name="TimeZone"/>.</param>
/// <param name="To">The last creation date included, in <paramref name="TimeZone"/>.</param>
/// <param name="TimeZone">The time zone of folder names, file names and times.</param>
public sealed record ExportOptions(
    ExportFormat Format,
    ExportLayout Layout,
    bool IncludeArchived,
    bool IncludeAttachments,
    DateOnly? From,
    DateOnly? To,
    TimeZoneInfo TimeZone)
{
    /// <summary>
    /// The time zone's IANA name, as the manifest records it. On Windows a zone's ID is a Windows name ("W. Europe
    /// Standard Time"), so it is converted; anything that has no IANA name is UTC (docs/05, Manifest).
    /// </summary>
    public string TimeZoneName => IanaName(TimeZone);

    /// <summary>Checks the options before anything is written.</summary>
    /// <exception cref="UserFacingException">The start date is after the end date.</exception>
    public void Validate()
    {
        if (From > To)
        {
            throw new UserFacingException("The start date must not be after the end date.", "from");
        }
    }

    /// <summary>A time zone's IANA name.</summary>
    /// <param name="zone">The zone.</param>
    /// <returns>Its IANA name, or "UTC".</returns>
    public static string IanaName(TimeZoneInfo zone)
    {
        if (zone.HasIanaId)
        {
            return zone.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : "UTC";
    }
}

/// <summary>What an export wrote.</summary>
/// <param name="Notes">Notes written.</param>
/// <param name="Files">Files written.</param>
/// <param name="Problems">Files left out or incomplete, as listed in the manifest.</param>
public sealed record ExportResult(int Notes, int Files, IReadOnlyList<string> Problems);
