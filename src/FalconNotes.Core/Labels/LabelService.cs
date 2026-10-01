using System.Globalization;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Events;
using FalconNotes.Core.Storage;

namespace FalconNotes.Core.Labels;

/// <summary>A label and how many notes carry it.</summary>
/// <param name="Label">The label.</param>
/// <param name="NoteCount">Active notes of the enabled kinds that carry it.</param>
public sealed record LabelCount(Label Label, int NoteCount);

/// <summary>
/// Creates, renames, recolours and deletes labels (docs/04, Labels). Adapted from the server's <c>LabelService</c>.
/// Labels go on notes through <see cref="Notes.NoteService.PatchAsync"/>.
/// </summary>
/// <param name="storage">The open database.</param>
/// <param name="feed">Change events.</param>
/// <param name="time">The clock.</param>
public sealed class LabelService(StorageContext storage, ChangeFeed feed, TimeProvider time)
{
    /// <summary>Sorts names as people expect: culture-aware, ignoring case and accents (docs/04).</summary>
    public static readonly StringComparer NameOrder =
        StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    /// <summary>Every label with its count, sorted by name, then ID.</summary>
    /// <param name="kinds">The enabled kinds, which the counts cover.</param>
    /// <returns>The labels.</returns>
    public Task<IReadOnlyList<LabelCount>> ListAsync(IReadOnlyList<NoteKind> kinds) =>
        storage.Database.ReadAsync<IReadOnlyList<LabelCount>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT l.Id, l.Name, l.Color, l.CreatedAt,
                       (SELECT COUNT(*) FROM NoteLabels nl JOIN Notes n ON n.Id = nl.NoteId
                        WHERE nl.LabelId = l.Id AND n.ArchivedAt IS NULL AND n.TrashedAt IS NULL
                          AND n.Kind IN ({command.InList("$kind", kinds.Select(k => (object)(int)k))}))
                FROM Labels l
                """;
            var labels = new List<LabelCount>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                labels.Add(new LabelCount(
                    new Label(reader.GetId(0), reader.GetString(1), ParseColor(reader.GetString(2)), reader.GetTime(3)),
                    reader.GetInt32(4)));
            }

            return labels.OrderBy(l => l.Label.Name, NameOrder).ThenBy(l => l.Label.Id.ToString("D"), StringComparer.Ordinal).ToList();
        });

    /// <summary>Creates a label.</summary>
    /// <param name="name">Its name; trimmed, 1–40 characters, not used by another label.</param>
    /// <param name="color">Its colour; null cycles through every colour but grey.</param>
    /// <returns>The new label.</returns>
    /// <exception cref="UserFacingException">The name is empty, too long or taken, or there are 100 labels.</exception>
    public async Task<Label> CreateAsync(string name, LabelColor? color = null)
    {
        var clean = ValidateName(name);
        var label = await storage.Database.InTransactionAsync((connection, transaction) =>
        {
            var existing = Names(connection, transaction);
            if (existing.Count >= LabelRules.MaxLabels)
            {
                throw new UserFacingException($"You can have at most {LabelRules.MaxLabels} labels.");
            }

            if (LabelRules.HasLabelNamed(existing, clean))
            {
                throw new UserFacingException($"You already have a label called “{clean}”.", "name");
            }

            var created = new Label(Guid.CreateVersion7(), clean, color ?? LabelRules.NextColor(existing.Count), time.GetUtcNow().UtcDateTime);
            using var insert = Sql.Command(connection,
                "INSERT INTO Labels (Id, Name, Color, CreatedAt) VALUES ($id, $name, $color, $created)", transaction);
            insert.With("$id", Sql.Id(created.Id)).With("$name", created.Name).With("$color", created.Color.ToString())
                .With("$created", Sql.Time(created.CreatedAtUtc)).ExecuteNonQuery();
            return created;
        });

        feed.RaiseLabelsChanged();
        return label;
    }

    /// <summary>Renames and/or recolours a label.</summary>
    /// <param name="id">The label.</param>
    /// <param name="name">The new name, or null to keep it.</param>
    /// <param name="color">The new colour, or null to keep it.</param>
    /// <returns>False when there is no such label.</returns>
    /// <exception cref="UserFacingException">The name is empty, too long or taken.</exception>
    public async Task<bool> UpdateAsync(Guid id, string? name = null, LabelColor? color = null)
    {
        var clean = name is null ? null : ValidateName(name);
        var found = await storage.Database.InTransactionAsync((connection, transaction) =>
        {
            if (clean is not null && LabelRules.HasLabelNamed(Names(connection, transaction), clean, except: id))
            {
                throw new UserFacingException($"You already have a label called “{clean}”.", "name");
            }

            using var update = Sql.Command(connection,
                "UPDATE Labels SET Name = COALESCE($name, Name), Color = COALESCE($color, Color) WHERE Id = $id", transaction);
            return update.With("$id", Sql.Id(id)).With("$name", clean).With("$color", color?.ToString()).ExecuteNonQuery() > 0;
        });

        if (found)
        {
            feed.RaiseLabelsChanged();
        }

        return found;
    }

    /// <summary>Deletes a label; it comes off its notes, and the notes stay.</summary>
    /// <param name="id">The label.</param>
    /// <returns>False when there is no such label.</returns>
    public async Task<bool> DeleteAsync(Guid id)
    {
        var deleted = await storage.Database.InTransactionAsync((connection, transaction) =>
        {
            using var delete = Sql.Command(connection, "DELETE FROM Labels WHERE Id = $id", transaction).With("$id", Sql.Id(id));
            return delete.ExecuteNonQuery() > 0;
        });

        if (deleted)
        {
            feed.RaiseLabelsChanged();
            feed.RaiseNotesChanged();
        }

        return deleted;
    }

    private static string ValidateName(string name)
    {
        var clean = name.Trim();
        if (clean.Length is 0 or > LabelRules.MaxNameLength)
        {
            throw new UserFacingException($"A label's name is 1 to {LabelRules.MaxNameLength} characters long.", "name");
        }

        return clean;
    }

    private static List<(Guid Id, string Name)> Names(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction)
    {
        using var command = Sql.Command(connection, "SELECT Id, Name FROM Labels", transaction);
        var names = new List<(Guid, string)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add((reader.GetId(0), reader.GetString(1)));
        }

        return names;
    }

    private static LabelColor ParseColor(string value) => Enum.TryParse<LabelColor>(value, out var color) ? color : LabelColor.Grey;
}
