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
