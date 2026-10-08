using System.Text;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Storage;

/// <summary>
/// Opens connections to the app's encrypted SQLite database (docs/03, Database).
/// </summary>
/// <remarks>
/// <para>
/// Encryption comes from SQLite3 Multiple Ciphers (<c>SQLite3MC.PCLRaw.bundle</c>, MIT) with its AEGIS-256 cipher,
/// which uses the CPU's AES instructions and costs little over plain SQLite (docs/13). The variant is pinned in the
/// URI (<c>algorithm=aegis-256</c>), so a library update cannot change it silently; a file written with another
/// variant does not open.
/// </para>
/// <para>
/// The key is a raw 256-bit key (<c>x'…'</c>), which skips the cipher's password stretching: the key is already random,
/// and opening a connection takes about a millisecond instead of hundreds.
/// </para>
/// <para>
/// <c>Microsoft.Data.Sqlite</c>'s async methods run synchronously, and Blazor Hybrid components run on the UI thread,
/// so opening and every query go through <see cref="Task.Run(Action)"/>. The connection string holds key material:
/// never log it or put it in an exception message.
/// </para>
/// </remarks>
public sealed class Database
{
    /// <summary>The database key's length in bytes (256 bits).</summary>
    public const int KeySizeBytes = 32;

    /// <summary>SQLite's result code for a file that is not a database, which is what a wrong key looks like.</summary>
    private const int SqliteNotADb = 26;

    private static readonly string[] Pragmas =
    [
        "PRAGMA journal_mode = WAL",
        "PRAGMA synchronous = NORMAL",
        "PRAGMA foreign_keys = ON",
        "PRAGMA secure_delete = ON",
        "PRAGMA journal_size_limit = 0", // the write-ahead log is cut back after each checkpoint, so old pages do not linger in it
        "PRAGMA temp_store = MEMORY",
    ];

    private readonly string _connectionString;

    /// <summary>Prepares to open the database at <paramref name="path"/> with <paramref name="key"/>.</summary>
    /// <param name="path">The database file. It is created on first open if it does not exist.</param>
    /// <param name="key">The 256-bit database key. It is copied into the connection string, not kept otherwise.</param>
    /// <param name="pooling">Whether to pool connections (on in the app; tests turn it off to release files).</param>
    /// <exception cref="ArgumentException">The key is not <see cref="KeySizeBytes"/> bytes long.</exception>
    public Database(string path, ReadOnlySpan<byte> key, bool pooling = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"The database key must be exactly {KeySizeBytes} bytes.", nameof(key));
        }

        Path = System.IO.Path.GetFullPath(path);
        _connectionString = BuildConnectionString(Path, key, pooling);
    }

    /// <summary>The database file's full path.</summary>
    public string Path { get; }

    /// <summary>
    /// Opens a connection with the app's pragmas applied. Dispose it when the unit of work is done; pooling keeps the
    /// key set for the next one.
    /// </summary>
    /// <param name="cancellationToken">Cancels before the connection is opened.</param>
    /// <returns>An open connection.</returns>
    /// <exception cref="DatabaseKeyException">The key does not open the file (wrong key, or not a database).</exception>
    public Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var connection = new SqliteConnection(_connectionString);
            try
            {
                connection.Open();
                using var command = connection.CreateCommand();
                foreach (var pragma in Pragmas)
                {
                    command.CommandText = pragma;
                    command.ExecuteNonQuery();
                }

                return connection;
            }
            catch (SqliteException e) when (e.SqliteErrorCode == SqliteNotADb)
            {
                connection.Dispose();
                throw new DatabaseKeyException(e);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }, cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> on a background thread with an open connection, and returns its result.
    /// </summary>
    /// <typeparam name="T">The result's type.</typeparam>
    /// <param name="work">Reads or writes through the connection.</param>
    /// <param name="cancellationToken">Cancels before the work starts.</param>
    /// <returns>The work's result.</returns>
    public async Task<T> ReadAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await Task.Run(() => work(connection), cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a background thread inside one transaction, committing when it returns and
    /// rolling back when it throws.
    /// </summary>
    /// <typeparam name="T">The result's type.</typeparam>
    /// <param name="work">Writes through the connection and transaction.</param>
    /// <param name="cancellationToken">Cancels before the work starts.</param>
    /// <returns>The work's result.</returns>
    public async Task<T> InTransactionAsync<T>(
        Func<SqliteConnection, SqliteTransaction, T> work, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await Task.Run(() =>
        {
            using var transaction = connection.BeginTransaction();
            var result = work(connection, transaction);
            transaction.Commit();
            return result;
        }, cancellationToken);
    }

    /// <summary>Builds the connection string. Contains the key: never log it.</summary>
    internal static string BuildConnectionString(string fullPath, ReadOnlySpan<byte> key, bool pooling) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = $"file:{ToUriPath(fullPath)}?cipher=aegis&algorithm=aegis-256",
            Password = $"x'{Convert.ToHexString(key)}'",
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = pooling,
        }.ToString();

    /// <summary>
    /// Converts an absolute path into the path part of an SQLite <c>file:</c> URI. Ported from the reference's
    /// <c>SqlCipherConnectionString.ToUriPath</c>.
    /// </summary>
    /// <remarks>
    /// SQLite treats <c>?</c> and <c>#</c> as URI delimiters and decodes <c>%HH</c> escapes, so those characters are
    /// percent-encoded. Windows paths use forward slashes and gain a leading slash (<c>/C:/data/falcon.db</c>).
    /// </remarks>
    /// <param name="fullPath">An absolute path.</param>
    /// <returns>The URI-safe path.</returns>
    internal static string ToUriPath(string fullPath)
    {
        var path = fullPath.Replace('\\', '/');
        if (path.Length >= 2 && path[1] == ':')
        {
            path = "/" + path;
        }

        var result = new StringBuilder(path.Length + 8);
        foreach (var c in path)
        {
            result.Append(c switch
            {
                '%' => "%25",
                '?' => "%3f",
                '#' => "%23",
                _ => c.ToString(),
            });
        }

        return result.ToString();
    }
}
