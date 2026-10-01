using FalconNotes.Core.Domain;
using FalconNotes.Core.Labels;

namespace FalconNotes.Core.Tests;

public class NoteKindsTests
{
    [Fact]
    public void Enabled_kinds_follow_the_feature_switches_and_never_include_habits()
    {
        Assert.Equal([NoteKind.Note, NoteKind.Todo, NoteKind.Quick], NoteKinds.Enabled(new Preferences()));
        Assert.Equal([NoteKind.Note], NoteKinds.Enabled(new Preferences { TodoLists = false, QuickNotes = false, HabitTracker = true }));
    }

    [Fact]
    public void New_labels_cycle_through_every_colour_but_grey()
    {
        Assert.Equal(LabelColor.Red, LabelRules.NextColor(0));
        Assert.Equal(LabelColor.Pink, LabelRules.NextColor(8));
        Assert.Equal(LabelColor.Red, LabelRules.NextColor(9));
    }

    [Fact]
    public void Label_names_compare_ignoring_case_and_surrounding_spaces()
    {
        var id = Guid.NewGuid();
        (Guid, string)[] labels = [(id, "Work")];

        Assert.True(LabelRules.HasLabelNamed(labels, "  work "));
        Assert.False(LabelRules.HasLabelNamed(labels, "work", except: id));
        Assert.False(LabelRules.HasLabelNamed(labels, "home"));
    }
}
