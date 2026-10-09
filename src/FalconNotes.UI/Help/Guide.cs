namespace FalconNotes.UI.Help;

/// <summary>One section of the Help page: a Markdown body, rendered with the note Markdown renderer.</summary>
/// <param name="Id">The section's address, <c>/help#{id}</c>.</param>
/// <param name="Title">The heading.</param>
/// <param name="Body">The Markdown text. Tag examples are in code, because plain <c>#words</c> would become tag links.</param>
public sealed record GuideSection(string Id, string Title, string Body);

/// <summary>
/// The in-app user guide, adapted from the web app's <c>src/maple-web/src/help/guide.ts</c> (docs/08).
/// Sections marked "as in web" there are copied unchanged; the others are rewritten for an offline, single-device app
/// with no sign-in, server or link previews. The guide names the app "Falcon Notes" itself; its mentions of the Maple
/// Notes web app stay as written, unlike ported screen text.
/// </summary>
public static class Guide
{
    /// <summary>The sections, in the order the Help page shows them.</summary>
    public static readonly IReadOnlyList<GuideSection> Sections =
    [
        new("writing", "Writing notes", """
            Write in the box at the top of **Home** and choose **Post** (or press Ctrl+Enter, ⌘+Enter on a Mac). Your newest notes
            are at the top.

            - **Formatting.** Use the toolbar under the text box for bold, italic, headings, lists, checklists, quotes, code and
              links, or type [Markdown](https://commonmark.org/help/) yourself. Ctrl/⌘+B, I and K are shortcuts for bold, italic
              and links.
            - **Pictures and files.** Choose the paperclip and pick them. They are added while you write.
            - **Checklists.** Tick a checklist's boxes right in the note; the change is saved straight away.
            - **Changing a note.** Open a note's **⋯** menu to edit it, pin it to the top, label it, copy its text, archive it or
              delete it. Editing starts with the cursor at the end of the note, ready to carry on. Archived notes wait in the
              **Archive**, where you can restore them, and deleted ones in the [trash](/trash). To edit a note faster, turn on
              **Double-tap to edit** in [Settings → Writing](/settings/writing), then double-tap the note (or double-click it).
            """),

        new("titles", "Titles and dates", """
            Turn on **Note titles** in [Settings → Writing](/settings/writing) to get a title field above your text. The title is saved as the
            note's first line, as a heading, so it also shows up in searches and exports.

            With **Start titles with today's date**, new notes begin with the date. Choose how dates look under **Date format**.
            The date on its own is not enough to post, so an untouched box never becomes an empty note.

            Quick notes have no title field unless you also turn on **Titles on quick notes**.
            """),

        new("tags", "Tags", """
            Add tags anywhere in a note by typing a word after a hash, like `#ideas` or `#groceries`. Use a slash for
            nested tags, like `#work/meetings`.

            Choose a tag in a note, or open **Tags** in the menu, to see its notes. Choosing a parent tag such as `#work` also shows
            notes tagged `#work/meetings`. The Tags page lists every tag with how many notes use it, and has a filter for when
            there are many.

            To keep your tags consistent, turn on **Suggest tags while typing** in [Settings → Writing](/settings/writing). When you
            type a hash, the tags you already use appear under it, the most used first; pick one with the arrow keys and Enter (or
            Tab), or tap it. Esc closes the list.
            """),

        new("labels", "Labels", """
            Labels are coloured markers you put on notes and todo lists by hand, such as *Work* in blue or *Urgent* in red.
            Unlike tags, they are not part of what you write, so adding or removing one never changes a note.

            Turn on **Labels** in [Settings → Labels](/settings/labels), where you also create, rename, recolour and delete them.
            Then choose **Labels…** in a note's **⋯** menu to tick the ones it should have; you can create a new label right there
            by typing its name. A note shows its labels at the bottom, and choosing one, or a label in the side menu, lists its
            notes.

            Exports list each note's labels by name, and restoring one brings them back.
            """),

        new("search", "Searching", """
            Type in **Search notes** at the top of the menu and press Enter. Search looks through the text of your notes and the
            names of attached files. Archived notes are not included.
            """),

        new("todo", "Todo lists", """
            Open **Todo** in the menu and create a list, such as "Groceries". Add items in the box at the bottom of the list,
            tick them off as you go, and choose an item to change its text. The list's **⋯** menu can rename it, clear the
            ticked items, pin it, archive it or delete it.

            To change many items at once, choose **Edit as Markdown** in the list's **⋯** menu. Each item is a line: `- [ ]` for
            one to do and `- [x]` for one done. Reorder, add or delete lines, or paste a list; lines without a box become items
            to do. Choose **Save**, or press Ctrl+Enter (⌘+Enter on a Mac).

            Todo lists stay out of your Home timeline, but searches and tags find them.
            """),

        new("quick", "Quick notes", """
            **Quick notes** is a scratchpad for things you jot down in passing, kept out of your timeline. When a quick note
            turns out to be worth keeping, open its **⋯** menu and choose **Move to Home**. A timeline note can also move to
            quick notes.
            """),

        new("daily", "Daily notes", """
            Turn on **Daily notes** in [Settings → Features](/settings/features) to get a **Today** card at the top of Home, titled with today's
            date. Write in it and it becomes today's note; days you skip leave no empty notes.

            **A template** gives each new day's note the same start, such as headings or a checklist. Write it as an ordinary note,
            then choose **Use as daily-note template** from its **⋯** menu. Today's card then starts with its text, ready to fill in;
            the template's title, if it has one, is left out, and its files are not copied. Edit the note to change the template.
            A quick note or an archived note works well for it, as it stays out of your timeline. To stop, choose **Stop using as
            daily template** from its menu, or **Stop using** under Daily notes in [Settings → Features](/settings/features).
            """),

        new("calendar", "The calendar", """
            The calendar in the menu marks the days you wrote on. Choose a day to see that day's notes, and use the arrows to go
            to other months. You can turn the calendar off in [Settings → Features](/settings/features).
            """),

        new("habits", "Habits", """
            Turn on the **Habit tracker** in [Settings → Features](/settings/features) to get **Habits** in the menu. Add a habit, such as "Read 20
            minutes", then tap a day's circle on each day you do it; tap it again to undo. The last seven days are shown, with
            today outlined, and the arrows go back a week at a time, so you can fill in a day you missed.

            Under your habits, **Progress** shows how many of the possible days you did each week or month, for all your habits
            or just one, with your current and best streak. A habit counts from the day you added it.

            **Calendar** shows a whole month: for one habit, the days you did it; for all of them, the days you did all or some.
            Use the arrows to look at earlier months. To tick a day, use the list above.

            A habit's **⋯** menu renames, archives or deletes it. Archived habits keep their history under **Archived habits**
            until you restore them. Habits stay out of your timeline, searches and tags, but your exports and backups include
            them.
            """),

        new("pictures", "Pictures, video and files", """
            Choose a picture to open it full screen. Move between a note's pictures with the arrows, the arrow keys or a swipe,
            keep a copy with the save button, send it to another app with the share button, and close with Esc or ✕. Videos
            and audio play in the note. Choose any other file to open it in the app you use for that kind of file, or to save
            a copy.

            Pictures and players load as you scroll towards them, so a long timeline opens quickly however many files it has.

            To save space, turn on **Shrink photos before adding** in [Settings → Features](/settings/features): large photos are
            resized and saved as JPEG, and their location and camera details are left out. Then choose a **Photo size**: **Large**
            (2560 pixels on the longest side) stays sharp on big screens and is often a tenth of the original; **Medium** (1920
            pixels) and **Small** (1280 pixels) are smaller still, and saved at a lower quality, which suits photos you mostly
            look at on a phone. The full-size original is not kept, and photos you added earlier stay as they are.
            """),

        new("appearance", "Appearance", """
            Under [Settings → Appearance](/settings/appearance), choose **Light**, **Dark**, or **Device** to follow your phone
            or computer, pick an accent colour, and choose the day your weeks start on in the calendars. For a festive look, choose
            a **Background**, such as Halloween, Christmas, Diwali, Vesak or Eid: a pattern behind your notes, in light and dark.
            **None**, or **Remove background**, takes it away again.

            Under [Settings → Side menu](/settings/menu), put the menu's items in the order you use them, moving each one
            with its arrows. You can also make the menu's text smaller or larger.
            """),

        new("features", "Choosing your features", """
            Everything beyond plain notes can be switched on or off in [Settings → Features](/settings/features): todo
            lists, quick notes, the habit tracker, the Tags page, the archive, Help in the side menu, daily notes, the calendar,
            labels and the trash. Turning something off only hides it; nothing is deleted, and it all comes back when you turn it
            on again. With Help hidden from the menu, this guide is opened from the foot of [Settings](/settings).
            """),

        new("trash", "The trash", """
            Deleting a note, todo list or habit moves it to the trash, and **Undo** in the message that appears brings it
            straight back. The trash is not in the menu: open it from [Settings → Backup & data](/settings/data). There you can
            **Restore** anything to where it was, **Delete forever** one item, or **Empty trash**.

            Things stay in the trash for 30 days, then they are deleted for good by themselves. Until then they still count
            towards your storage, and they are left out of your timeline, searches, tags, the calendar and exports.

            If you would rather delete for good at once, turn the **Trash** off in [Settings → Features](/settings/features);
            deleting then asks first.
            """),

        new("privacy", "Keeping your notes private", """
            Falcon Notes keeps everything on this device and never sends your notes anywhere. Your notes, labels and files are
            stored encrypted, with a key that your phone or computer keeps in its secure storage, separate from the files. A copy
            of the app's files is therefore unreadable anywhere else.

            Two things are up to you:

            - **Exports are not encrypted.** A ZIP you export can be opened by anyone who gets it, so keep it somewhere safe.
            - **Uninstalling Falcon Notes deletes your notes** from this device, together with their key. Export them first.

            To keep people who pick up your device out of the app, turn on the [app lock](/settings/security).
            """),

        new("lock", "The app lock", """
            Turn on **Lock Falcon Notes** in [Settings → Privacy & security](/settings/security) and choose a PIN. Falcon Notes then
            asks for it when it opens, and when it comes back after being in the background for longer than you chose under **Lock
            after**. To lock straight away, choose the lock button at the bottom of the menu.

            Where your device has a fingerprint reader, face recognition, Windows Hello or Touch ID, you can unlock with that
            instead.

            Write your PIN down somewhere safe. It cannot be reset: if you forget it, the only way back in is to erase the app's
            data on this device and restore your notes from a backup.
            """),

        new("account", "Your name", """
            [Settings → Profile](/settings/account) shows the name Falcon Notes uses for you, at the bottom of the menu and in your
            exports. Choose **Change** to pick another.
            """),

        new("backup", "Backing up and restoring", """
            Your notes live only on this device, so make a backup from time to time. Under [Settings → Backup & data](/settings/data),
            **Export** saves your notes as a ZIP file of Markdown, plain text or JSON, with your files, in folders by year, month or
            day. Keep it somewhere safe, such as a USB drive or a cloud folder you trust. Exports are not encrypted, so store them
            with care.

            So that you need not remember, turn on **Back up automatically** in the same place and choose a folder. Falcon Notes
            then saves a backup of everything there every day, week or month, keeps the newest few and deletes the older ones. It
            does this when it is open, so open it now and then. These backups are not encrypted either, so choose a folder only
            you can open. A folder on this device is lost with the device, so copy the backups somewhere else from time to time.

            **Restore** brings notes back from such a file. It also works with exports from the Maple Notes web app and its
            servers, and their restore works with yours. Notes keep their dates, pins, archive state, labels and files, and
            notes you already have are skipped, so restoring twice does no harm. A label is matched by name to one you
            already have, or created in the colour it had. You can also add single Markdown, text or JSON files as notes.

            To start over, **Delete all notes and files**, in the same place, deletes everything you wrote and added at once,
            labels and the trash included. Your name, settings and app lock stay. This cannot be undone, so export first if you
            might want your notes back.
            """),

        new("devices", "Moving to another device", """
            Falcon Notes does not sync. To move your notes to a new phone or computer, **Export** them on the old one, copy the ZIP
            across, and **Restore** it on the new one, in [Settings → Backup & data](/settings/data). Labels come with the notes
            that carry them. Your settings do not travel with it, so set them up again on the new device.
            """),

        new("storage", "How much you store", """
            [Settings → Profile](/settings/account) shows how much space your notes and files take, with how many of each.
            Archived notes, the trash and files count too; deleting them for good frees the space. There is no limit other than
            the free space on your device.
            """),

        new("shortcuts", "Keyboard shortcuts", """
            On a computer, or a tablet with a keyboard:

            | Keys | What they do |
            |---|---|
            | Ctrl+Enter / ⌘+Enter | Post or save the note you are writing |
            | Esc | Cancel an edit, or close a dialog or the picture viewer |
            | Ctrl+B / ⌘+B | Bold |
            | Ctrl+I / ⌘+I | Italic |
            | Ctrl+K / ⌘+K | Link |
            | ← → | Previous and next picture in the viewer |
            """),
    ];
}
