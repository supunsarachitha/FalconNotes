using FalconNotes.Core.Domain;

namespace FalconNotes.Core.Labels;

/// <summary>Rules about labels shared by the service and the screens (docs/04, Labels).</summary>
public static class LabelRules
{
    /// <summary>The most characters in a label's name.</summary>
    public const int MaxNameLength = 40;

    /// <summary>The most labels there can be.</summary>
    public const int MaxLabels = 100;

    /// <summary>The most labels one note can have.</summary>
    public const int MaxPerNote = 20;

    /// <summary>
    /// Whether a label with this name exists already, ignoring case and surrounding spaces. Port of
    /// <c>hasLabelNamed</c> in <c>web/lib/labels.ts</c>.
    /// </summary>
    /// <param name="names">The existing labels' IDs and names.</param>
    /// <param name="name">The name to check.</param>
    /// <param name="except">A label to leave out (the one being renamed).</param>
    /// <returns>Whether the name is taken.</returns>
    public static bool HasLabelNamed(IEnumerable<(Guid Id, string Name)> names, string name, Guid? except = null)
    {
        var wanted = name.Trim().ToLowerInvariant();
        return names.Any(label => label.Id != except && label.Name.Trim().ToLowerInvariant() == wanted);
    }

    /// <summary>
    /// A new label's colour: it cycles through every colour except Grey, by how many labels there are
    /// (<c>Colors[(count % 9) + 1]</c>, as the server does).
    /// </summary>
    /// <param name="existingCount">How many labels there are now.</param>
    /// <returns>The colour.</returns>
    public static LabelColor NextColor(int existingCount) => (LabelColor)((existingCount % 9) + 1);
}
