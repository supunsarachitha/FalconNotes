using System.Globalization;

namespace FalconNotes.Core.Text;

/// <summary>
/// Times as note headers and tooltips show them. Port of <c>web/lib/format.ts</c> (docs/04, Dates and numbers), in
/// English as <c>Intl.RelativeTimeFormat("en", { numeric: "auto" })</c> writes it.
/// </summary>
public static class RelativeTime
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>"just now", "5 minutes ago", "yesterday", "Sep 12", "Sep 12, 2024": compact, feed-style.</summary>
    /// <param name="time">The time to describe.</param>
    /// <param name="now">The current time.</param>
    /// <param name="zone">The device's time zone, for the date and the year.</param>
    /// <returns>The description.</returns>
    public static string Format(DateTimeOffset time, DateTimeOffset now, TimeZoneInfo zone)
    {
        var seconds = RoundLikeJavaScript((time - now).TotalSeconds);
        var abs = Math.Abs(seconds);
        if (abs < 45)
        {
            return "just now";
        }

        if (abs < 3600)
        {
            return Unit(RoundLikeJavaScript(seconds / 60.0), "minute");
        }

        if (abs < 86400)
        {
            return Unit(RoundLikeJavaScript(seconds / 3600.0), "hour");
        }

        if (abs < 7 * 86400)
        {
            var days = RoundLikeJavaScript(seconds / 86400.0);
            return days switch
            {
                -1 => "yesterday",
                1 => "tomorrow",
                _ => Unit(days, "day"),
            };
        }

        var local = TimeZoneInfo.ConvertTime(time, zone);
        return local.Year == TimeZoneInfo.ConvertTime(now, zone).Year
            ? local.ToString("MMM d", English)
            : local.ToString("MMM d, yyyy", English);
    }

    /// <summary>Full date and time, for tooltips and settings: "Sep 28, 2026, 2:30 PM" or "Sep 28, 2026, 14:30".</summary>
    /// <param name="time">The time.</param>
    /// <param name="zone">The device's time zone.</param>
    /// <param name="use24Hour">Whether the device uses a 24-hour clock.</param>
    /// <returns>The description.</returns>
    public static string Absolute(DateTimeOffset time, TimeZoneInfo zone, bool use24Hour) =>
        TimeZoneInfo.ConvertTime(time, zone).ToString(use24Hour ? "MMM d, yyyy, HH:mm" : "MMM d, yyyy, h:mm tt", English);

    private static string Unit(long value, string unit)
    {
        var count = Math.Abs(value);
        var plural = count == 1 ? unit : unit + "s";
        return value < 0 ? $"{count} {plural} ago" : $"in {count} {plural}";
    }

    /// <summary>JavaScript's <c>Math.round</c>: halves go up (towards positive infinity).</summary>
    private static long RoundLikeJavaScript(double value) => (long)Math.Floor(value + 0.5);
}
