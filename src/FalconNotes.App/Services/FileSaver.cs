using FalconNotes.Core.Platform;

namespace FalconNotes.App.Services;

/// <summary><see cref="Core.Platform.IFileSaver"/> on the Community Toolkit's <c>FileSaver</c>.</summary>
public sealed class FileSaver : Core.Platform.IFileSaver
{
    /// <inheritdoc />
    public async Task<bool> SaveAsync(string suggestedName, Stream content, CancellationToken cancellationToken = default)
    {
        var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(suggestedName, content, cancellationToken);
        if (result.IsSuccessful)
        {
            return true;
        }

        // The toolkit reports a cancelled dialog as an exception; anything else is a real failure.
        if (result.Exception is OperationCanceledException or CommunityToolkit.Maui.Storage.FileSaveException)
        {
            return false;
        }

        throw result.Exception ?? new IOException("The file could not be saved.");
    }
}
