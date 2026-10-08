using FalconNotes.Core.Backup.Export;
using FalconNotes.Core.Platform;
using FalconNotes.UI.Components;
using FalconNotes.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FalconNotes.UI.Tests;

/// <summary>
/// New tests (docs/11): Settings → Backup &amp; data → Automatic backups, and Home's notice when one fails, over a
/// fake folder. The real folder picker (<c>AndroidBackupFolders</c>) can only be checked on a device or an emulator.
/// </summary>
public class AutoBackupSectionTests : BunitContext
{
    private const string Switch = "button[role=switch][aria-label='Back up automatically']";

    private static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> cut, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text);

    [Fact]
    public async Task It_is_off_at_first_and_says_the_backups_are_not_encrypted()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");

        var cut = Render<AutoBackupSection>();

        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find(Switch).GetAttribute("aria-checked")));
        Assert.Contains("These backups are not\n            encrypted", cut.Markup.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("it cannot back up while it is closed", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("select"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Back up now");
    }

    [Fact]
    public async Task Turning_it_on_asks_for_the_folder_and_makes_the_first_backup_there()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.PostAsync("Remember me");
        var cut = Render<AutoBackupSection>();
        cut.WaitForAssertion(() => cut.Find(Switch));

        cut.Find(Switch).Click();

        cut.WaitForAssertion(() => Assert.Single(app.BackupFolders.Backups));
        cut.WaitForAssertion(() => Assert.Contains("Last automatic backup: just now", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("true", cut.Find(Switch).GetAttribute("aria-checked"));
        Assert.Contains("Backups", cut.Find("p.truncate").TextContent, StringComparison.Ordinal); // the folder's name
        Assert.Contains(app.Toasts.Current, t => t.Message == "Exported 1 note and 0 files.");
        Assert.Equal(["7", "3"], cut.FindAll("select").Select(s => s.GetAttribute("value")));
        Assert.Contains("Other files in it are never touched.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_folder_picker_leaves_it_off()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        app.BackupFolders.Next = null;
        var cut = Render<AutoBackupSection>();
        cut.WaitForAssertion(() => cut.Find(Switch));

        await cut.Find(Switch).ClickAsync(new());

        Assert.Equal("false", cut.Find(Switch).GetAttribute("aria-checked"));
        Assert.False((await app.AutoExport.GetAsync()).Enabled);
        Assert.Empty(app.BackupFolders.Files);
    }

    [Fact]
    public async Task How_often_and_how_many_to_keep_are_saved()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.AutoExport.ChooseFolderAsync();
        var cut = Render<AutoBackupSection>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("select").Count));
        Assert.Equal(["Every day", "Every week", "Every month"], cut.FindAll("select")[0].Children.Select(o => o.TextContent));
        Assert.Equal(["The newest 3", "The newest 5", "The newest 10"], cut.FindAll("select")[1].Children.Select(o => o.TextContent));
        Assert.Contains("No automatic backup yet.", cut.Markup, StringComparison.Ordinal);

        await cut.FindAll("select")[0].ChangeAsync(new() { Value = "1" });
        await cut.FindAll("select")[1].ChangeAsync(new() { Value = "10" });

        var settings = await app.AutoExport.GetAsync();
        Assert.Equal((1, 10), (settings.EveryDays, settings.Keep));
    }

    [Fact]
    public async Task A_failed_backup_says_why_until_another_folder_works()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.AutoExport.ChooseFolderAsync();
        app.BackupFolders.Unavailable = true;
        var cut = Render<AutoBackupSection>();
        cut.WaitForAssertion(() => cut.Find(Switch));

        Button(cut, "Back up now").Click();

        cut.WaitForAssertion(() => Assert.Equal(
            "The last automatic backup failed. Falcon Notes can no longer reach the folder. Choose it again.",
            cut.Find("p[role=alert]").TextContent.Trim()));
        Assert.Empty(app.Toasts.Current);

        app.BackupFolders.Unavailable = false;
        app.BackupFolders.Next = new BackupFolder("tree:card", "Card");
        Button(cut, "Change…").Click();

        cut.WaitForAssertion(() => Assert.Contains("Last automatic backup: just now", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("p[role=alert]"));
        Assert.Equal("Card", cut.Find("p.truncate").TextContent);
        Assert.Equal(["tree:card"], app.BackupFolders.WrittenTo);
    }

    [Fact]
    public async Task Turning_it_off_hides_its_choices_and_gives_up_the_folder()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.AutoExport.ChooseFolderAsync();
        var cut = Render<AutoBackupSection>();
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find(Switch).GetAttribute("aria-checked")));

        cut.Find(Switch).Click();

        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find(Switch).GetAttribute("aria-checked")));
        Assert.Empty(cut.FindAll("select"));
        Assert.Equal(["tree:backups"], app.BackupFolders.Released);
    }

    [Fact]
    public async Task A_backup_made_in_the_background_shows_when_it_ends()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        await app.AutoExport.ChooseFolderAsync();
        var section = Render<AutoBackupSection>();
        var manual = Render<BackupSection>();
        section.WaitForAssertion(() => Assert.Contains("No automatic backup yet.", section.Markup, StringComparison.Ordinal));
        manual.WaitForAssertion(() => Assert.Contains("You have not exported yet.", manual.Markup, StringComparison.Ordinal));

        await app.AutoExport.RunIfDueAsync(Xunit.TestContext.Current.CancellationToken); // as maintenance does at start and on the hour

        section.WaitForAssertion(() => Assert.Contains("Last automatic backup: just now", section.Markup, StringComparison.Ordinal));
        manual.WaitForAssertion(() => Assert.Contains("Last export: just now", manual.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task It_is_not_offered_where_the_platform_cannot_keep_a_folder()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        Services.AddSingleton(new AutoExportService(
            new NoteExporter(app.Core.Storage, app.Core.Attachments, app.Core.Profile, app.Core.Clock, NullLogger<NoteExporter>.Instance),
            app.Core.Directories, app.Core.Storage, app.Core.Clock, NullLogger<AutoExportService>.Instance));

        Assert.Equal("", Render<AutoBackupSection>().Markup.Trim());
        Assert.Equal("", Render<AutoBackupNotice>().Markup.Trim());
    }

    [Fact]
    public async Task Home_says_when_the_last_backup_failed_and_stops_when_one_works()
    {
        using var app = await UiTestApp.StartAsync(Services, "Alex");
        var cut = Render<AutoBackupNotice>();
        Assert.Equal("", cut.Markup.Trim()); // off: nothing

        await app.AutoExport.ChooseFolderAsync();
        app.BackupFolders.Unavailable = true;
        await app.AutoExport.RunIfDueAsync(Xunit.TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => Assert.Contains(
            "The last automatic backup failed. Falcon Notes can no longer reach the folder. Choose it again.", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("Open Backup & data", cut.Find("a[href='/settings/data']").TextContent);

        app.BackupFolders.Unavailable = false;
        await app.AutoExport.RunIfDueAsync(Xunit.TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => Assert.Equal("", cut.Markup.Trim()));
    }
}
