namespace FalconNotes.Core.Platform;

/// <summary>A file the user chose in the system file picker.</summary>
/// <param name="FileName">The file's name, as the picker reports it.</param>
/// <param name="ContentType">The type the picker reports, or null.</param>
/// <param name="OpenReadAsync">Opens the file for reading. On Android it may not be seekable.</param>
public sealed record PickedFile(string FileName, string? ContentType, Func<Task<Stream>> OpenReadAsync);
