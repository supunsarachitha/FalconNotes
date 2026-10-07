using System.Security.Cryptography;
using FalconNotes.Core.Crypto;

namespace FalconNotes.Core.Tests.Crypto;

public class KeyMaterialTests
{
    [Fact]
    public void Derives_distinct_stable_keys_from_the_device_key()
    {
        var deviceKey = RandomNumberGenerator.GetBytes(32);
        using var first = new KeyMaterial(deviceKey);
        using var again = new KeyMaterial(deviceKey);
        using var attachments = first.CreateAttachmentKey();

        Assert.Equal(first.DatabaseKey.ToArray(), again.DatabaseKey.ToArray());
        Assert.NotEqual(deviceKey, first.DatabaseKey.ToArray());
        Assert.NotEqual(first.DatabaseKey.ToArray(), attachments.Span.ToArray());
        Assert.Matches("^[0-9a-f]{8}$", first.Fingerprint);
        Assert.Equal(first.Fingerprint, again.Fingerprint);
    }

    [Fact]
    public void Uses_this_app_s_own_salt_and_labels()
    {
        // Computed independently (Python's hmac, RFC 5869). Changing the salt or a label would make every existing
        // database unreadable, so this must never change.
        using var keys = new KeyMaterial(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

        Assert.Equal("b1eaf8c3", keys.Fingerprint);
    }

    [Fact]
    public void Rejects_keys_of_the_wrong_size() => Assert.Throws<ArgumentException>(() => new KeyMaterial(new byte[16]));
}
