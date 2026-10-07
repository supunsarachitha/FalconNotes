using FalconNotes.Core.Domain;

namespace FalconNotes.UI.Components;

/// <summary>Each label colour's classes, written out in full so Tailwind finds them. Port of <c>lib/labels.ts</c>.</summary>
public static class LabelStyles
{
    /// <summary>A small round swatch.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The classes.</returns>
    public static string Dot(LabelColor color) => color switch
    {
        LabelColor.Red => "bg-red-500",
        LabelColor.Orange => "bg-orange-500",
        LabelColor.Amber => "bg-amber-500",
        LabelColor.Green => "bg-green-600",
        LabelColor.Teal => "bg-teal-600",
        LabelColor.Blue => "bg-blue-600",
        LabelColor.Indigo => "bg-indigo-600",
        LabelColor.Purple => "bg-purple-600",
        LabelColor.Pink => "bg-pink-500",
        _ => "bg-stone-500",
    };

    /// <summary>A chip: background and text, light and dark.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The classes.</returns>
    public static string Chip(LabelColor color) => color switch
    {
        LabelColor.Red => "bg-red-100 text-red-800 dark:bg-red-500/20 dark:text-red-300",
        LabelColor.Orange => "bg-orange-100 text-orange-800 dark:bg-orange-500/20 dark:text-orange-300",
        LabelColor.Amber => "bg-amber-100 text-amber-900 dark:bg-amber-500/20 dark:text-amber-300",
        LabelColor.Green => "bg-green-100 text-green-800 dark:bg-green-500/20 dark:text-green-300",
        LabelColor.Teal => "bg-teal-100 text-teal-800 dark:bg-teal-500/20 dark:text-teal-300",
        LabelColor.Blue => "bg-blue-100 text-blue-800 dark:bg-blue-500/20 dark:text-blue-300",
        LabelColor.Indigo => "bg-indigo-100 text-indigo-800 dark:bg-indigo-500/20 dark:text-indigo-300",
        LabelColor.Purple => "bg-purple-100 text-purple-800 dark:bg-purple-500/20 dark:text-purple-300",
        LabelColor.Pink => "bg-pink-100 text-pink-800 dark:bg-pink-500/20 dark:text-pink-300",
        _ => "bg-stone-200 text-stone-800 dark:bg-stone-700/60 dark:text-stone-200",
    };
}
