using System.Security.Cryptography;
using FalconNotes.Core.Crypto;

namespace FalconNotes.Core.Tests.Crypto;

/// <summary>
/// A first check of the ported cipher for the Phase 0 media spike. Phase 1 ports the server's full
/// AttachmentCipherTests (tampering, truncation, reordering, every chunk boundary).
/// </summary>
public class AttachmentCipherSmokeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(AttachmentCipher.ChunkSize)]
    [InlineData(3 * AttachmentCipher.ChunkSize + 17)]
    public async Task Round_trips_and_reads_any_range(int size)
    {
        var plain = RandomNumberGenerator.GetBytes(size);
        using var key = new AttachmentKey(RandomNumberGenerator.GetBytes(32));
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var encrypted = new MemoryStream();
        await AttachmentCipher.EncryptAsync(new MemoryStream(plain), encrypted, key, owner, id, TestContext.Current.CancellationToken);

        await using var stream = DecryptingAttachmentStream.Open(new MemoryStream(encrypted.ToArray()), key, owner, id);
        Assert.Equal(size, stream.Length);
        var all = new MemoryStream();
        await stream.CopyToAsync(all, TestContext.Current.CancellationToken);
        Assert.Equal(plain, all.ToArray());

        if (size > 10)
        {
            stream.Position = size - 10;
            var tail = new byte[10];
            stream.ReadExactly(tail);
            Assert.Equal(plain[^10..], tail);
        }
    }

    [Fact]
    public async Task Another_attachment_id_does_not_decrypt()
    {
        using var key = new AttachmentKey(RandomNumberGenerator.GetBytes(32));
        var owner = Guid.NewGuid();
        var encrypted = new MemoryStream();
        await AttachmentCipher.EncryptAsync(new MemoryStream([1, 2, 3]), encrypted, key, owner, Guid.NewGuid(), TestContext.Current.CancellationToken);

        await using var stream = DecryptingAttachmentStream.Open(new MemoryStream(encrypted.ToArray()), key, owner, Guid.NewGuid());
        Assert.ThrowsAny<CryptographicException>(() => stream.ReadByte());
    }
}
