using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace MapleBench;

// The cost of EF Core on top of SQLite: building the model and compiling the first query happen once per app start.
public sealed class Note
{
    public Guid Id { get; set; }
    public NoteBody? Body { get; set; }
    public int Kind { get; set; }
    public string? DailyDate { get; set; }
    public bool IsPinned { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public DateTime? TrashedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<Tag> Tags { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
}

public sealed class NoteBody
{
    public Guid NoteId { get; set; }
    public string Content { get; set; } = "";
}

public sealed class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Attachment
{
    public Guid Id { get; set; }
    public Guid? NoteId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class BenchContext(string connectionString) : DbContext
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connectionString);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Note>(note =>
        {
            note.ToTable("Notes");
            note.HasMany(n => n.Tags).WithMany().UsingEntity("NoteTags",
                l => l.HasOne(typeof(Tag)).WithMany().HasForeignKey("TagId"),
                r => r.HasOne(typeof(Note)).WithMany().HasForeignKey("NoteId"));
            note.HasMany(n => n.Attachments).WithOne().HasForeignKey(a => a.NoteId);
            note.HasOne(n => n.Body).WithOne().HasForeignKey<NoteBody>(b => b.NoteId);
        });
        model.Entity<NoteBody>().ToTable("NoteBodies").HasKey(b => b.NoteId);
        model.Entity<Tag>().ToTable("Tags");
        model.Entity<Attachment>().ToTable("Attachments");
    }
}

public static class EfBench
{
    public static void Run(string dbPath, int notes, Action<string> log)
    {
        var cs = Bench.ConnectionString(dbPath, "sqlcipher");
        List<Note> Feed(BenchContext db) => db.Notes.AsNoTracking()
            .Where(n => n.Kind == 0 && !n.IsPinned && n.ArchivedAtUtc == null && n.TrashedAtUtc == null)
            .OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Include(n => n.Body).Include(n => n.Tags).Include(n => n.Attachments).AsSplitQuery()
            .Take(21).ToList();

        var sw = Stopwatch.StartNew();
        using (var db = new BenchContext(cs))
        {
            var page = Feed(db);
            log($"MAPLEBENCH|ef-core|{notes}|first query in the process (model + compile + open)|{sw.Elapsed.TotalMilliseconds:F2}|{sw.Elapsed.TotalMilliseconds:F2}|{page.Count} notes");
        }
        var times = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            sw.Restart();
            using var db = new BenchContext(cs);
            Feed(db);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        log($"MAPLEBENCH|ef-core|{notes}|feed: first page (new context + connection each time)|{times[0]:F2}|{times[5]:F2}|");
    }
}
