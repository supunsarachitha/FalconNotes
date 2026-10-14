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

    // New: the web app has no backgrounds (docs/06, Backgrounds).
    [Fact]
    public async Task The_background_is_plain_until_one_is_chosen_and_one_this_app_does_not_offer_is_plain_again()
    {
        using var app = await TestApp.StartAsync();
        Assert.Equal(Background.None, (await app.Preferences.GetAsync()).Background);

        await app.Preferences.SaveAsync(new Preferences { Background = Background.LunarNewYear });
        Assert.Equal(Background.LunarNewYear, (await app.Preferences.GetAsync()).Background);
        var stored = await app.Storage.Database.ReadAsync(connection => SettingsStore.GetRaw(connection, "preferences"));
        Assert.Contains("\"background\":\"LunarNewYear\"", stored, StringComparison.Ordinal);

        await app.Storage.Database.InTransactionAsync((connection, transaction) =>
        {
            using var command = Sql.Command(connection, "UPDATE Settings SET Value = $json WHERE Key = 'preferences'", transaction)
                .With("$json", """{"background":"Midsummer"}""");
            return command.ExecuteNonQuery();
        });
        Assert.Equal(Background.None, (await app.Preferences.GetAsync()).Background);
        Assert.Equal(Background.None, (await app.Preferences.SaveAsync(new Preferences { Background = (Background)99 })).Background);
    }

    // New: the web app has no locked notes (docs/04, Locked notes).
    [Fact]
    public async Task Note_lock_is_off_until_it_is_turned_on()
    {
        using var app = await TestApp.StartAsync();
        Assert.False((await app.Preferences.GetAsync()).NoteLock);
        Assert.False(new Preferences().NoteLock); // and for preferences saved before there was such a switch

        await app.Preferences.SaveAsync(new Preferences { NoteLock = true });

        Assert.True((await app.Preferences.GetAsync()).NoteLock);
    }

    // New: the web app always joins a single line break's lines (docs/04, Markdown rendering).
    [Fact]
    public async Task Line_breaks_are_joined_until_keeping_them_is_turned_on()
    {
        using var app = await TestApp.StartAsync();
        Assert.False((await app.Preferences.GetAsync()).LineBreaks);
        Assert.False(new Preferences().LineBreaks); // and for preferences saved before there was such a switch

        await app.Preferences.SaveAsync(new Preferences { LineBreaks = true });

        Assert.True((await app.Preferences.GetAsync()).LineBreaks);
        var stored = await app.Storage.Database.ReadAsync(connection => SettingsStore.GetRaw(connection, "preferences"));
        Assert.Contains("\"lineBreaks\":true", stored, StringComparison.Ordinal);
    }

    // New: the web app has no Export to PDF (docs/04, Export to PDF).
    [Fact]
    public async Task Export_to_pdf_is_off_until_it_is_turned_on()
    {
        using var app = await TestApp.StartAsync();
        Assert.False((await app.Preferences.GetAsync()).PdfExport);
        Assert.False(new Preferences().PdfExport); // and for preferences saved before there was such a switch

        await app.Preferences.SaveAsync(new Preferences { PdfExport = true });

        Assert.True((await app.Preferences.GetAsync()).PdfExport);
        var stored = await app.Storage.Database.ReadAsync(connection => SettingsStore.GetRaw(connection, "preferences"));
        Assert.Contains("\"pdfExport\":true", stored, StringComparison.Ordinal);
    }

    // New: the web app has no mood tracker (docs/04, Moods).
    [Fact]
    public async Task The_mood_tracker_is_off_until_it_is_turned_on()
    {
        using var app = await TestApp.StartAsync();
        Assert.False((await app.Preferences.GetAsync()).MoodTracker);
        Assert.False(new Preferences().MoodTracker); // and for preferences saved before there was such a switch

        await app.Preferences.SaveAsync(new Preferences { MoodTracker = true });

        Assert.True((await app.Preferences.GetAsync()).MoodTracker);
        var stored = await app.Storage.Database.ReadAsync(connection => SettingsStore.GetRaw(connection, "preferences"));
        Assert.Contains("\"moodTracker\":true", stored, StringComparison.Ordinal);
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
