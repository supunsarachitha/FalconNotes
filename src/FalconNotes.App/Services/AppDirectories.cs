using FalconNotes.Core.Platform;

namespace FalconNotes.App.Services;

/// <summary><see cref="IAppDirectories"/> on MAUI's <c>FileSystem</c>.</summary>
public sealed class AppDirectories : IAppDirectories
{
    /// <inheritdoc />
    public string DataDirectory => FileSystem.Current.AppDataDirectory;

    /// <inheritdoc />
    public string CacheDirectory => FileSystem.Current.CacheDirectory;
}
