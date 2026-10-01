using System.Security.Cryptography;
using FalconNotes.Core.Attachments;
using FalconNotes.Core.Crypto;
using FalconNotes.Core.Domain;
using FalconNotes.Core.Notes;

namespace FalconNotes.Core.Tests.Attachments;

// The rules of the server's Notes/AttachmentsApiTests.cs, at the service level.
public class AttachmentServiceTests
{
    [Fact]
    public async Task A_file_is_encrypted_on_disk_and_reads_back_intact()
    {
        using var app = await TestApp.StartAsync();
        var bytes = RandomNumberGenerator.GetBytes(3 * AttachmentCipher.ChunkSize + 123);
        var progress = new List<long>();

        var attachment = await app.Attachments.AddAsync(new MemoryStream(bytes), "../Holiday photo.JPG", null, new SyncProgress(progress.Add));

        Assert.Equal("Holiday photo.JPG", attachment.FileName);
        Assert.Equal("image/jpeg", attachment.ContentType);
        Assert.Equal(bytes.Length, attachment.SizeBytes);
        Assert.Null(attachment.NoteId);
        Assert.Equal(bytes.Length, progress[^1]);
        var stored = await File.ReadAllBytesAsync(Path.Combine(app.Store.Root, app.Store.EnumerateStorageKeys().Single()));
        Assert.True(AttachmentCipher.HasEncryptedHeader(stored));
        Assert.Equal(-1, stored.AsSpan().IndexOf(bytes.AsSpan(0, 64)));

        var media = await app.Attachments.OpenAsync(attachment.Id);
        await using var content = media!.Content;
        var read = new MemoryStream();
        await content.CopyToAsync(read);
        Assert.Equal(bytes, read.ToArray());
        Assert.Equal("image/jpeg", media.ContentType);
    }

    [Fact]
    public async Task Ranges_read_across_chunk_boundaries()
    {
        using var app = await TestApp.StartAsync();
        var bytes = RandomNumberGenerator.GetBytes(2 * AttachmentCipher.ChunkSize + 50);
        var attachment = await app.Attachments.AddAsync(new MemoryStream(bytes), "clip.mp4", "video/mp4");
        var start = AttachmentCipher.ChunkSize - 10;

        var media = await app.Attachments.OpenAsync(attachment.Id);
        await using var range = new RangeStream(media!.Content, start, 100);
        var read = new MemoryStream();
        await range.CopyToAsync(read);

        Assert.Equal(bytes.AsSpan(start, 100).ToArray(), read.ToArray());
    }

    [Fact]
    public async Task A_file_attaches_to_one_note_only()
    {
        using var app = await TestApp.StartAsync();
        var file = await app.AddFileAsync();
        await app.Notes.CreateAsync("first", attachmentIds: [file.Id]);

        await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.CreateAsync("second", attachmentIds: [file.Id]));
        await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.CreateAsync("third", attachmentIds: [Guid.NewGuid()]));
        Assert.False(await app.Attachments.RemoveUnattachedAsync(file.Id)); // attached files are not removed this way
    }

    [Fact]
    public async Task Files_list_oldest_first_and_removing_one_from_a_note_deletes_it()
    {
        using var app = await TestApp.StartAsync();
        var first = await app.AddFileAsync("a.png");
        app.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await app.AddFileAsync("b.pdf");
        var note = await app.Notes.CreateAsync("files", attachmentIds: [second.Id, first.Id]);
        Assert.Equal([first.Id, second.Id], note.Attachments.Select(a => a.Id));
        Assert.True(note.Attachments[0].IsImage);
        Assert.False(note.Attachments[1].IsImage);

        var edited = await app.Notes.UpdateAsync(note.Id, "files", [second.Id]);

        Assert.Equal([second.Id], edited!.Attachments.Select(a => a.Id));
        Assert.Null(await app.Attachments.GetAsync(first.Id));
        Assert.Single(app.Store.EnumerateStorageKeys());
    }

    [Fact]
    public async Task Editing_without_a_file_list_keeps_the_files()
    {
        using var app = await TestApp.StartAsync();
        var file = await app.AddFileAsync();
        var note = await app.Notes.CreateAsync("files", attachmentIds: [file.Id]);

        var edited = await app.Notes.UpdateAsync(note.Id, "new text");

        Assert.Equal([file.Id], edited!.Attachments.Select(a => a.Id));
    }

    [Fact]
    public async Task A_file_removed_before_posting_is_deleted_at_once()
    {
        using var app = await TestApp.StartAsync();
        var file = await app.AddFileAsync();

        Assert.True(await app.Attachments.RemoveUnattachedAsync(file.Id));

        Assert.Null(await app.Attachments.GetAsync(file.Id));
        Assert.Empty(app.Store.EnumerateStorageKeys());
    }

    [Fact]
    public async Task A_decrypted_copy_for_Open_keeps_the_file_name()
    {
        using var app = await TestApp.StartAsync();
        var file = await app.Attachments.AddAsync(new MemoryStream([1, 2, 3]), "report.pdf", "application/pdf");

        var path = await app.Attachments.CopyDecryptedAsync(file.Id, Path.Combine(app.Directories.CacheDirectory, "open"));

        Assert.Equal("report.pdf", Path.GetFileName(path));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path!));
    }

    [Fact]
    public async Task Clean_up_removes_abandoned_files_and_orphans_only()
    {
        using var app = await TestApp.StartAsync();
        app.Clock.Now = DateTimeOffset.UtcNow; // orphan files are judged by their real write times
        var attached = await app.AddFileAsync("kept.png");
        await app.Notes.CreateAsync("note", attachmentIds: [attached.Id]);
        var abandoned = await app.AddFileAsync("abandoned.png");
        app.Clock.Advance(TimeSpan.FromHours(23));
        var recent = await app.AddFileAsync("recent.png");
        var orphan = AttachmentStore.CreateStorageKey(Guid.CreateVersion7());
        await app.Store.WriteAsync(orphan, (s, ct) => s.WriteAsync(new byte[] { 1 }, ct).AsTask());
        File.SetLastWriteTimeUtc(Path.Combine(app.Store.Root, orphan), app.Clock.Now.UtcDateTime.AddHours(-1).AddMinutes(-1));
        app.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1)); // the abandoned file is now over 24 hours old

        var result = await app.Cleanup.RunAsync();

        Assert.Equal(1, result.AbandonedUploads);
        Assert.Equal(1, result.OrphanFiles);
        Assert.NotNull(await app.Attachments.GetAsync(attached.Id));
        Assert.NotNull(await app.Attachments.GetAsync(recent.Id));
        Assert.Null(await app.Attachments.GetAsync(abandoned.Id));
        Assert.Equal(2, app.Store.EnumerateStorageKeys().Count());
    }

    [Theory]
    [InlineData("photo.png", "photo.png")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\Users\me\report.pdf", "report.pdf")]
    [InlineData("  ..hidden..  ", "hidden")]
    [InlineData("bad<>:\"|?*name.txt", "badname.txt")]
    [InlineData("tab\tname\u0000.txt", "tabname.txt")]
    [InlineData("", "file")]
    [InlineData(null, "file")]
    [InlineData("日本語のファイル.txt", "日本語のファイル.txt")]
    public void Sanitizes_file_names(string? input, string expected) => Assert.Equal(expected, UploadPolicy.SanitizeFileName(input));

    [Fact]
    public void Truncates_long_names_keeping_the_extension()
    {
        var name = UploadPolicy.SanitizeFileName(new string('a', 500) + ".jpeg");

        Assert.Equal(200, name.Length);
        Assert.EndsWith(".jpeg", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("image/PNG", "x.bin", "image/png")]
    [InlineData("text/plain; charset=utf-8", "x", "text/plain")]
    [InlineData(null, "photo.jpg", "image/jpeg")]
    [InlineData("application/octet-stream", "clip.mp4", "video/mp4")]
    [InlineData("", "unknown.zzz", "application/octet-stream")]
    [InlineData("not a type", "notes.txt", "text/plain")]
    public void Resolves_content_types(string? declared, string fileName, string expected) =>
        Assert.Equal(expected, UploadPolicy.ResolveContentType(declared, fileName));

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("video/mp4", true)]
    [InlineData("text/plain", true)]
    [InlineData("image/svg+xml", false)]
    [InlineData("text/html", false)]
    [InlineData("application/pdf", false)]
    public void Only_passive_media_is_displayed_inline(string contentType, bool inline) =>
        Assert.Equal(inline, UploadPolicy.CanDisplayInline(contentType));

    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
