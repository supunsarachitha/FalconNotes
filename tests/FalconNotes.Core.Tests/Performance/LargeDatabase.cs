using System.Globalization;
using System.Text;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Storage;
using FalconNotes.Core.Text;

namespace FalconNotes.Core.Tests.Performance;

/// <summary>
/// Fills a database with generated notes, as benchmarks/storage/Shared/Bench.cs <c>Generate</c> does (docs/13): seeded,
/// over three years, 8% todo lists, 7% quick notes, 12 habits, 10% archived, 2% in the trash, 5% daily notes, 20%
/// with a label, 30% with one to three files (rows only), tags from a pool of 50.
/// </summary>
internal static class LargeDatabase
{
    private static readonly string[] Words = ("maple syrup meeting budget garden project idea call email review plan draft travel " +
        "book read write code fix release design colour coffee walk run family weekend dinner recipe café résumé " +
        "naïve 日本語 テキスト report invoice doctor school train weather photo video music film list notes later today " +
        "tomorrow important quick thought remember buy sell price market news article link question answer").Split(' ');

    private static readonly string[] TagPool = Enumerable.Range(0, 40).Select(i => $"topic{i}")
        .Concat(["work", "work/meetings", "work/1on1", "home", "home/garden", "ideas", "groceries", "health", "travel/japan", "reading"])
        .ToArray();

    public static async Task<IReadOnlyList<Guid>> FillAsync(Database database, int count, DateTime now)
    {
        var rng = new Random(42);
        var start = now.AddYears(-3);
        var span = (now - start).TotalSeconds;
        var times = Enumerable.Range(0, count).Select(_ => start.AddSeconds(rng.NextDouble() * span)).Order().ToList();
        var labelIds = Enumerable.Range(0, 12).Select(_ => Guid.CreateVersion7()).ToList();
        var daily = new HashSet<string>();

        return await database.InTransactionAsync((connection, transaction) =>
        {
            for (var i = 0; i < labelIds.Count; i++)
            {
                using var label = Sql.Command(connection, "INSERT INTO Labels (Id, Name, Color, CreatedAt) VALUES ($id, $name, 'Blue', $t)", transaction);
                label.With("$id", Sql.Id(labelIds[i])).With("$name", $"Label {i}").With("$t", Sql.Time(start)).ExecuteNonQuery();
            }

            using var note = Sql.Command(connection, """
                INSERT INTO Notes (Id, Kind, DailyDate, IsPinned, ArchivedAt, TrashedAt, CreatedAt, UpdatedAt, ContentBytes)
                VALUES ($id, $kind, $daily, $pinned, $archived, $trashed, $created, $updated, $bytes);
                INSERT INTO NoteBodies (NoteId, Content) VALUES ($id, $content);
                """, transaction);
            string[] parameters = ["$id", "$kind", "$daily", "$pinned", "$archived", "$trashed", "$created", "$updated", "$bytes", "$content"];
            foreach (var name in parameters)
            {
                note.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter { ParameterName = name });
            }

            using var tag = Sql.Command(connection, """
                INSERT INTO Tags (Name) VALUES ($name) ON CONFLICT (Name) DO NOTHING;
                INSERT OR IGNORE INTO NoteTags (NoteId, TagId) SELECT $note, Id FROM Tags WHERE Name = $name;
                """, transaction);
            tag.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter { ParameterName = "$name" });
            tag.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter { ParameterName = "$note" });
            using var labelLink = Sql.Command(connection, "INSERT INTO NoteLabels (NoteId, LabelId) VALUES ($note, $label)", transaction);
            labelLink.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter { ParameterName = "$note" });
            labelLink.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter { ParameterName = "$label" });
            using var file = Sql.Command(connection, """
                INSERT INTO Attachments (Id, NoteId, FileName, ContentType, SizeBytes, StorageKey, CreatedAt)
                VALUES ($id, $note, $name, 'image/jpeg', $size, $key, $created)
                """, transaction);
            foreach (var name in new[] { "$id", "$note", "$name", "$size", "$key", "$created" })
            {
                file.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter { ParameterName = name });
            }

            var ids = new List<Guid>(count);
            for (var i = 0; i < count; i++)
            {
                var created = times[i];
                var id = Guid.CreateVersion7(new DateTimeOffset(created));
                ids.Add(id);
                var roll = rng.NextDouble();
                var kind = i < 12 ? NoteKind.Habit : roll < 0.08 ? NoteKind.Todo : roll < 0.15 ? NoteKind.Quick : NoteKind.Note;
                var size = rng.NextDouble() switch { < 0.6 => rng.Next(40, 200), < 0.9 => rng.Next(200, 1000), _ => rng.Next(1000, 5000) };
                var text = new StringBuilder();
                if (kind == NoteKind.Habit)
                {
                    text.Append("# Habit ").Append(i).Append("\n\n");
                    for (var d = 0; d < 300; d++)
                    {
                        if (rng.NextDouble() < 0.6)
                        {
                            text.Append("- ").Append(DateOnly.FromDateTime(created.AddDays(d)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
                        }
                    }
                }
                else if (kind == NoteKind.Todo)
                {
                    text.Append("# List ").Append(i).Append("\n\n");
                    for (var k = rng.Next(2, 12); k > 0; k--)
                    {
                        text.Append(rng.NextDouble() < 0.4 ? "- [x] " : "- [ ] ").Append(Words[rng.Next(Words.Length)]).Append('\n');
                    }
                }

                while (text.Length < size)
                {
                    text.Append(Words[rng.Next(Words.Length)]).Append(rng.NextDouble() < 0.1 ? ".\n" : " ");
                    if (rng.NextDouble() < 0.02)
                    {
                        text.Append('#').Append(TagPool[rng.Next(TagPool.Length)]).Append(' ');
                    }
                }

                var content = text.ToString().TrimEnd();
                var archived = rng.NextDouble() < 0.10 ? created.AddDays(1) : (DateTime?)null;
                var trashed = rng.NextDouble() < 0.02 ? now.AddDays(-rng.Next(0, 29)) : (DateTime?)null;
                string? dailyDate = null;
                if (kind == NoteKind.Note && trashed is null && rng.NextDouble() < 0.05)
                {
                    var day = DateOnly.FromDateTime(created).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (daily.Add(day))
                    {
                        dailyDate = day;
                    }
                }

                object?[] values = [Sql.Id(id), (int)kind, dailyDate, i % 400 == 7 ? 1 : 0, archived?.Ticks, trashed?.Ticks,
                    created.Ticks, (rng.NextDouble() < 0.2 ? created.AddMinutes(rng.Next(2, 10_000)) : created).Ticks,
                    Encoding.UTF8.GetByteCount(content), content];
                for (var p = 0; p < values.Length; p++)
                {
                    note.Parameters[p].Value = values[p] ?? DBNull.Value;
                }

                note.ExecuteNonQuery();
                foreach (var name in TagParser.Extract(content))
                {
                    tag.Parameters[0].Value = name;
                    tag.Parameters[1].Value = Sql.Id(id);
                    tag.ExecuteNonQuery();
                }

                if (rng.NextDouble() < 0.2)
                {
                    labelLink.Parameters[0].Value = Sql.Id(id);
                    labelLink.Parameters[1].Value = Sql.Id(labelIds[rng.Next(labelIds.Count)]);
                    labelLink.ExecuteNonQuery();
                }

                if (rng.NextDouble() < 0.3)
                {
                    for (var f = rng.Next(1, 4); f > 0; f--)
                    {
                        var fileId = Guid.CreateVersion7();
                        object[] fileValues = [Sql.Id(fileId), Sql.Id(id), $"photo-{i}-{f}.jpg", rng.Next(50_000, 4_000_000),
                            Core.Attachments.AttachmentStore.CreateStorageKey(fileId), created.AddSeconds(f).Ticks];
                        for (var p = 0; p < fileValues.Length; p++)
                        {
                            file.Parameters[p].Value = fileValues[p];
                        }

                        file.ExecuteNonQuery();
                    }
                }
            }

            return (IReadOnlyList<Guid>)ids;
        });
    }
}
