using FalconNotes.UI.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FalconNotes.UI.Tests;

/// <summary>New tests (docs/11): Welcome has no reference equivalent (the web app signs in instead), so this covers
/// docs/07's Welcome directly: starting writing, the default name, and the restore flow.</summary>
public class WelcomeTests : BunitContext
{
    private async Task<UiTestApp> StartAsync() => await UiTestApp.StartAsync(Services);

    [Fact]
    public async Task Shows_the_form_with_no_profile_yet()
    {
        using var app = await StartAsync();
        Assert.Null(app.State.Profile);

        var cut = Render<Welcome>();

        Assert.Equal("Welcome to Falcon Notes", cut.Find("h1").TextContent);
        cut.Find("input[autocomplete=nickname]"); // the display name field
        cut.Find("button[type=submit]");
        cut.Find("button:not([type=submit])"); // Restore from a backup…
    }

    [Fact]
    public async Task Start_writing_creates_the_profile_with_the_typed_name_and_opens_home()
    {
        using var app = await StartAsync();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = Render<Welcome>();

        cut.Find("input").Input("Alex");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Alex", app.State.Profile?.DisplayName));
        Assert.Equal(navigation.BaseUri, navigation.Uri);
    }

    [Fact]
    public async Task An_empty_name_becomes_me()
    {
        using var app = await StartAsync();
        var cut = Render<Welcome>();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Me", app.State.Profile?.DisplayName));
    }

    [Fact]
    public async Task Restore_from_a_backup_creates_the_profile_and_opens_the_restore_flow()
    {
        using var app = await StartAsync();
        var cut = Render<Welcome>();

        cut.FindAll("button").First(b => b.TextContent.Contains("Restore from a backup", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Equal("Restore from a backup", cut.Find("h1").TextContent));
        Assert.NotNull(app.State.Profile);
    }

    [Fact]
    public async Task Restore_equals_one_in_the_address_opens_the_restore_flow_at_once()
    {
        using var app = await StartAsync();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/welcome?restore=1");

        var cut = Render<Welcome>();

        cut.WaitForAssertion(() => Assert.Equal("Restore from a backup", cut.Find("h1").TextContent));
        Assert.NotNull(app.State.Profile);
    }

    [Fact]
    public async Task The_restore_flow_opened_at_once_shows_what_the_chosen_file_holds()
    {
        using var app = await StartAsync();
        app.Picker.Enqueue(new Core.Platform.PickedFile("trip.md", "text/markdown", async () =>
        {
            await Task.Delay(50, Xunit.TestContext.Current.CancellationToken); // a real file does not open at once
            return new MemoryStream("Pack the tent"u8.ToArray());
        }));

        // On its own, as Welcome shows it: the picker opens after the first render, not from a click, so nothing
        // else re-renders when reading ends.
        var cut = Render<Components.RestoreFlow>(p => p.Add(f => f.StartPicking, true));

        cut.WaitForAssertion(
            () => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Restore 1 note"), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Restoring_a_backup_brings_its_labels_back_in_their_colours()
    {
        using var app = await StartAsync();
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            void Entry(string name, string text)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
            }

            Entry("trip.md", "---\ncreated: 2025-01-01T10:00:00+01:00\nlabels: [\"Trip\"]\n---\n\nPack the tent\n");
            Entry("manifest.json", """{"application":"Maple Notes","manifestVersion":3,"labels":[{"name":"Trip","color":"Teal"}],"notes":[{"path":"trip.md"}]}""");
        }

        app.Picker.Enqueue(new Core.Platform.PickedFile("backup.zip", "application/zip", () => Task.FromResult<Stream>(new MemoryStream(buffer.ToArray()))));
        var cut = Render<Components.RestoreFlow>(p => p.Add(f => f.StartPicking, true));
        cut.WaitForAssertion(
            () => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Restore 1 note"), TimeSpan.FromSeconds(5));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Restore 1 note").Click();

        cut.WaitForAssertion(() => Assert.Contains("Restored 1 note and 0 files. Added 1 label.", cut.Markup), TimeSpan.FromSeconds(5));
        var label = Assert.Single(await app.Core.Labels.ListAsync([Core.Domain.NoteKind.Note]));
        Assert.Equal(("Trip", Core.Domain.LabelColor.Teal, 1), (label.Label.Name, label.Label.Color, label.NoteCount));
    }
}
