# 7. Screens

For each screen: where it comes from in the web app's `src/maple-web/src/`, and only what differs. Anything
not mentioned is ported as it is: markup, classes, copy, keyboard handling, empty and loading states, toasts and ARIA.

## Wording

Server words change everywhere they appear in ported copy:

| Web app says | This app says |
|---|---|
| upload, uploads, uploading (files) | add, adds, adding (e.g. "They are added while you write.") |
| download (attachments) | save a copy |
| "Upload failed." | "Could not add this file." |
| your account, the server, this server, administrators | this device / this app, or drop the phrase |
| sign in, sign out | drop the phrase |
| "Download ZIP" | "Export ZIP…" |
| Maple Notes (meaning this app) | Falcon Notes. Keep "Maple Notes" where the text means the web app, its servers or its exports. |
| 🍁 ("You're all caught up 🍁") | 🦅. There is no falcon emoji, and 🦅 (Unicode 9) shows on every supported platform. |

Never mention passwords, encryption modes, recovery keys, sessions, administrators, storage limits or link previews.

## App shell

`components/AppShell.tsx`. Changes:

- **Sidebar footer**: avatar (first letter of the display name, upper case), the display name, and under it "On this
  device" (in place of `@username`). When the app lock is on, a **Lock** icon button (`Lock` icon, label "Lock
  now") locks at once. There is no Sign out.
- **App name**: always "Falcon Notes" with the falcon mark (D12), wherever the reference shows `appName` or
  `BrandMark`. There is no branding.
- The search box, menu items (with the Tags count), labels list and calendar are unchanged. Since web 1.16.0 a label
  that hides its notes carries a small `EyeOff` icon in the labels list, named "hidden from Home and Quick notes".
  Here the name is on a `role="img"` wrapper, because `Icon` is always decorative.

## Home and filters

`pages/HomePage.tsx`, `components/Composer.tsx`, `components/NoteList.tsx`, `components/NoteCard.tsx`. Unchanged apart from
these:

- **Note menu**: as in the web app (Pin, Edit, Labels…, Move to Home / quick notes, Use as daily-note template / Stop
  using as daily template, Copy text, Archive/Restore, Move to trash / Delete…), with two items of this app's own
  after Copy text, each only while its switch is on: **Export to PDF…** ([Export to PDF](#export-to-pdf)) and
  **Lock** ([Locked notes](#locked-notes)). Copy text uses `IClipboard`: "Copied to the
  clipboard." / "Copying is not allowed here." The template item is the web app's since 1.15
  ([04](04-domain-rules.md#daily-notes)).
- **Labels…** opens its dialog with the focus on the dialog itself, not on the find-or-create field, so a phone's
  keyboard stays down until the field is tapped (the web app since 1.8.1). The dialog has `tabindex="-1"` and
  `data-focus-self`, which `dialogs.js` acts on after `showModal()`.
- **Link previews** are gone.
- **A failed automatic backup** (new, [05](05-backup-compatibility.md#automatic-backups)): above everything else on
  Home, not on the filtered views, "The last automatic backup failed. {reason}" with a link **Open Backup & data**
  (`/settings/data`), in the colours of `ErrorMessage`, as a `status`. It stays until a backup works, or automatic
  backups are turned off. A backup that fails where nobody looks is worse than none, because it is relied on.
- **Filtered views** (`?tag`, `?q`, `?day`, `?label`) and their empty states are unchanged. Since web 1.16.0 the page
  of a label that hides its notes says so under its heading, with a link to Settings → Labels.

## Composer

`components/Composer.tsx`, `FormatToolbar.tsx`, `TagSuggestions.tsx`. Changes:

- The paperclip ("Attach files") opens the native file picker (several files). Paste and drag-and-drop work on
  Windows and macOS. A file's tile shows a progress bar while it is encrypted and stored. On failure, the red tile's
  tooltip and label read "Could not add this file."
- On phones, hide the "Ctrl/⌘ + Enter to post" hint, as at `sm` in the reference.
- With the mood tracker on, a smiley button stands after the paperclip ([Moods](#moods)); with stickers on, a
  sticker button follows it ([Stickers](#stickers)).

## Attachments

`components/AttachmentGallery.tsx`, `components/ImageViewer.tsx`. Changes:

- **File chips**: choosing one opens a menu with **Open** (`IFileOpener`, from a decrypted copy in the cache) and **Save a
  copy…** (`IFileSaver`).
- **Image viewer**: the download button becomes **Save a copy** (`Download` icon, label "Save a copy of {name}"). On
  Android, add **Share** (`Share` icon), which hands a decrypted copy to the system share sheet.
- **Videos over 50 MB**, if the media mechanism can only use blob URLs ([02](02-architecture.md#serving-attachments-to-the-webview)):
  show the poster tile with a play button that opens the video in the system player instead.

## Locked notes

New (1.4.0, 2026-10-12, at the owner's request); the web app has none. The rules are in
[04](04-domain-rules.md#locked-notes) and the PIN in [03](03-data-storage-and-security.md#locked-notes).
`NoteCard.razor` shows the locked card, and `NoteLock.razor` holds the two dialogs for it and for `TodoCard.razor`.

- **Lock** (`Lock` icon) is in the ⋯ menu of a note, a quick note and a todo list, archived ones too, **while Note
  lock is turned on** ([Settings → Privacy & security](#settings--privacy--security)): after Copy text in `NoteCard`,
  after Clear completed in `TodoCard`. A habit has none. With no PIN in use, and none typed or chosen since the app
  started, it first opens **Set a PIN** (the dialog of Settings → Privacy & security) with this text: "Choose a PIN
  of 4 to 8 digits for your locked notes. It cannot be reset: if you forget it, a locked note can only be deleted.
  The app lock, if you turn it on, uses the same PIN." Then, and at once otherwise, the toast "Locked. Unlocking it
  takes your PIN."
- **The locked card** is the note's own card with its header as it was (the time, "· edited", the kind in a mixed
  list, "Pinned"). In the top right corner, beside the ⋯ button, it has a **lock mark**: the `Lock` icon at `size-4`
  in the pin mark's colour (`text-maple-600 dark:text-maple-400`), named "Locked" on a `role="img"` wrapper, with the
  same tooltip. The owner asked for it to look like the pin mark and sit in that corner (2026-10-12). In place of the
  title, text, files and labels there is one line in `text-stone-500 dark:text-stone-400`: "This {note | list} is
  locked. Choose Unlock in its ⋯ menu to open it." Nothing of the note is in the page, and double-tap to edit does
  nothing.
- **Its menu** has only what does not show the note: Pin to top / Unpin (not when archived), **Unlock…** (`LockOpen`
  icon), Archive / Restore, and Move to trash / Delete…. With the trash off, the confirmation does not count its
  files. This menu does not depend on the switch.
- **Unlock…** opens the PIN check Settings uses (`VerifyPinDialog`), titled "Unlock this {note | list}": "Enter your
  PIN to continue.", the PIN field, and "Use {fingerprint or face | Windows Hello | Touch ID}" where the app lock
  allows it. A wrong PIN: "That PIN is not right."; after five: "Too many tries. Try again in {n} seconds." (the
  dialog says this wherever it is used, since 1.4.0). Then the toast "Unlocked. It locks again when you leave this
  page or the app."
- **The open card** is the note's usual card, with everything it usually offers, and two differences. The mark in
  the corner is an open lock (`LockOpen`, named "Unlocked for now"; on `TodoCard` it stands beside the pin mark). In
  the menu, where **Lock** would be, there are **Lock now** (`Lock` icon; toast "Locked.") and **Remove lock**
  (`LockOpen` icon; toast "Lock removed."), and the daily-note template is not offered. It becomes the locked card
  again by itself when another page is opened or the app is left ([04](04-domain-rules.md#locked-notes)); an open
  editor stays until it is saved or cancelled.
- **Todo tab**: a locked list is shown as the locked card, not as `TodoCard`, so it has "Note actions" and no items.
  Unlocked to be read, it is `TodoCard` again.
- **Today**: today's daily note can be locked like any note, and then shows locked under Today.
- **Trash**: `TrashCard` shows a locked note with the lock mark at the end of its header and "This {note | list} is
  locked. Restore it, then choose Unlock in its ⋯ menu to open it." in place of its text and file count. **Restore**
  and **Delete forever…** work as for any note.
- Any failure: "That didn't work. Please try again."

## Export to PDF

New (1.5.0, 2026-10-13, at the owner's request); the web app has none. The rules, and what the PDF holds, are in
[04](04-domain-rules.md#export-to-pdf). `NotePdfExport.cs` is the helper `NoteCard.razor` and `TodoCard.razor`
share.

- **The switch** is in [Settings → Features](#settings), last in **Notes and files**: `PreferenceSwitch` **Export
  to PDF**, **off by default** ("Adds Export to PDF… to the ⋯ menu of notes, quick notes and todo lists. It saves
  the note, with its pictures, as a PDF file where you choose on this device. The PDF is not encrypted, so keep it
  somewhere safe."). It is shown only on a platform that can write a PDF ([12](12-platforms.md)).
- **Export to PDF…** (`Download` icon) is then in the ⋯ menu of a note and a quick note, after Copy text, and of a
  todo list, after Clear completed; archived ones too. A locked card has none until it is unlocked to be read, and
  a habit and a note in the trash never have one.
- Choosing it makes the PDF, then opens the system's save dialog with the note's first line as the file's name
  ("Shopping list.pdf"). Saved: the toast "Saved as a PDF." Cancelled in the dialog: nothing. Any failure: "The PDF
  could not be made. Please try again." A second tap while a PDF is being made does nothing.
- There is no progress text: the PDF of a note is made before the dialog has opened.

## Moods

New in this app (1.7.0, 2026-10-13, at the owner's request; the rules are in [04](04-domain-rules.md#moods)). The
web app has no such thing yet, so the markup is this app's own, in the reference's classes. Nothing below is there
while **Mood tracker** is off, which it is as installed.

- **The smilies** are emoji, as the owner chose: 😢 Awful, 🙁 Bad, 😐 Okay, 🙂 Good, 😄 Great (`Moods.cs`). The
  device's emoji font draws them, so they differ a little between Android, Windows and macOS, and the app ships no
  picture for them. A smiley alone says too little to a screen reader, so each one carries its name.
- **The composer** (Home, the Today card, Quick notes, and a note being edited) has a second round button after
  the paperclip, an `IconButton` like it. With no mood chosen it shows the `Smile` icon and is named "Mood"; with
  one, it shows that smiley and is named "Mood: Good". It has `aria-expanded` and `aria-controls`, and opens and
  closes a row above the buttons (`MoodPicker`): a `group` named "Mood" of five round buttons, worst to best, each
  a smiley named by its mood, with `aria-pressed`. The chosen one has the accent's ring and tint. Choosing one
  closes the row; **choosing the chosen one takes the mood off**, so there is no sixth button for "none". After
  **Post** the next note starts without a mood. A todo list opened for editing from a search has no such button.
- **The note card** shows the smiley in its header, after the time and "· edited", as a `role="img"` named and
  titled "Mood: Good". A note without a mood shows nothing there. A locked note that is not open shows none
  ([Locked notes](#locked-notes)). The Archive shows it too; the trash does not.
- **Settings → Features**, under Home and menu, after Labels: **Mood tracker**, off by default, "A smiley button
  where you write a note or quick note: choose one of five smilies for how you feel, and it shows on the note.
  Turning it off hides the smilies; the moods you chose are kept."
- There is no page, no menu item, no filter and no chart: the owner asked for the mood to be shown in the note.


## Stickers

New in this app (1.7.0, 2026-10-14, at the owner's request; the rules are in [04](04-domain-rules.md#stickers)).
The web app has no such thing, so the markup is this app's own, in the reference's classes. The sticker button and
the packs are not there while **Stickers** is off, which it is as installed; stickers already on notes show either
way.

- **The composer** (Home, the Today card, Quick notes, and a note being edited) has a round `IconButton` after the
  mood button, or after the paperclip without it: the `Sticker` icon (Lucide `sticker`), named "Stickers", with
  `aria-expanded` and `aria-controls`. It opens and closes the **sticker picker** above the buttons
  (`StickerPicker`), a `group` named "Stickers"; the smilies and the stickers take the same place, so opening one
  closes the other. With several packs, a row of pill buttons, one per pack by name with `aria-pressed`, chooses
  the pack shown; under it a grid of the pack's stickers, four across (six from `sm`), at most 16rem high and
  scrolling, each a button named "Add sticker: {name}" with the picture whole inside. **Each sticker chosen is added
  at once** to the composer's files, with the same progress and ✕ as any file, its thumbnail whole and not cropped,
  and **the picker stays open**, so several can be added. After **Post** it closes. With no pack on the device it
  says "No sticker packs yet. Add one in Settings → Features." with a link there.
- **The note card** shows its stickers first in its files, before the pictures: a row that wraps, a list named
  "Stickers", each picture whole in a 5rem square (6rem from `sm`), described by its name ("Waving cat"). They are
  not in the picture grid and not in the full-screen viewer. A locked note that is not open shows none.
- **Settings → Features**, under Home and menu, after Mood tracker: **Stickers**, off by default, "A sticker button
  where you write a note or quick note: add stickers from the sticker packs on this device. Turning it off hides
  the button; stickers already on your notes stay." While it is on, **Sticker packs** follows under it
  (`StickerPacksSetting`): "A sticker pack is a ZIP file of pictures. Download one in your browser, or make your
  own, then add it here." with **Add pack…** (a secondary `Button`, opening the system picker for `.zip` files,
  several at once), then the packs by name, each with its first sticker, its name and "{n} stickers · by {author} ·
  {licence}" where the pack says, and **Remove**. With none: "No sticker packs yet."
- **Adding** says "Added “{name}”: {n} stickers." or, for a pack already here, "Updated “{name}”: {n} stickers."; a
  file that is not a pack is an error toast with its name and the reason ([04](04-domain-rules.md#sticker-packs)).
- **Removing** asks first: "Remove “{name}”?", "Its stickers leave the sticker picker. Stickers already on your notes
  stay where they are.", **Remove**; then "Removed “{name}”."
- There is no store and no download button: the app never goes online (D3).

## Todo, Quick notes, Habits, Tags, Archive, Trash, Calendar

Port unchanged, apart from **Export to PDF…** and **Lock** in a list's menu and the locked cards ([Export to PDF](#export-to-pdf), [Locked notes](#locked-notes)): `pages/TodoPage.tsx` + `components/TodoCard.tsx`; `pages/QuickNotesPage.tsx`; `pages/HabitsPage.tsx` +
`components/HabitRow.tsx`, `HabitChart.tsx`, `HabitCalendar.tsx`; `pages/TagsPage.tsx`; `ArchivePage` in `HomePage.tsx`;
`pages/TrashPage.tsx`; `components/Calendar.tsx`.

The one copy change: the Trash page intro ends "…Restore one to put it back where it was." Drop the sentence about
storage.

## Settings

`pages/SettingsPage.tsx`. Same layout (the list, and each section with its own address). Sections, in order:

| Id | Title | Summary (under the title) | Contents |
|---|---|---|---|
| `account` | **Profile** | "Your name and how much you store." | Profile, Storage, Erase all data |
| `appearance` | Appearance | "Light or dark, the accent colour, a background and the first day of the week." (the web app's, with "a background" added) | `AppearanceSection`, plus **Background** between Accent colour and Week starts on ([06](06-design-system.md#backgrounds)): a grid of radio choices, each a small picture of the background over its name — "None" (an empty swatch with a line across it), "Halloween", "Christmas", "New Year", "Valentine's Day", "Easter", "Diwali", "Vesak", "Eid", "Lunar New Year" — marked like the Theme choices when chosen. Hint: "A festive pattern behind your notes. It follows the light or dark theme. Choose None for no background." While a background is chosen, a secondary button **Remove background** under the hint does the same as choosing None: the owner did not find an empty first tile called "Plain" to be the way back (2026-10-08). |
| `menu` | Side menu | unchanged | `MenuSection` |
| `writing` | Writing | unchanged | `WritingSection`, `EditingSection`. Editing gains one switch of this app's own, last (1.6.0, 2026-10-13, at the owner's request; [04](04-domain-rules.md#markdown-rendering)): **Keep line breaks**, **off by default** ("Show every line of a note on its own line, as you typed it. Off, lines typed one under another are joined into one paragraph, as Markdown does, and a blank line starts a new one.") |
| `features` | Features | unchanged | `FeaturesSection` as in web 1.15.0, **without Link previews**; with **Mood tracker** of this app's own after Labels ([Moods](#moods)) and **Stickers** after it, with the sticker packs under it while it is on ([Stickers](#stickers)); "Shrink photos before uploading" becomes **Shrink photos before adding** ([08](08-help-guide.md#11-pictures-video-and-files-pictures)), with the **Photo size** choice under it while it is on; the **Daily-note template** line under Daily notes while they are on, without the web app's "could not be loaded" case, and reading "The template note was deleted, is locked or is in the trash, so new daily notes start empty." where the web app has no "is locked"; **Help in the menu**, whose description ends "the guide is then opened from the foot of Settings." where the web app says "the guide stays at /help."; and, this app's own, **Export to PDF** as the last switch of Notes and files, off by default ([Export to PDF](#export-to-pdf)) |
| `labels` | Labels | unchanged | `LabelSettings`. The note about end-to-end encryption is gone. The line under the list ends "Exports keep each note's labels, and restoring one brings them back.", as in the web app since 1.9.0 (2026-10-08; before that, "Exports do not include labels yet."). Since web 1.16.0 each label has an eye button that hides its notes from Home and Quick notes, with "Notes hidden" under its name while it does, and the line under the list starts with the sentence about the eye. |
| `data` | Backup & data | "Export, restore, the trash and starting over." | Backup & restore, Trash, Delete all notes and files |
| `security` | Privacy & security | "App lock, locked notes, and how your notes are protected." | App lock, Locked notes, Data protection |

The foot of the list shows "Falcon Notes {version}". On a second line in smaller text: "Based on Maple Notes 1.16.0"
(1.8.0 until Falcon Notes 1.1.0, 1.15.0 in 1.2.0). With **Help in the menu** off, a **Help** link to `/help` stands above them, in the
accent colour: the web app's guide keeps an address that can be typed, and an app has none.

### Settings → Profile

**Profile** (`Section`):

```text
Display name   {name}  Change
On this device since   {absolute date}
```

"Change" edits inline as `DisplayNameRow` does: at most 64 characters, Save and Cancel. Empty is not allowed here,
since there is no username to fall back on: "Enter a name." Toast: "Display name changed."

**Storage** (`Section`, description "Notes and files you keep, including archived ones and the trash."): `StorageRow`
without the limit parts.

**Erase all data** (`Section`, description "Delete everything Falcon Notes keeps on this device and start again: notes,
files, labels, settings and the app lock."): a danger button **Erase all data…**, then a dialog:

> **Erase all data?**
> Every note, todo list, quick note, habit, label, file and setting on this device is deleted, and its encryption key
> is destroyed. This cannot be undone.
> Export your notes first if you might want them back.
>
> Type ERASE to confirm: [ ]
>
> [Cancel] [Erase everything]

The danger button stays disabled until the box holds `ERASE`. With the app lock on, the PIN or biometrics are asked
first. Afterwards: the Welcome screen.

### Settings → Backup & data

**Backup & restore**: `ExportSection` + `RestorePanel`, with these changes:

- The intro: "Save your notes as a ZIP archive with attachments in an `attachments` folder linked from each note. Todo
  lists, quick notes and habits go in `todo`, `quick-notes` and `habits` folders. **Exports are not encrypted**, so
  keep them somewhere safe. The archive is the same as the Maple Notes web app's, so you can restore it there too."
- Under the intro: "Last export: {relative time}" or "You have not exported yet." (from `Settings['lastExportAt']`).
- The button reads **Export ZIP…**. Progress text "Preparing your export…", then "Reading your notes… {n}". Then the
  system save dialog. Done: "Exported {n} notes and {m} files." (toast). Cancelled in the dialog: nothing.
- "Dates use your time zone ({IANA zone})." is unchanged.
- The restore intro: "Bring notes back from an export (`.zip`, any format) made by Falcon Notes, the Maple Notes web app
  or a Maple Notes server, or add `.md`, `.txt` and `.json` files. Notes keep their dates, pins, archive state, labels
  and files. Notes you already have are skipped, so restoring the same export twice is safe." (Until 1.2.0 it ended
  "Labels are not part of exports.")
- **Choose files…** opens the native picker.
- **While any note is locked** ([Locked notes](#locked-notes)), **Export ZIP…** first opens the PIN check
  (`VerifyPinDialog`), titled "Export your notes": "The export holds your locked notes too, and it is not encrypted.
  Enter your PIN to continue." Cancelling it exports nothing.

**Automatic backups** (`Section`, new, between Backup & restore and Trash; only on a platform with `IBackupFolders`,
[12](12-platforms.md)). The rules are in [05](05-backup-compatibility.md#automatic-backups). Description: "Save a
backup to a folder you choose, without having to remember. Falcon Notes checks when it opens and every hour while it
is open; it cannot back up while it is closed. **These backups are not encrypted**, so choose a folder that only you
can open."

- `PreferenceSwitch` **Back up automatically** ("Saves a ZIP with all your notes, archived ones too, and their files,
  as Markdown. Turning it on asks for the folder."). Turning it on opens the system's folder picker; cancelling it
  leaves the switch off. With a folder chosen, the first backup is made at once, which shows that the folder works.
  A folder the system will not lend for later: "That folder cannot be used. Choose another."
- While it is on:
  - **Folder**, with the folder's name and a secondary button **Change…** (the picker again, then a backup at once).
  - **How often**: a select with "Every day", "Every week" (the default), "Every month".
  - **Keep**: a select with "The newest 3" (the default), "The newest 5", "The newest 10". Hint "Older automatic
    backups in the folder are deleted. Other files in it are never touched."
  - After a failure: "The last automatic backup failed. {reason}" (`ErrorMessage`), until one works.
  - A secondary button **Back up now**, busy while a backup is written, and beside it "Last automatic backup:
    {relative time}", "No automatic backup yet." or "Backing up…". Done by hand: the toast "Exported {n} notes and {m}
    files."
- "Last export: …" in Backup & restore counts automatic backups too.
- While any note is locked, turning the switch on and **Change…** first open the PIN check, titled "Choose the backup
  folder": "Automatic backups hold your locked notes too, and they are not encrypted. Enter your PIN to continue."
  **Back up now** and the backups made by themselves ask for nothing.

**Trash** (`TrashSection`) and **Delete all notes and files** (`DeleteContentSection`) are unchanged, except the second
one's confirmation:

> **Delete all your notes and files?**
> Every note, todo list, quick note, daily note, habit, tag, label and file is deleted. This cannot be undone.
> Export your notes first if you might want them back.
>
> [Cancel] [Delete everything]

With the app lock on, the PIN or biometrics are asked first. Toast: "Deleted {n} notes and {m} files."

### Settings → Privacy & security

**App lock** (`Section`, description "Ask for a PIN, or your fingerprint, face, Windows Hello or Touch ID, before
showing your notes. The lock keeps people out of the app; your notes are encrypted either way."):

- `PreferenceSwitch` **Lock Falcon Notes** ("Ask for your PIN when Falcon Notes opens or comes back after a while.").
  Turning it on opens **Set a PIN**: "Choose a PIN of 4 to 8 digits. If you forget it, the only way back in is to
  erase the app's data and restore a backup." Two numeric fields, "PIN" and "Repeat PIN"; errors "Use 4 to 8 digits."
  and "The PINs do not match." Turning it off asks for the PIN first. **While a note is locked**, the PIN is in use
  already ([03](03-data-storage-and-security.md#locked-notes)), so turning the lock on does not ask for a new one:
  it opens the PIN check, titled "Turn the app lock on": "Your locked notes already use a PIN, and the app lock uses
  the same one. Enter it to continue."
- `PreferenceSwitch` **Unlock with {fingerprint or face | Windows Hello | Touch ID}** ("You can always use your PIN
  instead."). Only shown while the lock is on and biometrics are available. Turning it on runs one biometric check
  first.
- **Lock after**: a select with "Immediately", "After 1 minute", "After 5 minutes", "After 15 minutes", "After 1 hour".
  Hint "How long Falcon Notes may stay in the background before it locks."
- **Change PIN…** (secondary button): current PIN, then Set a PIN. Shown while the lock is on, and also while it is
  off and a note is locked, under the line "Your locked notes use a PIN. The app lock, when you turn it on, uses the
  same one."

**Locked notes** (`Section`, new in 1.4.0, between App lock and Data protection; `NoteLockSection.razor`; description
"Hide single notes behind your PIN. A locked note shows a lock and nothing of what is in it. Unlock it to read it,
and it locks again by itself when you leave the page or the app."):

- `PreferenceSwitch` **Note lock**, **off by default** ("Adds Lock to the ⋯ menu of notes, quick notes and todo
  lists. The first lock asks you to choose a PIN, the same one the app lock uses. Turning this off takes Lock away
  again; notes already locked stay locked and can still be unlocked."). It asks for no PIN either way: turning it on
  locks nothing, and turning it off shows nothing ([Locked notes](#locked-notes)).
- Android only, under the switch: "While the lock is on, Falcon Notes hides its window from screenshots and the app
  switcher."

**Data protection** (`Section`):

> Your notes, labels and files are stored encrypted on this device (AEGIS-256 for the database, AES-256-GCM for files).
> The key is kept in {Android Keystore | Windows' protected storage | the macOS Keychain}, never in the files
> themselves, so a copy of the app's files cannot be read elsewhere.
> Exports are not encrypted. Uninstalling Falcon Notes deletes your notes from this device; export them first.
>
> Key fingerprint: `{8 hex}`

## Welcome

New, shown on first start (no profile). Use `AuthLayout` (`components/AuthLayout.tsx`): mark, heading, intro, card.

> **Welcome to Falcon Notes**
> Quick notes, todo lists and habits, kept on this device and encrypted.
>
> | What should we call you? (optional) [            ] |
> | [Start writing] |
> | ───────────── or ───────────── |
> | [Restore from a backup…] |
>
> Your notes never leave this device unless you export them.

- **Start writing** creates the profile (display name = what was typed, or "Me") with default preferences, then opens
  Home.
- **Restore from a backup…** creates the profile the same way, then runs the restore flow
  ([05](05-backup-compatibility.md#restore)) on a full-screen card: the reading summary, Restore, progress and the
  result. Then Home.

## Lock

New, shown at start and after the lock delay when the app lock is on. `AuthLayout`:

> **Falcon Notes is locked**
> Enter your PIN to open your notes.
>
> | PIN [      ] |
> | [Unlock] |
> | [{Fingerprint icon} Use {fingerprint or face | Windows Hello | Touch ID}] (when on) |
>
> Forgot your PIN?

- The biometric prompt opens by itself when the screen appears (once per lock). When it closes without unlocking
  (cancelled, not recognised too often, the app left), the screen shows no message: the system's prompt has already
  said why, and the PIN field's error is for the PIN.
- A wrong PIN: "That PIN is not right." After 5 wrong PINs: "Too many tries. Try again in {n} seconds." with the field
  disabled and a countdown ([03](03-data-storage-and-security.md#app-lock)).
- **Forgot your PIN?** opens a dialog:

  > **Forgot your PIN?**
  > The PIN cannot be reset: that is what keeps others out. To use Falcon Notes again, erase its data on this device
  > and restore your notes from a backup.
  >
  > Type ERASE to confirm: [ ]
  >
  > [Cancel] [Erase and start over]

## Key lost

New, shown when the database exists but the key is missing or wrong ([03](03-data-storage-and-security.md#lost-key)).
`AuthLayout`:

> **Your notes cannot be opened**
> Falcon Notes keeps your notes encrypted with a key stored by {Android | Windows | macOS}. That key is no longer
> available on this device, so the notes saved here cannot be read. This can happen after reinstalling the app or
> resetting the device's secure storage.
>
> | [Restore from a backup…] |
> | [Start with no notes] |
>
> Nothing has been deleted. The unreadable data is kept aside in case the key comes back.

- Both buttons first move the database, attachments and backups into `unreadable-{yyyyMMdd-HHmmss}/`, then create a
  new key. **Restore from a backup…** then continues like Welcome's restore; **Start with no notes** continues to
  Welcome.

## Help

`pages/HelpPage.tsx` with the guide in [08-help-guide.md](08-help-guide.md), without its
`replaceAll("Maple Notes", appName)` ([08](08-help-guide.md)). In-app links (`/settings/features`, `/trash`,
…) navigate inside the app. The page foot shows the version as in Settings.

## Desktop extras

Shortcuts the web app had keep working: Ctrl/⌘+Enter, Esc, Ctrl/⌘+B/I/K, ← → in the viewer. On macOS, check that ⌘B,
⌘I and ⌘K reach the WebView and are not taken by the menu bar. Remove any default menu items that would take them.
