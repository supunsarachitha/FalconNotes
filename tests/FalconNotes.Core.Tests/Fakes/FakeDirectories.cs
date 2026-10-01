using FalconNotes.Core.Platform;

namespace FalconNotes.Core.Tests.Fakes;

public sealed class FakeDirectories(string root) : IAppDirectories
{
    public string DataDirectory { get; } = Path.Combine(root, "data");

    public string CacheDirectory { get; } = Path.Combine(root, "cache");
}
