namespace FalconNotes.App.Services;

/// <summary><see cref="Core.Platform.IAppInfo"/> on MAUI's <c>AppInfo</c> and <c>DeviceInfo</c> (docs/12, Shared).</summary>
public sealed class AppInfo : Core.Platform.IAppInfo
{
    /// <inheritdoc />
    public string Version => Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;

    /// <inheritdoc />
    public string PlatformName
    {
        get
        {
            var platform = DeviceInfo.Current.Platform;
            if (platform == DevicePlatform.Android)
            {
                return "Android";
            }

            if (platform == DevicePlatform.WinUI)
            {
                return "Windows";
            }

            if (platform == DevicePlatform.MacCatalyst)
            {
                return "macOS";
            }

            return platform.ToString();
        }
    }
}
