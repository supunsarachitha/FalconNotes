using System.Globalization;
using System.Text.RegularExpressions;
using FalconNotes.Core.Domain;

namespace FalconNotes.Core.Text;

/// <summary>
/// Dates as the app writes them: the formats offered in Settings, local day keys (<c>yyyy-MM-dd</c>) and the week's
/// first day. Port of <c>web/lib/dates.ts</c> (docs/04, Dates and numbers). Names are always English.
/// </summary>
public static partial class DateFormats
{
    /// <summary>The date formats offered in Settings, as .NET-style patterns, in the order they are offered.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        "yyyy-MM-dd",
        "dd/MM/yyyy",
        "MM/dd/yyyy",
        "dd.MM.yyyy",
        "d MMM yyyy",
        "MMM d, yyyy",
        "dddd, d MMMM yyyy",
        "dddd, MMMM d, yyyy",
    ];

    private static readonly string[] Days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly string[] Months =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    ];

    /// <summary>Formats a date with one of the offered formats (tokens <c>yyyy MMMM MMM MM dddd dd d</c>).</summary>
    /// <param name="date">The date.</param>
    /// <param name="format">The pattern.</param>
    /// <returns>The formatted date, with English names.</returns>
    public static string Format(DateOnly date, string format)
    {
        var month = Months[date.Month - 1];
        return Tokens().Replace(format, match => match.Value switch
        {
            "yyyy" => date.Year.ToString("D4", CultureInfo.InvariantCulture),
            "MMMM" => month,
            "MMM" => month[..3],
            "MM" => date.Month.ToString("D2", CultureInfo.InvariantCulture),
            "dddd" => Days[(int)date.DayOfWeek],
            "dd" => date.Day.ToString("D2", CultureInfo.InvariantCulture),
            _ => date.Day.ToString(CultureInfo.InvariantCulture),
        });
    }

    /// <summary>Reads a <c>yyyy-MM-dd</c> key, or null when it is not a real date.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The date, or null.</returns>
    public static DateOnly? ParseKey(string? key) =>
        key is not null && KeyPattern().IsMatch(key)
        && DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>A date as <c>yyyy-MM-dd</c>.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The key.</returns>
    public static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The local calendar day of an instant in a time zone, as <c>yyyy-MM-dd</c>: it names a daily note.</summary>
    /// <param name="instant">The instant.</param>
    /// <param name="zone">The device's time zone.</param>
    /// <returns>The key.</returns>
    public static string LocalKey(DateTimeOffset instant, TimeZoneInfo zone) =>
        Key(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime));

    /// <summary>
    /// The instants (UTC) a local day starts and the next one starts, for the day filter. When midnight does not exist
    /// (a daylight-saving gap), the day starts at the first valid minute, as a browser's local date does.
    /// </summary>
    /// <param name="day">The day.</param>
    /// <param name="zone">The device's time zone.</param>
    /// <returns>The start, inclusive, and the end, exclusive.</returns>
    public static (DateTime StartUtc, DateTime EndUtc) DayRange(DateOnly day, TimeZoneInfo zone) =>
        (StartOfDayUtc(day, zone), StartOfDayUtc(day.AddDays(1), zone));

    /// <summary>The first day of the week for a choice, as a <see cref="DayOfWeek"/>.</summary>
    /// <param name="choice">The user's choice.</param>
    /// <param name="culture">The device's culture, used for <see cref="WeekStart.Auto"/>.</param>
    /// <returns>The first day.</returns>
    public static DayOfWeek WeekStartDay(WeekStart choice, CultureInfo culture) => choice switch
    {
        WeekStart.Sunday => DayOfWeek.Sunday,
        WeekStart.Monday => DayOfWeek.Monday,
        WeekStart.Saturday => DayOfWeek.Saturday,
        _ => culture.DateTimeFormat.FirstDayOfWeek,
    };

    private static DateTime StartOfDayUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    [GeneratedRegex("yyyy|MMMM|MMM|MM|dddd|dd|d", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
