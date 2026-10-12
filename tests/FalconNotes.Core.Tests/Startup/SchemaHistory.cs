using System.Globalization;
using FalconNotes.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Tests.Startup;

/// <summary>
/// Takes a test database back to the schema an earlier version of the app wrote, so a test can start the app on it as
/// an update does (docs/11, Updates keep every note). Each migration has its undo here; a migration added without one
/// fails <see cref="UpgradeKeepsNotesTests.Every_migration_has_its_undo_so_every_update_is_tested"/>.
/// </summary>
internal static class SchemaHistory
{
    /// <summary>What undoes each migration after the first: <c>Undo[0]</c> takes version 2 back to version 1.</summary>
    public static readonly string[] Undo =
    [
        "ALTER TABLE Labels DROP COLUMN HideNotes;",
        "DROP INDEX IX_Notes_Locked; ALTER TABLE Notes DROP COLUMN IsLocked;",
    ];

    /// <summary>Undoes every migration after <paramref name="version"/>, newest first, keeping the rows.</summary>
    public static void RewindTo(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = string.Concat(Undo.Take(Migrations.Latest - 1).Skip(version - 1).Reverse())
            + $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    /// <summary>The tables of a database, with the columns each has.</summary>
    public static Dictionary<string, string[]> Tables(SqliteConnection connection)
    {
        var tables = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var table in Column(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"))
        {
            tables[table] = [.. Column(connection, $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid")];
        }

        return tables;
    }

    /// <summary>Every row of these tables and columns as text, in a fixed order: what an update must leave as it was.</summary>
    public static List<string> Rows(SqliteConnection connection, Dictionary<string, string[]> tables)
    {
        var rows = new List<string>();
        foreach (var (table, columns) in tables.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var quoted = string.Join(" || '|' || ", columns.Select(c => $"quote({c})"));
            rows.AddRange(Column(connection, $"SELECT '{table}: ' || {quoted} FROM {table} ORDER BY 1"));
        }

        return rows;
    }

    private static List<string> Column(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return values;
    }
}
