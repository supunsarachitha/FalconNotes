using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Storage;

/// <summary>
/// The schema's migrations, applied in order, each in its own transaction. <c>PRAGMA user_version</c> records the last
/// one applied (docs/02, Data access). Never change a released migration: add the next one.
/// </summary>
public static class Migrations
{
    /// <summary>The schema version this build of the app expects.</summary>
    public static int Latest => Steps.Length;

    private static readonly string[] Steps =
    [
        // 1: the schema of docs/03. Note text lives in NoteBodies, so lists and counts never decrypt pages of text.
        """
        CREATE TABLE Settings (
          Key   TEXT NOT NULL PRIMARY KEY,
          Value TEXT NOT NULL
        );

        CREATE TABLE Notes (
          Id            TEXT    NOT NULL PRIMARY KEY,
          Kind          INTEGER NOT NULL,
          DailyDate     TEXT    NULL,
          IsPinned      INTEGER NOT NULL DEFAULT 0,
          ArchivedAt    INTEGER NULL,
          TrashedAt     INTEGER NULL,
          CreatedAt     INTEGER NOT NULL,
          UpdatedAt     INTEGER NOT NULL,
          Revision      INTEGER NOT NULL DEFAULT 0,
          ContentBytes  INTEGER NOT NULL
        );
        CREATE TABLE NoteBodies (
          NoteId  TEXT NOT NULL PRIMARY KEY REFERENCES Notes(Id) ON DELETE CASCADE,
          Content TEXT NOT NULL
        );
        CREATE INDEX IX_Notes_List     ON Notes (Kind, IsPinned, ArchivedAt, TrashedAt, CreatedAt, Id);
        CREATE INDEX IX_Notes_Created  ON Notes (CreatedAt, Id);
        CREATE INDEX IX_Notes_Trash    ON Notes (TrashedAt, Id) WHERE TrashedAt IS NOT NULL;
        CREATE UNIQUE INDEX IX_Notes_Daily ON Notes (DailyDate) WHERE DailyDate IS NOT NULL;

        CREATE TABLE Tags (
          Id   INTEGER PRIMARY KEY,
          Name TEXT NOT NULL UNIQUE
        );
        CREATE TABLE NoteTags (
          NoteId TEXT    NOT NULL REFERENCES Notes(Id) ON DELETE CASCADE,
          TagId  INTEGER NOT NULL REFERENCES Tags(Id)  ON DELETE CASCADE,
          PRIMARY KEY (NoteId, TagId)
        ) WITHOUT ROWID;
        CREATE INDEX IX_NoteTags_Tag ON NoteTags (TagId, NoteId);

        CREATE TABLE Labels (
          Id        TEXT    NOT NULL PRIMARY KEY,
          Name      TEXT    NOT NULL,
          Color     TEXT    NOT NULL,
          CreatedAt INTEGER NOT NULL
        );
        CREATE TABLE NoteLabels (
          NoteId  TEXT NOT NULL REFERENCES Notes(Id)  ON DELETE CASCADE,
          LabelId TEXT NOT NULL REFERENCES Labels(Id) ON DELETE CASCADE,
          PRIMARY KEY (NoteId, LabelId)
        ) WITHOUT ROWID;
        CREATE INDEX IX_NoteLabels_Label ON NoteLabels (LabelId, NoteId);

        CREATE TABLE Attachments (
          Id          TEXT    NOT NULL PRIMARY KEY,
          NoteId      TEXT    NULL REFERENCES Notes(Id) ON DELETE CASCADE,
          FileName    TEXT    NOT NULL,
          ContentType TEXT    NOT NULL,
          SizeBytes   INTEGER NOT NULL,
          StorageKey  TEXT    NOT NULL UNIQUE,
          CreatedAt   INTEGER NOT NULL,
          Revision    INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IX_Attachments_Note ON Attachments (NoteId, CreatedAt);
        """,
    ];

    /// <summary>Reads the schema version of an open database (0 for a new one).</summary>
    /// <param name="connection">An open connection.</param>
    /// <returns>The version.</returns>
    public static int ReadVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Applies every migration after the database's version, each in one transaction.</summary>
    /// <param name="connection">An open connection; not used by anyone else meanwhile.</param>
    /// <returns>The version before and after.</returns>
    /// <exception cref="InvalidOperationException">The database is newer than this build of the app.</exception>
    public static (int Before, int After) Apply(SqliteConnection connection)
    {
        var before = ReadVersion(connection);
        if (before > Latest)
        {
            throw new InvalidOperationException(
                $"The database has schema version {before}, newer than this app's {Latest}. Update the app.");
        }

        for (var version = before + 1; version <= Latest; version++)
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = Steps[version - 1] + $"\nPRAGMA user_version = {version};";
            command.ExecuteNonQuery();
            transaction.Commit();
        }

        return (before, Latest);
    }
}
