using FalconNotes.Core.Platform;

namespace FalconNotes.UI.Tests.Fakes;

/// <summary>A fixed version and platform name, standing in for the real device.</summary>
public sealed class FakeAppInfo : IAppInfo
{
    /// <inheritdoc />
    public string Version { get; init; } = "1.0.0";

    /// <inheritdoc />
    public string PlatformName { get; init; } = "Android";
}
