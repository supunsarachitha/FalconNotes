# 8. Help guide

The in-app guide (`Help`), adapted from `reference/maple-notes-1.8.0/src/maple-web/src/help/guide.ts`. Ship it as
`FalconNotes.UI/Help/Guide.cs`: a list of `(Id, Title, Body)` with Markdown bodies, in this order. Bodies are rendered
with the note Markdown renderer. Tag examples are in `code`, because plain `#words` would become tag links.

The guide names the app "Falcon Notes" itself. Do not port `HelpPage.tsx`'s `replaceAll("Maple Notes", appName)`: the
guide's mentions of the Maple Notes web app must stay as written.

Sections marked **as in web** are copied from the reference without change. The others are given in full here.

| # | Id | Title | Source |
|---|---|---|---|
| 1 | `writing` | Writing notes | changed |
| 2 | `titles` | Titles and dates | as in web |
| 3 | `tags` | Tags | as in web |
| 4 | `labels` | Labels | changed |
| 5 | `search` | Searching | as in web |
| 6 | `todo` | Todo lists | as in web |
| 7 | `quick` | Quick notes | as in web |
| 8 | `daily` | Daily notes | changed |
| 9 | `calendar` | The calendar | as in web |
| 10 | `habits` | Habits | as in web |
| 11 | `pictures` | Pictures, video and files | changed |
| 12 | `appearance` | Appearance | changed |
| 13 | `features` | Choosing your features | changed |
| 14 | `trash` | The trash | as in web |
| 15 | `privacy` | Keeping your notes private | new (replaces `encryption`) |
| 16 | `lock` | The app lock | new |
| 17 | `account` | Your name | changed |
| 18 | `backup` | Backing up and restoring | changed |
| 19 | `devices` | Moving to another device | new |
| 20 | `storage` | How much you store | changed |
| 21 | `shortcuts` | Keyboard shortcuts | changed |

Removed: `links` (link previews), `encryption`, `password`, `https`, `admins`.

### Left out of 1.0.0

The guide describes what the released app does (updated at the owner's request, 2026-10-07). Three things below are
in the plan but not built, so their text is left out until they are. Put each back, in the section named,
with the feature:

| Section | Text to put back | Waiting for |
|---|---|---|
| `writing` | "Choose the paperclip, or on a computer paste or drag files onto the box." | Paste and drag-and-drop in the composer (desktop) |
| `pictures` | "To save space, turn on **Shrink photos before adding** in [Settings → Features](/settings/features): large photos are resized to 2560 pixels on their longest side and saved as JPEG, often a tenth of the size, and their location and camera details are left out. The full-size original is not kept, and photos you added earlier stay as they are." | An `IImageCodec` (the SkiaSharp decision, [02](02-architecture.md#dependencies)) |
| `appearance` | "drag an item by its handle, or move it with its arrows" | Pointer drag in `MenuOrderEditor` |

Added in 1.0.0: `pictures` mentions the viewer's share button (Android, [12](12-platforms.md)).

Put back with biometric unlock (2026-10-08): the `lock` section's sentence on fingerprint, face, Windows Hello and
Touch ID.

## 1. Writing notes (`writing`)

````markdown
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
````

## 4. Labels (`labels`)

````markdown
Labels are coloured markers you put on notes and todo lists by hand, such as *Work* in blue or *Urgent* in red.
Unlike tags, they are not part of what you write, so adding or removing one never changes a note.

Turn on **Labels** in [Settings → Labels](/settings/labels), where you also create, rename, recolour and delete them.
Then choose **Labels…** in a note's **⋯** menu to tick the ones it should have; you can create a new label right there
by typing its name. A note shows its labels at the bottom, and choosing one, or a label in the side menu, lists its
notes.

Exports do not include labels yet, so labels stay on this device when you move your notes elsewhere.
````

## 8. Daily notes (`daily`)

````markdown
Turn on **Daily notes** in [Settings → Features](/settings/features) to get a **Today** card at the top of Home, titled with today's
date. Write in it and it becomes today's note; days you skip leave no empty notes.
````

## 11. Pictures, video and files (`pictures`)

````markdown
Choose a picture to open it full screen. Move between a note's pictures with the arrows, the arrow keys or a swipe,
keep a copy with the save button, send it to another app with the share button, and close with Esc or ✕. Videos
and audio play in the note. Choose any other file to open it in the app you use for that kind of file, or to save
a copy.

Pictures and players load as you scroll towards them, so a long timeline opens quickly however many files it has.
````

Rename the Features switch to match: **Shrink photos before adding**. Its description: "Large photos are resized to
2560 pixels on their longest side and saved as JPEG, often a tenth of the size. Their location and camera details are
left out too. Photos already added stay as they are."

## 12. Appearance (`appearance`)

````markdown
Under [Settings → Appearance](/settings/appearance), choose **Light**, **Dark**, or **Device** to follow your phone
or computer, pick an accent colour, and choose the day your weeks start on in the calendars.

Under [Settings → Side menu](/settings/menu), put the menu's items in the order you use them, moving each one
with its arrows. You can also make the menu's text smaller or larger.
````

## 13. Choosing your features (`features`)

````markdown
Everything beyond plain notes can be switched on or off in [Settings → Features](/settings/features): todo
lists, quick notes, the habit tracker, the Tags page, the archive, daily notes, the calendar, labels and the trash.
Turning something off only hides it; nothing is deleted, and it all comes back when you turn it on again.
````

## 15. Keeping your notes private (`privacy`)

````markdown
Falcon Notes keeps everything on this device and never sends your notes anywhere. Your notes, labels and files are
stored encrypted, with a key that your phone or computer keeps in its secure storage, separate from the files. A copy
of the app's files is therefore unreadable anywhere else.

Two things are up to you:

- **Exports are not encrypted.** A ZIP you export can be opened by anyone who gets it, so keep it somewhere safe.
- **Uninstalling Falcon Notes deletes your notes** from this device, together with their key. Export them first.

To keep people who pick up your device out of the app, turn on the [app lock](/settings/security).
````

## 16. The app lock (`lock`)

````markdown
Turn on **Lock Falcon Notes** in [Settings → Privacy & security](/settings/security) and choose a PIN. Falcon Notes then
asks for it when it opens, and when it comes back after being in the background for longer than you chose under **Lock
after**. To lock straight away, choose the lock button at the bottom of the menu.

Where your device has a fingerprint reader, face recognition, Windows Hello or Touch ID, you can unlock with that
instead.

Write your PIN down somewhere safe. It cannot be reset: if you forget it, the only way back in is to erase the app's
data on this device and restore your notes from a backup.
````

## 17. Your name (`account`)

````markdown
[Settings → Profile](/settings/account) shows the name Falcon Notes uses for you, at the bottom of the menu and in your
exports. Choose **Change** to pick another.
````

## 18. Backing up and restoring (`backup`)

````markdown
Your notes live only on this device, so make a backup from time to time. Under [Settings → Backup & data](/settings/data),
**Export** saves your notes as a ZIP file of Markdown, plain text or JSON, with your files, in folders by year, month or
day. Keep it somewhere safe, such as a USB drive or a cloud folder you trust. Exports are not encrypted, so store them
with care.

**Restore** brings notes back from such a file. It also works with exports from the Maple Notes web app and its
servers, and their restore works with yours. Notes keep their dates, pins, archive state and files, and notes you
already have are skipped, so restoring twice does no harm. You can also add single Markdown, text or JSON files as
notes. Labels are not part of exports yet.

To start over, **Delete all notes and files**, in the same place, deletes everything you wrote and added at once,
labels and the trash included. Your name, settings and app lock stay. This cannot be undone, so export first if you
might want your notes back.
````

## 19. Moving to another device (`devices`)

````markdown
Falcon Notes does not sync. To move your notes to a new phone or computer, **Export** them on the old one, copy the ZIP
across, and **Restore** it on the new one, in [Settings → Backup & data](/settings/data). Your settings and labels do
not travel with it, so set them up again on the new device.
````

## 20. How much you store (`storage`)

````markdown
[Settings → Profile](/settings/account) shows how much space your notes and files take, with how many of each.
Archived notes, the trash and files count too; deleting them for good frees the space. There is no limit other than
the free space on your device.
````

## 21. Keyboard shortcuts (`shortcuts`)

````markdown
On a computer, or a tablet with a keyboard:

| Keys | What they do |
|---|---|
| Ctrl+Enter / ⌘+Enter | Post or save the note you are writing |
| Esc | Cancel an edit, or close a dialog or the picture viewer |
| Ctrl+B / ⌘+B | Bold |
| Ctrl+I / ⌘+I | Italic |
| Ctrl+K / ⌘+K | Link |
| ← → | Previous and next picture in the viewer |
````
