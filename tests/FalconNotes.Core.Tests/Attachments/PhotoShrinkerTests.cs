using FalconNotes.Core.Attachments;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Platform;

namespace FalconNotes.Core.Tests.Attachments;

// Ported from web/lib/shrinkPhoto.test.ts, plus the keep-or-replace rules with a fake codec.
public class PhotoShrinkerTests
{
    [Fact]
    public void Fits_the_longest_side_within_2560_pixels_and_never_enlarges()
    {
        Assert.Equal((2560, 1920), PhotoShrinker.FitWithin(4000, 3000));
        Assert.Equal((1920, 2560), PhotoShrinker.FitWithin(3000, 4000));
        Assert.Equal((1200, 800), PhotoShrinker.FitWithin(1200, 800));
        Assert.Equal((2560, 1), PhotoShrinker.FitWithin(10_000, 3));
    }

    [Fact]
    public void Shrinks_smaller_photo_sizes_further_at_a_lower_quality()
    {
        Assert.Equal((2560, 85), PhotoShrinker.Presets[PhotoSize.Large]);
        Assert.Equal((1920, 80), PhotoShrinker.Presets[PhotoSize.Medium]);
        Assert.Equal((1280, 75), PhotoShrinker.Presets[PhotoSize.Small]);
        Assert.Equal((1920, 1440), PhotoShrinker.FitWithin(4000, 3000, PhotoShrinker.Presets[PhotoSize.Medium].MaxSide));
        Assert.Equal((960, 1280), PhotoShrinker.FitWithin(3000, 4000, PhotoShrinker.Presets[PhotoSize.Small].MaxSide));
    }

    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", true)]
    [InlineData("image/webp", true)]
    [InlineData("image/heic", true)]
    [InlineData("IMAGE/JPEG", true)]
    [InlineData("image/gif", false)]
    [InlineData("image/svg+xml", false)]
    [InlineData("video/mp4", false)]
    [InlineData("application/pdf", false)]
    [InlineData("", false)]
    public void Tries_still_images_only(string type, bool shrinkable) => Assert.Equal(shrinkable, PhotoShrinker.CanShrink(type));

    [Theory]
    [InlineData("IMG_1234.HEIC", "IMG_1234.jpg")]
    [InlineData("screen shot.png", "screen shot.jpg")]
    [InlineData("archive.tar.png", "archive.tar.jpg")]
    [InlineData("photo", "photo.jpg")]
    [InlineData(".png", "photo.jpg")]
    public void Names_the_result_as_a_JPEG(string name, string expected) => Assert.Equal(expected, PhotoShrinker.JpegName(name));

    [Fact]
    public async Task Replaces_a_photo_with_its_smaller_JPEG()
    {
        var codec = new FakeCodec(new EncodedImage(new byte[50], HasTransparency: false), upright: (4000, 3000));
        var original = new FileToAdd(new MemoryStream(new byte[100]), "IMG_1.HEIC", "image/heic");

        var result = await new PhotoShrinker(codec).ShrinkAsync(original);

        Assert.Equal("IMG_1.jpg", result.FileName);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(50, result.Content.Length);
        Assert.Equal((2560, 1920), codec.Drawn);
        Assert.Equal(85, codec.Quality);
    }

    [Fact]
    public async Task Asks_the_codec_for_the_chosen_size_and_quality()
    {
        var codec = new FakeCodec(new EncodedImage(new byte[20], HasTransparency: false), upright: (4000, 3000));

        await new PhotoShrinker(codec).ShrinkAsync(new FileToAdd(new MemoryStream(new byte[100]), "a.jpg", "image/jpeg"), PhotoSize.Small);

        Assert.Equal((1280, 960), codec.Drawn);
        Assert.Equal(75, codec.Quality);
    }

    [Theory]
    [InlineData(91, false, "image/png")]  // not a tenth smaller
    [InlineData(10, true, "image/png")]   // transparent pixels
    public async Task Keeps_the_original_when_shrinking_would_lose_or_hardly_save(int jpegSize, bool transparent, string type)
    {
        var codec = new FakeCodec(new EncodedImage(new byte[jpegSize], transparent));
        var content = new MemoryStream(new byte[100]);
        var original = new FileToAdd(content, "x.png", type);

        var result = await new PhotoShrinker(codec).ShrinkAsync(original);

        Assert.Same(original, result);
        Assert.Equal(0, content.Position); // rewound for adding as it is
    }

    [Fact]
    public async Task A_JPEG_is_shrunk_even_when_the_decoder_reports_alpha()
    {
        var codec = new FakeCodec(new EncodedImage(new byte[10], HasTransparency: true));

        var result = await new PhotoShrinker(codec).ShrinkAsync(new FileToAdd(new MemoryStream(new byte[100]), "a.jpg", "image/jpeg"));

        Assert.Equal(10, result.Content.Length);
    }

    [Fact]
    public async Task Leaves_alone_what_it_should_not_or_cannot_shrink()
    {
        var gif = new FileToAdd(new MemoryStream(new byte[100]), "cat.gif", "image/gif");
        var undecodable = new FileToAdd(new MemoryStream(new byte[100]), "photo.jpg", "image/jpeg");
        var broken = new FileToAdd(new MemoryStream(new byte[100]), "photo.jpg", "image/jpeg");

        Assert.Same(gif, await new PhotoShrinker(new FakeCodec(new EncodedImage(new byte[1], false))).ShrinkAsync(gif));
        Assert.Same(undecodable, await new PhotoShrinker(new FakeCodec(null)).ShrinkAsync(undecodable));
        Assert.Same(broken, await new PhotoShrinker(new FakeCodec(null, fail: true)).ShrinkAsync(broken));
    }

    private sealed class FakeCodec(EncodedImage? result, (int, int)? upright = null, bool fail = false) : IImageCodec
    {
        public (int, int)? Drawn { get; private set; }

        public int Quality { get; private set; }

        public Task<EncodedImage?> EncodeJpegAsync(Stream source, Func<int, int, (int Width, int Height)> fit, int quality, CancellationToken cancellationToken)
        {
            if (fail)
            {
                throw new InvalidDataException("bad image");
            }

            source.ReadByte(); // moves the stream, as a real decoder would
            Drawn = upright is { } size ? fit(size.Item1, size.Item2) : null;
            Quality = quality;
            return Task.FromResult(result);
        }
    }
}
