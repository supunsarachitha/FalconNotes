namespace FalconNotes.Core.Platform;

/// <summary>About the app itself (docs/02, Platform services).</summary>
public interface IAppInfo
{
    /// <summary>The version, e.g. 1.0.0 (<c>AppInfo.VersionString</c>).</summary>
    string Version { get; }

    /// <summary>The platform's name in text about the device's key store: "Android", "Windows" or "macOS".</summary>
    string PlatformName { get; }
}
