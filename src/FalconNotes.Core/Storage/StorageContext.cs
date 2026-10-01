using FalconNotes.Core.Crypto;

namespace FalconNotes.Core.Storage;

/// <summary>
/// The open database, the keys and the installation ID, once start-up has found them (docs/02, Start-up sequence).
/// Services read them from here; before start-up finishes, or on the Key lost and Lock screens, nothing may.
/// </summary>
public sealed class StorageContext
{
    private Database? _database;
    private KeyMaterial? _keys;

    /// <summary>Whether start-up has opened the database.</summary>
    public bool IsReady => _database is not null;

    /// <summary>The open database.</summary>
    /// <exception cref="InvalidOperationException">Start-up has not opened it.</exception>
    public Database Database => _database ?? throw new InvalidOperationException("The database is not open yet.");

    /// <summary>The keys derived from the device key.</summary>
    /// <exception cref="InvalidOperationException">Start-up has not opened the database.</exception>
    public KeyMaterial Keys => _keys ?? throw new InvalidOperationException("The database is not open yet.");

    /// <summary>The installation's random ID, bound into every attachment file as its owner (docs/03, Keys).</summary>
    public Guid InstallationId { get; private set; }

    /// <summary>Called by start-up once the database is open and migrated.</summary>
    /// <param name="database">The database.</param>
    /// <param name="keys">The derived keys; this context owns them from now on.</param>
    /// <param name="installationId">The installation ID.</param>
    public void Open(Database database, KeyMaterial keys, Guid installationId)
    {
        _keys?.Dispose();
        _database = database;
        _keys = keys;
        InstallationId = installationId;
    }

    /// <summary>Forgets the database and wipes the keys, as erasing all data does.</summary>
    public void Close()
    {
        _keys?.Dispose();
        _keys = null;
        _database = null;
        InstallationId = Guid.Empty;
    }
}
