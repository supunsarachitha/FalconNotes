using FalconNotes.Core.Crypto;

namespace FalconNotes.Core.Tests.Fakes;

public sealed class FakeSecretStore : ISecretStore
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<string?> GetAsync(string name) => Task.FromResult(Values.GetValueOrDefault(name));

    public Task SetAsync(string name, string value)
    {
        Values[name] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string name)
    {
        Values.Remove(name);
        return Task.CompletedTask;
    }
}
