using FalconNotes.Core.Platform;

namespace FalconNotes.App.Services;

/// <summary><see cref="Core.Platform.IFilePicker"/> on MAUI's picker (Android: the Storage Access Framework, no permission).</summary>
public sealed class FilePicker : Core.Platform.IFilePicker
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PickedFile>> PickFilesAsync(IReadOnlyList<string>? extensions = null)
    {
        var options = new PickOptions { FileTypes = ToFileTypes(extensions) };
        var files = await Microsoft.Maui.Storage.FilePicker.Default.PickMultipleAsync(options);
        return files?.Where(f => f is not null)
                   .Select(f => new PickedFile(f!.FileName, f.ContentType, f.OpenReadAsync))
                   .ToList() ?? [];
    }

    /// <summary>
    /// The Storage Access Framework filters by MIME type, not extension, and has no type for Markdown: restoring
    /// takes ".zip", ".md", ".txt" and ".json" (<see cref="Core.Backup.Restore.RestoreReader.Accept"/>), so the
    /// system picker is left open to every file and the reader itself rejects what it cannot read.
    /// </summary>
    private static FilePickerFileType? ToFileTypes(IReadOnlyList<string>? extensions) => null;
}
