using FalconNotes.Core.Domain;
using FalconNotes.Core.Storage;

namespace FalconNotes.Core.Tests.Settings;

public class SettingsTests
{
    [Fact]
    public async Task Preferences_start_with_the_defaults_and_save()
    {
        using var app = await TestApp.StartAsync();
        var raised = 0;
        app.Feed.PreferencesChanged += () => raised++;

        Assert.Equal(new Preferences(), await app.Preferences.GetAsync());
        await app.Preferences.SaveAsync(new Preferences { Accent = Accent.Ocean, WeekStart = WeekStart.Monday, MenuOrder = "help,home" });

        var saved = await app.Preferences.GetAsync();
        Assert.Equal(Accent.Ocean, saved.Accent);
        Assert.Equal(WeekStart.Monday, saved.WeekStart);
        Assert.Equal("help,home,todo,quick,habits,tags,archive,settings", saved.MenuOrder);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Stored_preferences_are_camelCase_and_unknown_or_missing_values_take_their_defaults()
    {
        using var app = await TestApp.StartAsync();
        await app.Storage.Database.InTransactionAsync((connection, transaction) =>
        {
            using var command = Sql.Command(connection, "INSERT INTO Settings (Key, Value) VALUES ('preferences', $json)", transaction)
                .With("$json", """{"labels":true,"accent":"Maple","theme":"dark","dateFormat":"nope","linkPreviews":true,"weekStart":7}""");
            return command.ExecuteNonQuery();
        });

        var preferences = await app.Preferences.GetAsync();

        Assert.True(preferences.Labels);
        Assert.Equal(Theme.Dark, preferences.Theme);
        Assert.Equal(Accent.Falcon, preferences.Accent); // an accent this app does not offer
        Assert.Equal("yyyy-MM-dd", preferences.DateFormat);
        Assert.Equal(WeekStart.Auto, preferences.WeekStart);
        Assert.True(preferences.TodoLists); // missing: the default
    }

    // Ported from the server's PreferencesTests.cs (Maple Notes 1.9.0 to 1.15.0). There a value that is not offered
    // is refused; here it falls back to its default, as every preference does.
    [Fact]
    public async Task Help_in_the_menu_the_photo_size_and_the_daily_note_template_have_defaults_and_save()
    {
        using var app = await TestApp.StartAsync();
        var defaults = await app.Preferences.GetAsync();
        Assert.True(defaults.HelpMenu);
        Assert.Equal("", defaults.DailyNoteTemplate); // no template until one is chosen
        Assert.Equal(PhotoSize.Large, defaults.PhotoSize); // as photos were shrunk before sizes could be chosen

        await app.Preferences.SaveAsync(new Preferences
        {
            HelpMenu = false, PhotoSize = PhotoSize.Small, DailyNoteTemplate = "0199A1B2-C3D4-E5F6-0718-293A4B5C6D7E",
        });

        var saved = await app.Preferences.GetAsync();
        Assert.Equal((false, PhotoSize.Small, "0199a1b2-c3d4-e5f6-0718-293a4b5c6d7e"), (saved.HelpMenu, saved.PhotoSize, saved.DailyNoteTemplate));
    }

    [Theory]
    [InlineData("today")]
    [InlineData("0199a1b2c3d4e5f60718293a4b5c6d7e")]
    [InlineData("{0199a1b2-c3d4-e5f6-0718-293a4b5c6d7e}")]
    public async Task The_daily_note_template_is_a_note_id_or_nothing(string template)
    {
        using var app = await TestApp.StartAsync();

        var saved = await app.Preferences.SaveAsync(new Preferences { DailyNoteTemplate = template, PhotoSize = (PhotoSize)7 });

        Assert.Equal(("", PhotoSize.Large), (saved.DailyNoteTemplate, saved.PhotoSize));
    }

    [Fact]
    public async Task The_profile_is_created_once_with_a_name_or_Me()
    {
        using var app = await TestApp.StartAsync();

        Assert.Null(await app.Profile.GetAsync());
        var profile = await app.Profile.CreateAsync("   ");

        Assert.Equal("Me", profile.DisplayName);
        Assert.Equal(app.Clock.Now.UtcDateTime, profile.CreatedAtUtc);
        Assert.Equal(profile, await app.Profile.GetAsync());
    }

    [Fact]
    public async Task The_display_name_changes_but_cannot_be_empty()
    {
        using var app = await TestApp.StartAsync();
        await app.Profile.CreateAsync("Sam");

        var changed = await app.Profile.SetDisplayNameAsync("  Samira   K ");
        var error = await Assert.ThrowsAsync<UserFacingException>(() => app.Profile.SetDisplayNameAsync(" "));

        Assert.Equal("Samira K", changed.DisplayName);
        Assert.Equal("Enter a name.", error.Message);
        Assert.Equal(64, (await app.Profile.SetDisplayNameAsync(new string('x', 100))).DisplayName.Length);
    }
}
