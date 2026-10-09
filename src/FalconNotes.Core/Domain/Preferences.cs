namespace FalconNotes.Core.Domain;

/// <summary>Light or dark: follow the device, or always one of them.</summary>
public enum Theme
{
    /// <summary>Follow the device.</summary>
    System,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Always dark.</summary>
    Dark,
}

/// <summary>The accent colours offered in Settings. <see cref="Falcon"/> is the web app's Maple, renamed (D12).</summary>
public enum Accent
{
    /// <summary>The default: deep red (the web app's Maple colours).</summary>
    Falcon,

    /// <summary>Blue.</summary>
    Ocean,

    /// <summary>Green.</summary>
    Forest,

    /// <summary>Teal.</summary>
    Teal,

    /// <summary>Purple.</summary>
    Plum,

    /// <summary>Orange.</summary>
    Amber,

    /// <summary>Grey-blue.</summary>
    Slate,
}

/// <summary>
/// What is behind the page (docs/06, Backgrounds): the plain colour, or one of the festival patterns drawn for this
/// app. The web app has no such choice.
/// </summary>
public enum Background
{
    /// <summary>The default: the plain page colour.</summary>
    None,

    /// <summary>Pumpkins, bats and ghosts.</summary>
    Halloween,

    /// <summary>Trees, snowflakes and presents.</summary>
    Christmas,

    /// <summary>Fireworks, balloons and confetti.</summary>
    NewYear,

    /// <summary>Hearts and love letters.</summary>
    Valentine,

    /// <summary>Painted eggs, flowers and a rabbit.</summary>
    Easter,

    /// <summary>Oil lamps and rangoli.</summary>
    Diwali,

    /// <summary>Paper lanterns, lotus flowers and bodhi leaves.</summary>
    Vesak,

    /// <summary>Crescents, stars and lanterns.</summary>
    Eid,

    /// <summary>Red lanterns, blossom and coins.</summary>
    LunarNewYear,
}

/// <summary>The side menu's text size.</summary>
public enum MenuTextSize
{
    /// <summary>0.87 of the usual size.</summary>
    Small,

    /// <summary>The usual size.</summary>
    Medium,

    /// <summary>1.14 of the usual size.</summary>
    Large,
}

/// <summary>The first day of the week in calendars and the weekly habit chart.</summary>
public enum WeekStart
{
    /// <summary>The device's culture decides.</summary>
    Auto,

    /// <summary>Sunday.</summary>
    Sunday,

    /// <summary>Monday.</summary>
    Monday,

    /// <summary>Saturday.</summary>
    Saturday,
}

/// <summary>How far photos shrink before they are added; each is a longest side and a JPEG quality (<c>PhotoShrinker.Presets</c>).</summary>
public enum PhotoSize
{
    /// <summary>At most 2,560 pixels, quality 85%: sharp on large screens. The default, and the only size before 1.2.0.</summary>
    Large,

    /// <summary>At most 1,920 pixels, quality 80%.</summary>
    Medium,

    /// <summary>At most 1,280 pixels, quality 75%: the smallest files.</summary>
    Small,
}

/// <summary>
/// The user's writing, feature and appearance choices: the web app's <c>Preferences</c> without link previews
/// (docs/04, Preferences). Stored as camelCase JSON in <c>Settings['preferences']</c>.
/// </summary>
public sealed record Preferences
{
    /// <summary>Show a title field when writing; a title is the note's first line, as a Markdown heading.</summary>
    public bool NoteTitles { get; init; }

    /// <summary>With titles on, start a new note's title with today's date.</summary>
    public bool DateInTitles { get; init; }

    /// <summary>With titles on, give quick notes a title field too.</summary>
    public bool QuickNoteTitles { get; init; }

    /// <summary>How dates are written in titles and daily notes: one of <c>DateFormats.All</c>.</summary>
    public string DateFormat { get; init; } = "yyyy-MM-dd";

    /// <summary>Show the Todo page.</summary>
    public bool TodoLists { get; init; } = true;

    /// <summary>Show the Quick notes page.</summary>
    public bool QuickNotes { get; init; } = true;

    /// <summary>Show today's daily note at the top of Home.</summary>
    public bool DailyNotes { get; init; }

    /// <summary>
    /// The ID of the note whose text starts each new daily note (lower-case, with hyphens), or empty (the default) for
    /// none. Only the ID is kept here: the template is an ordinary note.
    /// </summary>
    public string DailyNoteTemplate { get; init; } = "";

    /// <summary>Show a month calendar in the side menu.</summary>
    public bool Calendar { get; init; } = true;

    /// <summary>Show the Habits page.</summary>
    public bool HabitTracker { get; init; }

    /// <summary>Show the Archive page and the Archive action.</summary>
    public bool Archive { get; init; } = true;

    /// <summary>Show the Tags page.</summary>
    public bool Tags { get; init; } = true;

    /// <summary>Show Help in the side menu. Off, the guide is still opened from the foot of Settings. On by default.</summary>
    public bool HelpMenu { get; init; } = true;

    /// <summary>Shrink photos before adding them, as JPEG. The original is not kept; off by default.</summary>
    public bool ShrinkPhotos { get; init; }

    /// <summary>With <see cref="ShrinkPhotos"/> on, how far.</summary>
    public PhotoSize PhotoSize { get; init; } = PhotoSize.Large;

    /// <summary>Double-tap (or double-click) a note to edit it.</summary>
    public bool DoubleTapToEdit { get; init; }

    /// <summary>Suggest existing tags while a <c>#tag</c> is typed.</summary>
    public bool TagSuggestions { get; init; }

    /// <summary>Coloured labels on notes.</summary>
    public bool Labels { get; init; }

    /// <summary>Deleting moves notes to the trash, where they stay for 30 days.</summary>
    public bool Trash { get; init; } = true;

    /// <summary>Light or dark.</summary>
    public Theme Theme { get; init; } = Theme.System;

    /// <summary>The accent colour.</summary>
    public Accent Accent { get; init; } = Accent.Falcon;

    /// <summary>What is behind the page: plain, or a festival pattern.</summary>
    public Background Background { get; init; } = Background.None;

    /// <summary>The side menu's text size.</summary>
    public MenuTextSize MenuTextSize { get; init; } = MenuTextSize.Medium;

    /// <summary>The first day of the week.</summary>
    public WeekStart WeekStart { get; init; } = WeekStart.Auto;

    /// <summary>The side menu's items in the chosen order, comma-separated; empty for the default order.</summary>
    public string MenuOrder { get; init; } = "";
}
