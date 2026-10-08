using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Storage;

/// <summary>
/// The <c>Settings</c> table: small JSON values by key (<c>installationId</c>, <c>profile</c>, <c>preferences</c>,
/// <c>appLock</c>, <c>lastExportAt</c>, <c>autoExport</c>; docs/03, Schema).
/// </summary>
public static class SettingsStore
{
    /// <summary>camelCase JSON with enums as names; unknown keys are ignored, unknown enum names read as undefined.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new TolerantEnumConverter() },
    };

    /// <summary>Reads a setting's raw JSON.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="key">The setting.</param>
    /// <param name="transaction">The transaction, if any.</param>
    /// <returns>The JSON, or null when it is not set.</returns>
    public static string? GetRaw(SqliteConnection connection, string key, SqliteTransaction? transaction = null)
    {
        using var command = Sql.Command(connection, "SELECT Value FROM Settings WHERE Key = $key", transaction).With("$key", key);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Reads a setting.</summary>
    /// <typeparam name="T">Its type.</typeparam>
    /// <param name="connection">An open connection.</param>
    /// <param name="key">The setting.</param>
    /// <param name="transaction">The transaction, if any.</param>
    /// <returns>The value, or default when it is not set or cannot be read.</returns>
    public static T? Get<T>(SqliteConnection connection, string key, SqliteTransaction? transaction = null)
    {
        var raw = GetRaw(connection, key, transaction);
        if (raw is null)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>Stores a setting, replacing any earlier value.</summary>
    /// <typeparam name="T">Its type.</typeparam>
    /// <param name="connection">An open connection.</param>
    /// <param name="key">The setting.</param>
    /// <param name="value">The value.</param>
    /// <param name="transaction">The transaction, if any.</param>
    public static void Set<T>(SqliteConnection connection, string key, T value, SqliteTransaction? transaction = null)
    {
        using var command = Sql.Command(connection,
            "INSERT INTO Settings (Key, Value) VALUES ($key, $value) ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value",
            transaction);
        command.With("$key", key).With("$value", JsonSerializer.Serialize(value, Json)).ExecuteNonQuery();
    }
}
