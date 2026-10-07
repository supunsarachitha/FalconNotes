using FalconNotes.Core.Domain;

namespace FalconNotes.Core.Tests.Notes;

// The rules of the server's Notes/CalendarTests.cs.
public class CalendarTests
{
    [Fact]
    public async Task Days_follow_the_device_s_time_zone_and_count_active_notes()
    {
        using var app = await TestApp.StartAsync();
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        app.Clock.Now = new DateTimeOffset(2026, 9, 27, 14, 59, 0, TimeSpan.Zero); // 23:59 on the 27th in Tokyo
        await app.Notes.CreateAsync("late");
        app.Clock.Now = new DateTimeOffset(2026, 9, 27, 15, 0, 0, TimeSpan.Zero); // 00:00 on the 28th
        await app.Notes.CreateAsync("early");
        await app.Notes.CreateAsync("second");
        var archived = await app.Notes.CreateAsync("archived");
        await app.Notes.PatchAsync(archived.Id, new Core.Notes.NotePatch(IsArchived: true));

        var days = await app.Notes.CalendarAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), [NoteKind.Note], tokyo);

        Assert.Equal([(new DateOnly(2026, 9, 27), 1), (new DateOnly(2026, 9, 28), 2)], days);
    }

    [Fact]
    public async Task Only_the_requested_kinds_are_counted()
    {
        using var app = await TestApp.StartAsync();
        await app.Notes.CreateAsync("note");
        await app.Notes.CreateAsync("quick", NoteKind.Quick);

        Assert.Equal(1, (await app.Notes.CalendarAsync(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 28), [NoteKind.Note], TimeZoneInfo.Utc)).Single().Count);
        Assert.Equal(2, (await app.Notes.CalendarAsync(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 28), [NoteKind.Note, NoteKind.Quick], TimeZoneInfo.Utc)).Single().Count);
    }

    [Fact]
    public async Task Daylight_saving_changes_do_not_shift_days()
    {
        using var app = await TestApp.StartAsync();
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        app.Clock.Now = new DateTimeOffset(2026, 10, 24, 22, 30, 0, TimeSpan.Zero); // 00:30 on the 25th (CEST), the night clocks go back
        await app.Notes.CreateAsync("after midnight");
        app.Clock.Now = new DateTimeOffset(2026, 10, 25, 22, 59, 0, TimeSpan.Zero); // 23:59 on the 25th (CET)
        await app.Notes.CreateAsync("before midnight");

        var days = await app.Notes.CalendarAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), [NoteKind.Note], paris);

        Assert.Equal([(new DateOnly(2026, 10, 25), 2)], days);
    }

    [Fact]
    public async Task Ranges_longer_than_62_days_or_backwards_are_refused()
    {
        using var app = await TestApp.StartAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => app.Notes.CalendarAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 5), [NoteKind.Note], TimeZoneInfo.Utc));
        await Assert.ThrowsAsync<ArgumentException>(() => app.Notes.CalendarAsync(new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 1), [NoteKind.Note], TimeZoneInfo.Utc));
    }
}
