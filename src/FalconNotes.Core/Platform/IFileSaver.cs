namespace FalconNotes.Core.Platform;

/// <summary>The system "save to" dialog, for exports and Save a copy (docs/02, Platform services).</summary>
public interface IFileSaver
{
    /// <summary>Asks the user where to save a file, then writes <paramref name="content"/> there.</summary>
    /// <param name="suggestedName">The file name the dialog suggests.</param>
    /// <param name="content">The file's content, read from its current position to the end.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>True when the file was saved; false when the user cancelled.</returns>
    Task<bool> SaveAsync(string suggestedName, Stream content, CancellationToken cancellationToken = default);
}
