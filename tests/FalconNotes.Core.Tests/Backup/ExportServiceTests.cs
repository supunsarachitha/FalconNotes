using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Platform;

namespace FalconNotes.Core.Tests.Backup;

public class ExportServiceTests
{
    [Fact]
    public async Task An_export_is_saved_under_its_name_remembered_and_its_temporary_copy_deleted()
    {
        using var app = await TestApp.StartAsync();
        await ExportVectors.SeedAsync(app);
        var saver = new FakeSaver(save: true);
        var service = New(app, saver);

        var result = await service.ExportAsync(ExportVectors.Options("format=md&layout=month&includeArchived=true"));

        Assert.Equal((13, 5), (result!.Notes, result.Files));
        Assert.Equal("falcon-notes-2026-09-28.zip", saver.Name);
        Assert.True(saver.Bytes > 0);
        Assert.Equal(app.Clock.Now.UtcDateTime, await service.LastExportAsync());
        Assert.Empty(Directory.GetFiles(Path.Combine(app.Directories.CacheDirectory, "export")));
        Assert.Equal("Exported 13 notes and 5 files.", ExportService.Summary(result));
    }

    [Fact]
    public async Task A_cancelled_save_dialog_saves_and_remembers_nothing()
    {
        using var app = await TestApp.StartAsync();
        var service = New(app, new FakeSaver(save: false));

        Assert.Null(await service.ExportAsync(ExportVectors.Options("format=md")));
        Assert.Null(await service.LastExportAsync());
        Assert.Empty(Directory.GetFiles(Path.Combine(app.Directories.CacheDirectory, "export")));
    }

    private static ExportService New(TestApp app, IFileSaver saver) =>
        new(ExportConformanceTests.NewExporter(app), saver, app.Directories, app.Storage, app.Clock);

    private sealed class FakeSaver(bool save) : IFileSaver
    {
        public string? Name { get; private set; }

        public long Bytes { get; private set; }

        public async Task<bool> SaveAsync(string suggestedName, Stream content, CancellationToken cancellationToken = default)
        {
            Name = suggestedName;
            var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Bytes = copy.Length;
            return save;
        }
    }
}
