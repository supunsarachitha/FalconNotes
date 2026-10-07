namespace FalconNotes.Core.Platform;

/// <summary>Where the app keeps its files on this device (docs/03, Where things are; docs/12, Shared).</summary>
public interface IAppDirectories
{
    /// <summary>Private data: the database, attachments and database copies (<c>FileSystem.AppDataDirectory</c>).</summary>
    string DataDirectory { get; }

    /// <summary>Private cache: exports being written, restores being read, decrypted copies for Open.</summary>
    string CacheDirectory { get; }
}
