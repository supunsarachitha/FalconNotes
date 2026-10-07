using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Storage;

/// <summary>
/// Small helpers for hand-written SQL: parameters, column values and the stored forms of IDs, times and dates
/// (docs/03, Schema: times are UTC ticks, IDs lower-case canonical text, dates <c>yyyy-MM-dd</c>).
/// </summary>
public static class Sql
{
    /// <summary>Creates a command with its text, in a transaction when one is given.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="text">The SQL.</param>
    /// <param name="transaction">The transaction, if any.</param>
    /// <returns>The command; dispose it.</returns>
    public static SqliteCommand Command(SqliteConnection connection, string text, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = text;
        command.Transaction = transaction;
        return command;
    }

    /// <summary>Adds a parameter; null becomes SQL NULL.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter's name, with its <c>$</c>.</param>
    /// <param name="value">The value.</param>
    /// <returns>The command, for chaining.</returns>
    public static SqliteCommand With(this SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    /// <summary>
    /// Adds <c>$prefix0</c>, <c>$prefix1</c>… for each value and returns the list to put in <c>IN (…)</c>.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="prefix">The parameters' prefix, with its <c>$</c>.</param>
    /// <param name="values">The values.</param>
    /// <returns>For example <c>$id0, $id1</c>; <c>NULL</c> when there are none, which matches nothing.</returns>
    public static string InList(this SqliteCommand command, string prefix, IEnumerable<object> values)
    {
        var names = new List<string>();
        foreach (var value in values)
        {
            var name = prefix + names.Count.ToString(CultureInfo.InvariantCulture);
            command.Parameters.AddWithValue(name, value);
            names.Add(name);
        }

        return names.Count == 0 ? "NULL" : string.Join(", ", names);
    }

    /// <summary>An ID as stored: lower-case canonical text.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The text.</returns>
    public static string Id(Guid id) => id.ToString("D");

    /// <summary>A UTC time as stored: ticks.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>The ticks.</returns>
    public static long Time(DateTime utc) => utc.Ticks;

    /// <summary>A date as stored.</summary>
    /// <param name="date">The date, or null.</param>
    /// <returns>The text, or null.</returns>
    public static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Reads an ID column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column.</param>
    /// <returns>The ID.</returns>
    public static Guid GetId(this SqliteDataReader reader, int ordinal) => Guid.ParseExact(reader.GetString(ordinal), "D");

    /// <summary>Reads a nullable ID column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column.</param>
    /// <returns>The ID, or null.</returns>
    public static Guid? GetIdOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetId(ordinal);

    /// <summary>Reads a time column (UTC ticks).</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column.</param>
    /// <returns>The UTC time.</returns>
    public static DateTime GetTime(this SqliteDataReader reader, int ordinal) => new(reader.GetInt64(ordinal), DateTimeKind.Utc);

    /// <summary>Reads a nullable time column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column.</param>
    /// <returns>The UTC time, or null.</returns>
    public static DateTime? GetTimeOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetTime(ordinal);

    /// <summary>Reads a nullable date column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column.</param>
    /// <returns>The date, or null.</returns>
    public static DateOnly? GetDateOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateOnly.ParseExact(reader.GetString(ordinal), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
