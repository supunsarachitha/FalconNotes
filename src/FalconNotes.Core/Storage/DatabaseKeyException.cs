namespace FalconNotes.Core.Storage;

/// <summary>
/// The key does not open the database: it is the wrong key, or the file is not a database. The app shows the
/// Key lost screen and deletes nothing (docs/03, Lost key).
/// </summary>
public sealed class DatabaseKeyException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="inner">SQLite's error.</param>
    public DatabaseKeyException(Exception inner)
        : base("The database cannot be opened with this key.", inner)
    {
    }
}
