using FalconNotes.Core.Pdf;
using FalconNotes.Core.Platform;

namespace FalconNotes.Core.Tests.Pdf;

// New: the web app has no Export to PDF (docs/04, Export to PDF).
public class PdfExportServiceTests
{
    [Fact]
    public async Task A_note_is_saved_under_its_first_line_and_its_temporary_copy_deleted()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("# Buy **maple** syrup! #shopping\n\n- [ ] two bottles");
        var writer = new FakeWriter();
        var saver = new FakeSaver(save: true);

        var saved = await new PdfExportService(writer, saver, app.Directories, app.Clock).ExportAsync(note, note.Content);

        Assert.True(saved);
        Assert.Equal("Buy maple syrup.pdf", saver.Name);
        Assert.Equal("%PDF-fake"u8.ToArray(), saver.Bytes);
        Assert.Equal(PdfTextStyle.Heading1, Assert.IsType<PdfText>(writer.Written!.Blocks[0]).Style);
        Assert.Empty(Directory.GetFiles(Path.Combine(app.Directories.CacheDirectory, "export")));
    }

    [Fact]
    public async Task A_cancelled_save_dialog_leaves_nothing_behind()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("oats");

        var saved = await new PdfExportService(new FakeWriter(), new FakeSaver(save: false), app.Directories, app.Clock).ExportAsync(note, note.Content);

        Assert.False(saved);
        Assert.Empty(Directory.GetFiles(Path.Combine(app.Directories.CacheDirectory, "export")));
    }

    [Fact]
    public async Task A_pdf_that_cannot_be_written_is_not_offered_for_saving_and_leaves_nothing_behind()
    {
        using var app = await TestApp.StartAsync();
        var note = await app.Notes.CreateAsync("oats");
        var saver = new FakeSaver(save: true);
        var service = new PdfExportService(new FakeWriter { Fails = true }, saver, app.Directories, app.Clock);

        await Assert.ThrowsAsync<IOException>(() => service.ExportAsync(note, note.Content));

        Assert.Null(saver.Name);
        Assert.Empty(Directory.GetFiles(Path.Combine(app.Directories.CacheDirectory, "export")));
    }

    [Theory]
    [InlineData("", "note.pdf")]
    [InlineData("**!!**\n\nwords later", "note.pdf")]
    [InlineData("#shopping", "shopping.pdf")]
    [InlineData("# 2026-09-28\n\nRained all day", "2026-09-28.pdf")]
    [InlineData("- [x] Mum's *birthday*: cake/candles?", "Mums birthday cake candles.pdf")]
    [InlineData("> a<b>:c|d\\e \"f\" .. ", "a b c d e f.pdf")]
    [InlineData("සටහන් පොත", "සටහන් පොත.pdf")] // the vowel signs an export's slug would drop
    [InlineData("தமிழ் குறிப்பு", "தமிழ் குறிப்பு.pdf")]
    public void The_file_is_named_after_the_notes_first_line(string content, string name) =>
        Assert.Equal(name, PdfExportService.FileName(content));

    [Fact]
    public void A_long_first_line_is_cut_between_characters()
    {
        // 𠀀 takes two chars: the cut at 60 would fall inside the thirtieth one.
        var name = PdfExportService.FileName("a" + string.Concat(Enumerable.Repeat("𠀀", 40)));

        Assert.Equal("a" + string.Concat(Enumerable.Repeat("𠀀", 29)) + ".pdf", name);
        Assert.Equal("word word.pdf", PdfExportService.FileName(new string(' ', 3) + "word " + new string('!', 80) + " word " + new string('?', 80)));
    }

    private sealed class FakeWriter : IPdfWriter
    {
        public PdfNote? Written { get; private set; }

        public bool Fails { get; init; }

        public async Task WriteAsync(PdfNote note, Stream output, CancellationToken cancellationToken = default)
        {
            if (Fails)
            {
                throw new IOException("No room.");
            }

            Written = note;
            await output.WriteAsync("%PDF-fake"u8.ToArray(), cancellationToken);
        }
    }

    private sealed class FakeSaver(bool save) : IFileSaver
    {
        public string? Name { get; private set; }

        public byte[] Bytes { get; private set; } = [];

        public async Task<bool> SaveAsync(string suggestedName, Stream content, CancellationToken cancellationToken = default)
        {
            Name = suggestedName;
            var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Bytes = copy.ToArray();
            return save;
        }
    }
}
