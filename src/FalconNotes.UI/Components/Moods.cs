using FalconNotes.Core.Domain;

namespace FalconNotes.UI.Components;

/// <summary>
/// How each mood looks and what it is called (docs/07, Moods). The smilies are emoji, drawn by the device's own
/// emoji font, so the app ships no picture for them and they look as they do everywhere else on that device.
/// </summary>
public static class Moods
{
    /// <summary>The mood's smiley.</summary>
    /// <param name="mood">The mood.</param>
    /// <returns>The emoji; empty for none.</returns>
    public static string Emoji(NoteMood mood) => mood switch
    {
        NoteMood.Awful => "😢",
        NoteMood.Bad => "🙁",
        NoteMood.Okay => "😐",
        NoteMood.Good => "🙂",
        NoteMood.Great => "😄",
        _ => "",
    };

    /// <summary>The mood's name, for screen readers and tooltips: a smiley alone says too little.</summary>
    /// <param name="mood">The mood.</param>
    /// <returns>The name; empty for none.</returns>
    public static string Label(NoteMood mood) => mood switch
    {
        NoteMood.Awful => "Awful",
        NoteMood.Bad => "Bad",
        NoteMood.Okay => "Okay",
        NoteMood.Good => "Good",
        NoteMood.Great => "Great",
        _ => "",
    };
}
