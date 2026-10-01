namespace FalconNotes.Core.Platform;

/// <summary>The system file picker (docs/02, Platform services). No storage permission is needed.</summary>
public interface IFilePicker
{
    /// <summary>Lets the user choose one or more files.</summary>
    /// <returns>The chosen files; empty when the user cancelled.</returns>
    Task<IReadOnlyList<PickedFile>> PickFilesAsync();
}
