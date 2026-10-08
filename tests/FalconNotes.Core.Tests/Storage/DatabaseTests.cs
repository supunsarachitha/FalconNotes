using System.Security.Cryptography;
using System.Text;
using FalconNotes.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FalconNotes.Core.Tests.Storage;

public class DatabaseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(Database.KeySizeBytes);

    private static async Task WriteRowAsync(Database database, string value)
    {
        await using var connection = await database.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS T (V TEXT NOT NULL); INSERT INTO T VALUES ($v);";
        command.Parameters.AddWithValue("$v", value);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<string?> ReadRowAsync(Database database)
    {
        await using var connection = await database.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT V FROM T";
        return (string?)await command.ExecuteScalarAsync(Ct);
    }

    [Fact]
    public async Task Reopens_with_the_same_key()
    {
        using var dir = new TempDirectory();
        var key = NewKey();

        await WriteRowAsync(new Database(dir.File("falcon.db"), key, pooling: false), "hello");

        Assert.Equal("hello", await ReadRowAsync(new Database(dir.File("falcon.db"), key, pooling: false)));
    }

    [Fact]
    public async Task The_file_is_encrypted()
    {
        using var dir = new TempDirectory();
        await WriteRowAsync(new Database(dir.File("falcon.db"), NewKey(), pooling: false), "a secret note");
        SqliteConnection.ClearAllPools();

        var bytes = await File.ReadAllBytesAsync(dir.File("falcon.db"), Ct);
        Assert.NotEqual("SQLite format 3\0"u8.ToArray(), bytes[..16]);
        Assert.DoesNotContain("a secret note", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task A_wrong_key_is_reported_as_DatabaseKeyException()
    {
        using var dir = new TempDirectory();
        await WriteRowAsync(new Database(dir.File("falcon.db"), NewKey(), pooling: false), "x");

        await Assert.ThrowsAsync<DatabaseKeyException>(() =>
            new Database(dir.File("falcon.db"), NewKey(), pooling: false).OpenAsync(Ct));
    }

    [Fact]
    public async Task The_AEGIS_variant_is_pinned_to_AEGIS_256()
    {
        using var dir = new TempDirectory();
        var key = NewKey();
        var path = dir.File("other.db");
        var other = new SqliteConnectionStringBuilder
        {
            DataSource = $"file:{Database.ToUriPath(path)}?cipher=aegis&algorithm=aegis-128l",
            Password = $"x'{Convert.ToHexString(key)}'",
            Pooling = false,
        }.ToString();
        await using (var connection = new SqliteConnection(other))
        {
            connection.Open();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE T (V)";
            command.ExecuteNonQuery();
        }

        await Assert.ThrowsAsync<DatabaseKeyException>(() => new Database(path, key, pooling: false).OpenAsync(Ct));
    }

    [Fact]
    public async Task Connections_use_the_app_pragmas()
    {
        using var dir = new TempDirectory();
        await using var connection = await new Database(dir.File("falcon.db"), NewKey(), pooling: false).OpenAsync(Ct);

        Assert.Equal("wal", Pragma(connection, "journal_mode"));
        Assert.Equal("1", Pragma(connection, "synchronous"));
        Assert.Equal("1", Pragma(connection, "foreign_keys"));
        Assert.Equal("1", Pragma(connection, "secure_delete"));
        Assert.Equal("2", Pragma(connection, "temp_store"));
    }

    [Fact]
    public void Keys_must_be_256_bits() =>
        Assert.Throws<ArgumentException>(() => new Database("x.db", new byte[16]));

    [Theory]
    [InlineData("/data/user/0/lk.stechbuzz.falconnotes/files/falcon.db", "/data/user/0/lk.stechbuzz.falconnotes/files/falcon.db")]
    [InlineData(@"C:\Users\Me\falcon.db", "/C:/Users/Me/falcon.db")]
    [InlineData("/a/100%/b?c#d.db", "/a/100%25/b%3fc%23d.db")]
    public void Paths_become_safe_uri_paths(string path, string expected) =>
        Assert.Equal(expected, Database.ToUriPath(path));

    private static string Pragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name}";
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
