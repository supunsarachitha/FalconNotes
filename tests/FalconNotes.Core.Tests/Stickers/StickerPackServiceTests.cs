using System.IO.Compression;
using System.Text;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Stickers;
using FalconNotes.Core.Tests.Fakes;

namespace FalconNotes.Core.Tests.Stickers;

/// <summary>New tests (docs/11): sticker packs added from a ZIP file, listed and removed (docs/04, Stickers).</summary>
public class StickerPackServiceTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FakeDirectories _directories;
    private readonly StickerPackService _packs;

    public StickerPackServiceTests()
    {
        _directories = new FakeDirectories(_directory.Path);
        _packs = new StickerPackService(_directories, new FakeClock(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose() => _directory.Dispose();

    public static byte[] Png(byte tail = 0) => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, tail];

    public static byte[] Gif() => [.. "GIF89a"u8, 1, 0, 1, 0];

    public static byte[] Webp() => [.. "RIFF"u8, 20, 0, 0, 0, .. "WEBPVP8 "u8, 0, 0];

    public static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    private static (string, byte[]) Text(string name, string text) => (name, Encoding.UTF8.GetBytes(text));

    private Task<StickerPack> AddAsync(string fileName, byte[] zip) => _packs.AddAsync(new MemoryStream(zip), fileName);

    private static byte[] Read((Stream Content, string ContentType)? opened)
    {
        Assert.NotNull(opened);
        using var content = opened.Value.Content;
        using var copy = new MemoryStream();
        content.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public async Task A_zip_of_pictures_is_a_pack_named_after_its_file_with_every_picture_in_path_order()
    {
        var pack = await AddAsync("Happy Cats.zip", Zip(
            ("waving.png", Png(1)),
            ("README.txt", "made by me"u8.ToArray()),
            ("more/sleepy.gif", Gif()),
            ("angry.webp", Webp()),
            ("__MACOSX/._waving.png", Png(9)),
            (".hidden.png", Png(9))));

        Assert.Equal(("happy-cats", "Happy Cats"), (pack.Id, pack.Name));
        Assert.Equal(
            [("001.webp", "angry", "image/webp"), ("002.gif", "sleepy", "image/gif"), ("003.png", "waving", "image/png")],
            pack.Stickers.Select(s => (s.File, s.Name, s.ContentType)));
        Assert.Equal(Png(1), Read(_packs.Open("happy-cats", "003.png")));
        Assert.Equal([pack], await _packs.ListAsync(), new PackComparer());
    }

    [Fact]
    public async Task The_packs_own_description_names_it_and_lists_its_stickers_in_its_order()
    {
        var pack = await AddAsync("download (3).zip", Zip(
            Text("Falcon friends/sticker-pack.json", """
                {
                  "id": "falcon-friends",
                  "name": "Falcon friends",
                  "author": "Supun",
                  "licence": "CC0-1.0",
                  "version": "1.2",
                  "stickers": [
                    { "file": "art/wave.png", "name": "Waving" },
                    { "file": "art/sleep.gif" }
                  ]
                }
                """),
            ("Falcon friends/art/wave.png", Png()),
            ("Falcon friends/art/sleep.gif", Gif()),
            ("Falcon friends/art/unused.png", Png())));

        Assert.Equal(("falcon-friends", "Falcon friends", "Supun", "CC0-1.0", "1.2"), (pack.Id, pack.Name, pack.Author, pack.Licence, pack.Version));
        Assert.Equal([("001.png", "Waving"), ("002.gif", "sleep")], pack.Stickers.Select(s => (s.File, s.Name)));
    }

    [Fact]
    public async Task A_pack_added_again_with_the_same_id_replaces_the_old_one()
    {
        await AddAsync("cats.zip", Zip(("a.png", Png(1)), ("b.png", Png(2))));
        var replaced = await AddAsync("cats.zip", Zip(("c.png", Png(3))));

        var packs = await _packs.ListAsync();
        Assert.Equal(replaced, Assert.Single(packs), new PackComparer());
        Assert.Equal(Png(3), Read(_packs.Open("cats", "001.png")));
        Assert.Null(_packs.Open("cats", "002.png"));
        Assert.Equal(["cats"], Directory.EnumerateFileSystemEntries(_packs.Root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Packs_are_listed_by_name()
    {
        await AddAsync("zebra.zip", Zip(("a.png", Png())));
        await AddAsync("Apple.zip", Zip(("a.png", Png())));
        await AddAsync("mango.zip", Zip(("a.png", Png())));

        Assert.Equal(["Apple", "mango", "zebra"], (await _packs.ListAsync()).Select(p => p.Name));
    }

    [Fact]
    public async Task Removing_a_pack_deletes_its_files()
    {
        await AddAsync("cats.zip", Zip(("a.png", Png())));
        await AddAsync("dogs.zip", Zip(("a.png", Png())));

        Assert.True(await _packs.RemoveAsync("cats"));
        Assert.False(await _packs.RemoveAsync("cats"));

        Assert.Equal(["dogs"], (await _packs.ListAsync()).Select(p => p.Id));
        Assert.Null(_packs.Open("cats", "001.png"));
        Assert.Equal(["dogs"], Directory.EnumerateFileSystemEntries(_packs.Root).Select(Path.GetFileName));
    }

    public static TheoryData<string, byte[]> NotPacks => new()
    {
        { "This file is not a sticker pack. A sticker pack is a ZIP file of pictures.", "not a zip"u8.ToArray() },
        { "This sticker pack has no pictures in it.", Zip(Text("notes.txt", "hello")) },
        { "\"cat\" is not a PNG, JPEG, GIF or WebP picture, so it cannot be a sticker.", Zip(("cat.png", "<svg onload=alert(1)>"u8.ToArray())) },
        { "\"big\" is too large for a sticker: a sticker can be at most 2 MB.", Zip(("big.png", [.. Png(), .. new byte[2 * 1024 * 1024]])) },
        { "This sticker pack lists \"gone.png\", which is not in it.", Zip(Text("sticker-pack.json", """{"stickers":[{"file":"gone.png"}]}"""), ("a.png", Png())) },
        { "This sticker pack's sticker-pack.json could not be read.", Zip(Text("sticker-pack.json", "{ name: oops"), ("a.png", Png())) },
        { "A sticker pack can have at most 300 stickers; this one has 301.", Zip([.. Enumerable.Range(0, 301).Select(i => ($"{i}.png", Png()))]) },
    };

    [Theory]
    [MemberData(nameof(NotPacks))]
    public async Task A_file_that_is_not_a_pack_is_refused_with_the_reason_and_leaves_nothing_behind(string message, byte[] zip)
    {
        await AddAsync("good.zip", Zip(("a.png", Png())));

        var refused = await Assert.ThrowsAsync<UserFacingException>(() => AddAsync("pack.zip", zip));

        Assert.Equal(message, refused.Message);
        Assert.Equal(["good"], Directory.EnumerateFileSystemEntries(_packs.Root).Select(Path.GetFileName));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_directories.CacheDirectory, "stickers")));
    }

    [Fact]
    public async Task Nothing_in_a_zip_names_a_path_on_the_device()
    {
        var pack = await AddAsync("evil.zip", Zip(
            Text("sticker-pack.json", """{"id":"../../escape","name":"Evil"}"""),
            ("../../outside.png", Png()),
            ("art/../../../outside.png", Png()),
            ("ok.png", Png())));

        Assert.Equal("escape", pack.Id);
        Assert.Equal(["ok"], pack.Stickers.Select(s => s.Name));
        Assert.Equal(["001.png", "pack.json"], Directory.EnumerateFiles(Path.Combine(_packs.Root, "escape")).Select(Path.GetFileName).Order());
        Assert.Equal(["data/stickers/escape/001.png", "data/stickers/escape/pack.json"],
            Directory.EnumerateFiles(_directory.Path, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_directory.Path, f).Replace('\\', '/')).Order());
    }

    [Theory]
    [InlineData("cats", "../cats/001.png")]
    [InlineData("..", "001.png")]
    [InlineData("cats", "pack.json")]
    [InlineData("cats", "001.svg")]
    [InlineData("Cats", "001.png")]
    public async Task Only_a_stickers_own_file_is_opened(string pack, string file)
    {
        await AddAsync("cats.zip", Zip(("a.png", Png())));

        Assert.Null(_packs.Open(pack, file));
    }

    [Fact]
    public async Task A_new_service_reads_the_packs_already_on_the_device()
    {
        var added = await AddAsync("cats.zip", Zip(("a.png", Png()), ("b.gif", Gif())));

        var later = new StickerPackService(_directories, TimeProvider.System);

        Assert.Equal([added], await later.ListAsync(), new PackComparer());
    }

    private sealed class PackComparer : IEqualityComparer<StickerPack>
    {
        public bool Equals(StickerPack? x, StickerPack? y) =>
            x is not null && y is not null && x with { Stickers = [] } == y with { Stickers = [] } && x.Stickers.SequenceEqual(y.Stickers);

        public int GetHashCode(StickerPack obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }
}
