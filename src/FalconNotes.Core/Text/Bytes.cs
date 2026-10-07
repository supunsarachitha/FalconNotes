using System.Globalization;

namespace FalconNotes.Core.Text;

/// <summary>Byte sizes as the app shows them. Port of <c>formatBytes</c> in <c>web/lib/format.ts</c>.</summary>
public static class Bytes
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    /// <summary>"512 B", "1.4 KB", "23 MB", "1.2 GB": binary units, one decimal under 10.</summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The description.</returns>
    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        var value = bytes / 1024.0;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var number = value < 10
            ? value.ToString("0.0", CultureInfo.InvariantCulture)
            : Math.Floor(value + 0.5).ToString(CultureInfo.InvariantCulture);
        return $"{number} {Units[unit]}";
    }
}
