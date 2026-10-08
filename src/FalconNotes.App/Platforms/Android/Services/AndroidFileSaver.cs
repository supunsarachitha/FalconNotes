using Android.App;
using Android.Content;
using Android.OS;
using Microsoft.Win32.SafeHandles;
using AndroidUri = Android.Net.Uri;

namespace FalconNotes.App.Services;

/// <summary>
/// <see cref="Core.Platform.IFileSaver"/> on Android: the system "save as" dialog (<c>ACTION_CREATE_DOCUMENT</c>), then
/// a plain .NET <see cref="FileStream"/> on the chosen document's file descriptor.
/// </summary>
/// <remarks>
/// The Community Toolkit's saver shows the same dialog but copies 4 KB at a time through Java's
/// <c>OutputStream.WriteAsync</c>: 1 GB took 13 minutes on the emulator (Phase 0 spike S4). Writing to the descriptor
/// from .NET with a 1 MiB buffer never crosses the Java bridge per write.
/// </remarks>
public sealed class AndroidFileSaver : Core.Platform.IFileSaver
{
    private const int RequestCode = 0x46_4E; // "FN"
    private const int BufferSize = 1024 * 1024;
    private static TaskCompletionSource<AndroidUri?>? _pending;

    /// <inheritdoc />
    public async Task<bool> SaveAsync(string suggestedName, Stream content, CancellationToken cancellationToken = default)
    {
        var uri = await ChooseAsync(suggestedName);
        if (uri is null)
        {
            return false;
        }

        await Task.Run(() => WriteAsync(uri, content, cancellationToken), cancellationToken);
        return true;
    }

    /// <summary>Writes a document the user gave access to, replacing what it held. Call it off the UI thread.</summary>
    /// <param name="document">The document's URI.</param>
    /// <param name="content">The content, read from its current position to the end.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when everything is written.</returns>
    internal static async Task WriteAsync(AndroidUri document, Stream content, CancellationToken cancellationToken)
    {
        using var descriptor = Platform.AppContext.ContentResolver!.OpenFileDescriptor(document, "wt")
            ?? throw new IOException("The chosen file could not be opened.");
        await using var output = new FileStream(new SafeFileHandle(descriptor.DetachFd(), ownsHandle: true), FileAccess.Write, 1);
        await content.CopyToAsync(output, BufferSize, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    /// <summary>Called by <c>MainActivity</c> with the dialog's result.</summary>
    /// <param name="requestCode">The request's code; others are ignored.</param>
    /// <param name="resultCode">OK when the user chose a place.</param>
    /// <param name="data">Holds the chosen document's URI.</param>
    public static void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == RequestCode)
        {
            _pending?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
        }
    }

    private static Task<AndroidUri?> ChooseAsync(string suggestedName)
    {
        _pending?.TrySetResult(null);
        _pending = new TaskCompletionSource<AndroidUri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var extension = Path.GetExtension(suggestedName).TrimStart('.');
        var intent = new Intent(Intent.ActionCreateDocument)
            .AddCategory(Intent.CategoryOpenable)
            .SetType(Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? "application/octet-stream")
            .PutExtra(Intent.ExtraTitle, suggestedName);
        Platform.CurrentActivity!.StartActivityForResult(intent, RequestCode);
        return _pending.Task;
    }
}
