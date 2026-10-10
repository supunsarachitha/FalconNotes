using FalconNotes.Core.Domain;
using FalconNotes.Core.Labels;
using FalconNotes.Core.Notes;

namespace FalconNotes.Core.Tests.Labels;

// The rules of the server's Labels/LabelsTests.cs.
public class LabelServiceTests
{
    private static readonly NoteKind[] Enabled = [NoteKind.Note, NoteKind.Todo, NoteKind.Quick];

    [Fact]
    public async Task Labels_are_created_renamed_recoloured_and_deleted()
    {
        using var app = await TestApp.StartAsync();

        var work = await app.Labels.CreateAsync("  Work  ");
        var home = await app.Labels.CreateAsync("home", LabelColor.Blue);

        Assert.Equal("Work", work.Name);
        Assert.Equal(LabelColor.Red, work.Color); // the first new label's colour
        Assert.Equal(LabelColor.Blue, home.Color);
        Assert.True(await app.Labels.UpdateAsync(work.Id, name: "Job", color: LabelColor.Green));
        Assert.True(await app.Labels.UpdateAsync(home.Id, color: LabelColor.Pink));
        Assert.Equal([("home", LabelColor.Pink), ("Job", LabelColor.Green)], (await app.Labels.ListAsync(Enabled)).Select(l => (l.Label.Name, l.Label.Color)));
        Assert.True(await app.Labels.DeleteAsync(home.Id));
        Assert.False(await app.Labels.DeleteAsync(home.Id));
        Assert.False(await app.Labels.UpdateAsync(home.Id, name: "x"));
        Assert.Single(await app.Labels.ListAsync(Enabled));
    }

    [Fact]
    public async Task New_labels_cycle_through_the_colours_and_sort_by_name_ignoring_case_and_accents()
    {
        using var app = await TestApp.StartAsync();
        string[] names = ["b", "É", "a", "e"];
        foreach (var name in names)
        {
            await app.Labels.CreateAsync(name);
            app.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        var labels = await app.Labels.ListAsync(Enabled);

        Assert.Equal(["a", "b"], labels.Take(2).Select(l => l.Label.Name));
        Assert.Equal(["e", "É"], labels.Skip(2).Select(l => l.Label.Name).Order(StringComparer.Ordinal)); // a tie on name: the ID decides
        Assert.Equal([LabelColor.Red, LabelColor.Orange, LabelColor.Amber, LabelColor.Green],
            labels.OrderBy(l => l.Label.CreatedAtUtc).Select(l => l.Label.Color));
    }

    [Theory]
    [InlineData("", "A label's name is 1 to 40 characters long.")]
    [InlineData("   ", "A label's name is 1 to 40 characters long.")]
    [InlineData("12345678901234567890123456789012345678901", "A label's name is 1 to 40 characters long.")]
    [InlineData(" WORK ", "You already have a label called “WORK”.")]
    public async Task Names_are_checked(string name, string message)
    {
        using var app = await TestApp.StartAsync();
        await app.Labels.CreateAsync("Work");

        var error = await Assert.ThrowsAsync<UserFacingException>(() => app.Labels.CreateAsync(name));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task Renaming_to_another_label_s_name_is_refused_but_to_its_own_is_fine()
    {
        using var app = await TestApp.StartAsync();
        var work = await app.Labels.CreateAsync("Work");
        await app.Labels.CreateAsync("Home");

        await Assert.ThrowsAsync<UserFacingException>(() => app.Labels.UpdateAsync(work.Id, name: "home"));
        Assert.True(await app.Labels.UpdateAsync(work.Id, name: "WORK"));
    }

    [Fact]
    public async Task Labels_go_on_notes_list_their_notes_and_count_active_notes_of_enabled_kinds()
    {
        using var app = await TestApp.StartAsync();
        var label = await app.Labels.CreateAsync("Work");
        var note = await app.Notes.CreateAsync("note");
        var todo = await app.Notes.CreateAsync("# List", NoteKind.Todo);
        var archived = await app.Notes.CreateAsync("archived");
        foreach (var id in new[] { note.Id, todo.Id, archived.Id })
        {
            await app.Notes.PatchAsync(id, new NotePatch(LabelIds: [label.Id]));
        }

        await app.Notes.PatchAsync(archived.Id, new NotePatch(IsArchived: true));

        Assert.Equal([label.Id], (await app.Notes.GetAsync(note.Id))!.LabelIds);
        Assert.Equal(2, (await app.Labels.ListAsync(Enabled)).Single().NoteCount);
        Assert.Equal(1, (await app.Labels.ListAsync([NoteKind.Note])).Single().NoteCount);
        Assert.Equal(2, (await app.Notes.ListAsync(new NoteQuery(NoteState.Active, Enabled, Label: label.Id))).Items.Count);
    }

    [Fact]
    public async Task Deleting_a_label_takes_it_off_its_notes_and_the_notes_stay()
    {
        using var app = await TestApp.StartAsync();
        var label = await app.Labels.CreateAsync("Work");
        var note = await app.Notes.CreateAsync("note");
        await app.Notes.PatchAsync(note.Id, new NotePatch(LabelIds: [label.Id]));

        await app.Labels.DeleteAsync(label.Id);

        Assert.Empty((await app.Notes.GetAsync(note.Id))!.LabelIds);
    }

    [Fact]
    public async Task A_label_can_hide_its_notes_from_home_and_quick_notes_but_not_from_its_own_page()
    {
        using var app = await TestApp.StartAsync();
        await app.Preferences.SaveAsync(new Preferences { Labels = true });
        var hidden = await app.Labels.CreateAsync("Private");
        Assert.True(await app.Labels.UpdateAsync(hidden.Id, hideNotes: true));
        var other = await app.Labels.CreateAsync("Work");
        var secret = await NewNoteAsync("Surprise party plans");
        var plain = await NewNoteAsync("Groceries");
        var pinned = await NewNoteAsync("Gift list");
        var quick = await NewNoteAsync("Call the florist", NoteKind.Quick);
        var todo = await NewNoteAsync("# Party\n\n- [ ] cake", NoteKind.Todo);
        await app.Notes.PatchAsync(secret.Id, new NotePatch(LabelIds: [hidden.Id, other.Id]));
        await app.Notes.PatchAsync(pinned.Id, new NotePatch(IsPinned: true, LabelIds: [hidden.Id]));
        await app.Notes.PatchAsync(quick.Id, new NotePatch(LabelIds: [hidden.Id]));
        await app.Notes.PatchAsync(todo.Id, new NotePatch(LabelIds: [hidden.Id]));

        Assert.Equal([plain.Id], await IdsAsync(new NoteQuery(NoteState.Feed, [NoteKind.Note])));
        Assert.Empty(await IdsAsync(new NoteQuery(NoteState.Pinned, [NoteKind.Note])));
        Assert.Empty(await IdsAsync(new NoteQuery(NoteState.Feed, [NoteKind.Quick])));
        Assert.Equal([todo.Id], await IdsAsync(new NoteQuery(NoteState.Feed, [NoteKind.Todo]))); // todo lists keep their tab
        Assert.Equal([secret.Id], await IdsAsync(new NoteQuery(NoteState.Active, Enabled, Label: other.Id)));
        Assert.Equal(
            [quick.Id, pinned.Id, secret.Id],
            await IdsAsync(new NoteQuery(NoteState.Active, [NoteKind.Note, NoteKind.Quick], Label: hidden.Id))); // the label's page
        Assert.Equal([secret.Id], await IdsAsync(new NoteQuery(NoteState.Active, Enabled, Search: "party plans"))); // search still finds it
        Assert.Equal([("Private", true), ("Work", false)], (await app.Labels.ListAsync(Enabled)).Select(l => (l.Label.Name, l.Label.HideNotes)));

        // Taking the label off a note brings it back; so does turning the option off, or labels altogether.
        await app.Notes.PatchAsync(secret.Id, new NotePatch(LabelIds: [other.Id]));
        Assert.Equal([plain.Id, secret.Id], await IdsAsync(new NoteQuery(NoteState.Feed, [NoteKind.Note])));
        Assert.True(await app.Labels.UpdateAsync(hidden.Id, hideNotes: false));
        Assert.Equal([quick.Id], await IdsAsync(new NoteQuery(NoteState.Feed, [NoteKind.Quick])));
        Assert.True(await app.Labels.UpdateAsync(hidden.Id, hideNotes: true));
        await app.Preferences.SaveAsync(new Preferences { Labels = false });
        Assert.Equal([pinned.Id], await IdsAsync(new NoteQuery(NoteState.Pinned, [NoteKind.Note])));

        async Task<Note> NewNoteAsync(string content, NoteKind kind = NoteKind.Note)
        {
            var note = await app.Notes.CreateAsync(content, kind);
            app.Clock.Advance(TimeSpan.FromSeconds(1)); // lists are newest first
            return note;
        }

        async Task<IEnumerable<Guid>> IdsAsync(NoteQuery query) => (await app.Notes.ListAsync(query)).Items.Select(n => n.Id);
    }

    [Fact]
    public async Task Hiding_a_label_s_notes_reloads_the_lists_and_renaming_it_does_not()
    {
        using var app = await TestApp.StartAsync();
        var label = await app.Labels.CreateAsync("Private");
        var reloads = 0;
        app.Feed.NotesChanged += () => reloads++;

        await app.Labels.UpdateAsync(label.Id, name: "Secret", color: LabelColor.Pink);
        Assert.Equal(0, reloads);
        await app.Labels.UpdateAsync(label.Id, hideNotes: true);

        Assert.Equal(1, reloads);
        var saved = (await app.Labels.ListAsync(Enabled)).Single().Label;
        Assert.Equal(("Secret", LabelColor.Pink, true), (saved.Name, saved.Color, saved.HideNotes)); // each change leaves the others
    }

    [Fact]
    public async Task A_note_carries_at_most_20_labels_and_only_labels_that_exist()
    {
        using var app = await TestApp.StartAsync();
        var labels = new List<Guid>();
        for (var i = 0; i < 21; i++)
        {
            labels.Add((await app.Labels.CreateAsync($"label {i}")).Id);
        }

        var note = await app.Notes.CreateAsync("note");

        Assert.Equal(20, (await app.Notes.PatchAsync(note.Id, new NotePatch(LabelIds: labels[..20])))!.LabelIds.Count);
        var tooMany = await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.PatchAsync(note.Id, new NotePatch(LabelIds: labels)));
        Assert.Equal("A note can have at most 20 labels.", tooMany.Message);
        var missing = await Assert.ThrowsAsync<UserFacingException>(() => app.Notes.PatchAsync(note.Id, new NotePatch(LabelIds: [Guid.NewGuid()])));
        Assert.Equal("One or more labels do not exist.", missing.Message);
        Assert.Equal(20, (await app.Notes.GetAsync(note.Id))!.LabelIds.Count); // a refused change changes nothing
    }

    [Fact]
    public async Task There_are_at_most_100_labels()
    {
        using var app = await TestApp.StartAsync();
        for (var i = 0; i < LabelRules.MaxLabels; i++)
        {
            await app.Labels.CreateAsync($"label {i}");
        }

        var error = await Assert.ThrowsAsync<UserFacingException>(() => app.Labels.CreateAsync("one more"));

        Assert.Equal("You can have at most 100 labels.", error.Message);
    }
}
