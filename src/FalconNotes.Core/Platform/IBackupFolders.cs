namespace FalconNotes.Core.Platform;

/// <summary>A folder the user chose for automatic backups (docs/05, Automatic backups).</summary>
/// <param name="Reference">What the platform needs to reach the folder again after a restart (Android: its tree URI).</param>
/// <param name="Name">The folder's name, to show in Settings.</param>
public sealed record BackupFolder(string Reference, string Name);

/// <summary>
/// The chosen folder cannot be reached any more: it was deleted or moved, its drive is not there, or the access the
/// user gave was taken back. Choosing the folder again is the only way on.
/// </summary>
/// <param name="inner">What the platform reported.</param>
public sealed class BackupFolderUnavailableException(Exception? inner = null)
    : IOException("The backup folder cannot be reached.", inner);

/// <summary>
/// A folder outside the app that the user chose once and the app may write to from then on, for automatic backups
/// (docs/02, Platform services; docs/12). No storage permission is needed: the user grants one folder in the system's
/// picker. A platform without it registers none, and Settings then does not offer automatic backups.
/// </summary>
public interface IBackupFolders
{
    /// <summary>Lets the user choose a folder, and keeps the access to it across restarts.</summary>
    /// <returns>The folder, or null when the user cancelled.</returns>
    Task<BackupFolder?> ChooseAsync();

    /// <summary>Lists the names of the files in the folder.</summary>
    /// <param name="folder">A folder's <see cref="BackupFolder.Reference"/>.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The file names.</returns>
    /// <exception cref="BackupFolderUnavailableException">The folder cannot be reached.</exception>
    Task<IReadOnlyList<string>> ListAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>Writes a file into the folder, replacing one of the same name. A file that fails part-way is removed.</summary>
    /// <param name="folder">A folder's <see cref="BackupFolder.Reference"/>.</param>
    /// <param name="fileName">The file's name.</param>
    /// <param name="content">The file's content, read from its current position to the end.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="BackupFolderUnavailableException">The folder cannot be reached.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    Task WriteAsync(string folder, string fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Deletes a file from the folder. A file that is not there is not an error.</summary>
    /// <param name="folder">A folder's <see cref="BackupFolder.Reference"/>.</param>
    /// <param name="fileName">The file's name.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>A task that completes when the file is gone.</returns>
    /// <exception cref="BackupFolderUnavailableException">The folder cannot be reached.</exception>
    Task DeleteAsync(string folder, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Gives up the access to a folder that is no longer used. Never throws.</summary>
    /// <param name="folder">A folder's <see cref="BackupFolder.Reference"/>.</param>
    void Release(string folder);
}
