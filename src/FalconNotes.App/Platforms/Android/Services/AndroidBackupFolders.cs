using Android.App;
using Android.Content;
using Android.Provider;
using FalconNotes.Core.Platform;
using AndroidUri = Android.Net.Uri;

namespace FalconNotes.App.Services;

/// <summary>
/// <see cref="IBackupFolders"/> on Android, for automatic backups (docs/12, Automatic backups): the user chooses a
/// folder in the system's picker (<c>ACTION_OPEN_DOCUMENT_TREE</c>), the app keeps that one grant across restarts,
/// and files are listed, created and deleted through <see cref="DocumentsContract"/>. No storage permission is
/// involved, and the app can reach nothing outside the chosen folder.
/// </summary>
/// <remarks>
/// A folder's reference is its tree URI. Every failure of the documents provider (the folder deleted, its card taken
/// out, the grant gone after the app's data was cleared) arrives as a Java exception and leaves as
/// <see cref="BackupFolderUnavailableException"/> or <see cref="IOException"/>, which Core understands.
/// </remarks>
public sealed class AndroidBackupFolders : IBackupFolders
{
    private const int RequestCode = 0x46_4F; // "FO"; AndroidFileSaver has "FN"
    private const ActivityFlags ReadWrite = ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission;
    private static TaskCompletionSource<AndroidUri?>? _pending;

    private static ContentResolver Resolver => Platform.AppContext.ContentResolver!;

    /// <inheritdoc />
    public async Task<BackupFolder?> ChooseAsync()
    {
        _pending?.TrySetResult(null);
        _pending = new TaskCompletionSource<AndroidUri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var intent = new Intent(Intent.ActionOpenDocumentTree)
            .AddFlags(ReadWrite | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
        Platform.CurrentActivity!.StartActivityForResult(intent, RequestCode);
        if (await _pending.Task is not { } tree)
        {
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                Resolver.TakePersistableUriPermission(tree, ReadWrite);
            }
            catch (Java.Lang.Exception e)
            {
                // A provider that lends its folder for this session only is no use for backups made later.
                throw new BackupFolderUnavailableException(e);
            }

            // The app only ever uses one folder. Giving up every other grant also clears one whose record was lost
            // with the database (Erase all data, Key lost).
            foreach (var held in Resolver.PersistedUriPermissions)
            {
                if (held.Uri is { } other && !other.Equals(tree))
                {
                    Release(other.ToString()!);
                }
            }

            return new BackupFolder(tree.ToString()!, Name(tree) ?? "Folder");
        });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListAsync(string folder, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<string>>(() => Children(Parse(folder)).Select(child => child.Name).ToList(), cancellationToken);

    /// <inheritdoc />
    public Task WriteAsync(string folder, string fileName, Stream content, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        var tree = Parse(folder);
        var existing = Children(tree).FirstOrDefault(child => child.Name == fileName);
        var document = existing.Id is null ? Create(tree, fileName) : DocumentsContract.BuildDocumentUriUsingTree(tree, existing.Id)!;
        try
        {
            await AndroidFileSaver.WriteAsync(document, content, cancellationToken);
        }
        catch (Exception e)
        {
            // Half a ZIP must not sit in the folder looking like a backup.
            TryDelete(document);
            if (e is Java.Lang.Exception)
            {
                throw new IOException("The file could not be written.", e);
            }

            throw;
        }
    }, cancellationToken);

    /// <inheritdoc />
    public Task DeleteAsync(string folder, string fileName, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var tree = Parse(folder);
        foreach (var child in Children(tree).Where(child => child.Name == fileName))
        {
            try
            {
                DocumentsContract.DeleteDocument(Resolver, DocumentsContract.BuildDocumentUriUsingTree(tree, child.Id)!);
            }
            catch (Java.Lang.Exception e)
            {
                throw new IOException("The file could not be deleted.", e);
            }
        }
    }, cancellationToken);

    /// <inheritdoc />
    public void Release(string folder)
    {
        try
        {
            Resolver.ReleasePersistableUriPermission(Parse(folder), ReadWrite);
        }
        catch (Exception e) when (e is Java.Lang.Exception or IOException)
        {
            // The grant was already gone.
        }
    }

    /// <summary>Called by <c>MainActivity</c> with the picker's result.</summary>
    /// <param name="requestCode">The request's code; others are ignored.</param>
    /// <param name="resultCode">OK when the user chose a folder.</param>
    /// <param name="data">Holds the chosen folder's tree URI.</param>
    public static void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == RequestCode)
        {
            _pending?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
        }
    }

    private static AndroidUri Parse(string folder) => AndroidUri.Parse(folder) ?? throw new BackupFolderUnavailableException();

    private static AndroidUri Root(AndroidUri tree) =>
        DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))!;

    private static string? Name(AndroidUri tree)
    {
        try
        {
            using var cursor = Resolver.Query(Root(tree), [DocumentsContract.Document.ColumnDisplayName], null, null, null);
            return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
        }
        catch (Java.Lang.Exception)
        {
            return null;
        }
    }

    private static List<(string Id, string Name)> Children(AndroidUri tree)
    {
        try
        {
            var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))!;
            using var cursor = Resolver.Query(
                    children, [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName], null, null, null)
                ?? throw new BackupFolderUnavailableException();
            var found = new List<(string, string)>();
            while (cursor.MoveToNext())
            {
                if (cursor.GetString(0) is { } id && cursor.GetString(1) is { } name)
                {
                    found.Add((id, name));
                }
            }

            return found;
        }
        catch (Java.Lang.Exception e)
        {
            throw new BackupFolderUnavailableException(e);
        }
    }

    private static AndroidUri Create(AndroidUri tree, string fileName)
    {
        try
        {
            return DocumentsContract.CreateDocument(Resolver, Root(tree), "application/zip", fileName)
                ?? throw new BackupFolderUnavailableException();
        }
        catch (Java.Lang.Exception e)
        {
            throw new BackupFolderUnavailableException(e);
        }
    }

    private static void TryDelete(AndroidUri document)
    {
        try
        {
            DocumentsContract.DeleteDocument(Resolver, document);
        }
        catch (Java.Lang.Exception)
        {
            // Nothing more can be done from here; the next backup replaces or outlives it.
        }
    }
}
