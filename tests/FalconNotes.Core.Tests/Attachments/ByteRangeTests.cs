using FalconNotes.Core.Attachments;

namespace FalconNotes.Core.Tests.Attachments;

public class ByteRangeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("items=0-1")]
    [InlineData("bytes=0-1,5-6")]
    [InlineData("bytes=abc-")]
    [InlineData("bytes=5-2")]
    public void Unusable_headers_get_the_whole_file(string? header) =>
        Assert.Equal(RangeResult.Whole, ByteRange.Parse(header, 1000, out _));

    [Theory]
    [InlineData("bytes=0-", 0, 999)]
    [InlineData("bytes=0-99", 0, 99)]
    [InlineData("bytes=500-599", 500, 599)]
    [InlineData("bytes=900-5000", 900, 999)]
    [InlineData("bytes=999-", 999, 999)]
    [InlineData("bytes=-100", 900, 999)]
    [InlineData("bytes=-5000", 0, 999)]
    public void Ranges_are_clamped_to_the_file(string header, long start, long end)
    {
        Assert.Equal(RangeResult.Partial, ByteRange.Parse(header, 1000, out var range));
        Assert.Equal(new ByteRange(start, end), range);
    }

    [Theory]
    [InlineData("bytes=1000-")]
    [InlineData("bytes=5000-6000")]
    [InlineData("bytes=-0")]
    public void Ranges_past_the_end_are_not_satisfiable(string header) =>
        Assert.Equal(RangeResult.NotSatisfiable, ByteRange.Parse(header, 1000, out _));

    [Fact]
    public void Content_range_names_the_range_and_the_total() =>
        Assert.Equal("bytes 0-99/1000", new ByteRange(0, 99).ContentRange(1000));

    [Fact]
    public void A_range_stream_reads_only_its_window()
    {
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        using var window = new RangeStream(new MemoryStream(bytes), 10, 5);

        var copy = new MemoryStream();
        window.CopyTo(copy);

        Assert.Equal(bytes[10..15], copy.ToArray());
    }
}
