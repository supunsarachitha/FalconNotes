using FalconNotes.Core.Platform;

namespace FalconNotes.App.Services;

/// <summary><see cref="Core.Platform.IFilePicker"/> on MAUI's picker (Android: the Storage Access Framework, no permission).</summary>
public sealed class FilePicker : Core.Platform.IFilePicker
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PickedFile>> PickFilesAsync()
    {
        var files = await Microsoft.Maui.Storage.FilePicker.Default.PickMultipleAsync(new PickOptions());
        return files?.Where(f => f is not null)
                   .Select(f => new PickedFile(f!.FileName, f.ContentType, f.OpenReadAsync))
                   .ToList() ?? [];
    }
}
