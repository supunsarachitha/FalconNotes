using FalconNotes.Core.Platform;

namespace FalconNotes.Core.Tests.Fakes;

/// <summary>A folder outside the app, kept in memory: what the user chooses, what is in it, and how it fails.</summary>
public sealed class FakeBackupFolders : IBackupFolders
{
    /// <summary>What the picker answers next; null for a cancelled picker.</summary>
    public BackupFolder? Next { get; set; } = new("tree:backups", "Backups");

    /// <summary>The files in the folder, by name.</summary>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>The folder was deleted, or the access to it taken back.</summary>
    public bool Unavailable { get; set; }

    /// <summary>Thrown by the next writes, after a partial file would have been removed.</summary>
    public Exception? WriteFails { get; set; }

    /// <summary>Files that cannot be deleted.</summary>
    public HashSet<string> Undeletable { get; } = [];

    /// <summary>The folders written to, in order.</summary>
    public List<string> WrittenTo { get; } = [];

    /// <summary>The folders whose access was given up.</summary>
    public List<string> Released { get; } = [];

    /// <summary>The names of the automatic backups in the folder, oldest first.</summary>
    public List<string> Backups => Files.Keys.Where(Core.Backup.Export.AutoExportService.IsOwnFile).Order(StringComparer.Ordinal).ToList();

    public Task<BackupFolder?> ChooseAsync() => Task.FromResult(Next);

    public Task<IReadOnlyList<string>> ListAsync(string folder, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        return Task.FromResult<IReadOnlyList<string>>(Files.Keys.ToList());
    }

    public async Task WriteAsync(string folder, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        if (WriteFails is not null)
        {
            throw WriteFails;
        }

        var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Files[fileName] = copy.ToArray();
        WrittenTo.Add(folder);
    }

    public Task DeleteAsync(string folder, string fileName, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        if (Undeletable.Contains(fileName))
        {
            throw new IOException("The file could not be deleted.");
        }

        Files.Remove(fileName);
        return Task.CompletedTask;
    }

    public void Release(string folder) => Released.Add(folder);

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
        {
            throw new BackupFolderUnavailableException();
        }
    }
}
