# 7. Screens

For each screen: where it comes from in `reference/maple-notes-1.8.0/src/maple-web/src/`, and only what differs. Anything
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
- The search box, menu items (with the Tags count), labels list and calendar are unchanged.

## Home and filters

`pages/HomePage.tsx`, `components/Composer.tsx`, `components/NoteList.tsx`, `components/NoteCard.tsx`. Unchanged apart from
these:

- **Note menu**: unchanged (Pin, Edit, Labels…, Move to Home / quick notes, Use as daily-note template / Stop using as
  daily template, Copy text, Archive/Restore, Move to trash / Delete…). Copy text uses `IClipboard`: "Copied to the
  clipboard." / "Copying is not allowed here." The template item is the web app's since 1.15
  ([04](04-domain-rules.md#daily-notes)).
- **Labels…** opens its dialog with the focus on the dialog itself, not on the find-or-create field, so a phone's
  keyboard stays down until the field is tapped (the web app since 1.8.1). The dialog has `tabindex="-1"` and
  `data-focus-self`, which `dialogs.js` acts on after `showModal()`.
- **Link previews** are gone.
- **Filtered views** (`?tag`, `?q`, `?day`, `?label`) and their empty states are unchanged.

## Composer

`components/Composer.tsx`, `FormatToolbar.tsx`, `TagSuggestions.tsx`. Changes:

- The paperclip ("Attach files") opens the native file picker (several files). Paste and drag-and-drop work on
  Windows and macOS. A file's tile shows a progress bar while it is encrypted and stored. On failure, the red tile's
  tooltip and label read "Could not add this file."
- On phones, hide the "Ctrl/⌘ + Enter to post" hint, as at `sm` in the reference.

## Attachments

`components/AttachmentGallery.tsx`, `components/ImageViewer.tsx`. Changes:

- **File chips**: choosing one opens a menu with **Open** (`IFileOpener`, from a decrypted copy in the cache) and **Save a
  copy…** (`IFileSaver`).
- **Image viewer**: the download button becomes **Save a copy** (`Download` icon, label "Save a copy of {name}"). On
  Android, add **Share** (`Share` icon), which hands a decrypted copy to the system share sheet.
- **Videos over 50 MB**, if the media mechanism can only use blob URLs ([02](02-architecture.md#serving-attachments-to-the-webview)):
  show the poster tile with a play button that opens the video in the system player instead.

## Todo, Quick notes, Habits, Tags, Archive, Trash, Calendar

Port unchanged: `pages/TodoPage.tsx` + `components/TodoCard.tsx`; `pages/QuickNotesPage.tsx`; `pages/HabitsPage.tsx` +
`components/HabitRow.tsx`, `HabitChart.tsx`, `HabitCalendar.tsx`; `pages/TagsPage.tsx`; `ArchivePage` in `HomePage.tsx`;
`pages/TrashPage.tsx`; `components/Calendar.tsx`.

The one copy change: the Trash page intro ends "…Restore one to put it back where it was." Drop the sentence about
storage.

## Settings

`pages/SettingsPage.tsx`. Same layout (the list, and each section with its own address). Sections, in order:

| Id | Title | Summary (under the title) | Contents |
|---|---|---|---|
| `account` | **Profile** | "Your name and how much you store." | Profile, Storage, Erase all data |
| `appearance` | Appearance | unchanged | `AppearanceSection` |
| `menu` | Side menu | unchanged | `MenuSection` |
| `writing` | Writing | unchanged | `WritingSection`, `EditingSection` |
| `features` | Features | unchanged | `FeaturesSection` as in web 1.15.0, **without Link previews**; "Shrink photos before uploading" becomes **Shrink photos before adding** ([08](08-help-guide.md#11-pictures-video-and-files-pictures)), with the **Photo size** choice under it while it is on; the **Daily-note template** line under Daily notes while they are on, without the web app's "could not be loaded" case; **Help in the menu**, whose description ends "the guide is then opened from the foot of Settings." where the web app says "the guide stays at /help." |
| `labels` | Labels | unchanged | `LabelSettings`. The note about end-to-end encryption is gone. The line under the list ends "Exports keep each note's labels, and restoring one brings them back.", as in the web app since 1.9.0 (2026-10-08; before that, "Exports do not include labels yet."). |
| `data` | Backup & data | "Export, restore, the trash and starting over." | Backup & restore, Trash, Delete all notes and files |
| `security` | Privacy & security | "App lock, and how your notes are protected." | App lock, Data protection |

The foot of the list shows "Falcon Notes {version}". On a second line in smaller text: "Based on Maple Notes 1.15.0"
(1.8.0 until Falcon Notes 1.1.0). With **Help in the menu** off, a **Help** link to `/help` stands above them, in the
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
  and "The PINs do not match." Turning it off asks for the PIN first.
- `PreferenceSwitch` **Unlock with {fingerprint or face | Windows Hello | Touch ID}** ("You can always use your PIN
  instead."). Only shown while the lock is on and biometrics are available. Turning it on runs one biometric check
  first.
- **Lock after**: a select with "Immediately", "After 1 minute", "After 5 minutes", "After 15 minutes", "After 1 hour".
  Hint "How long Falcon Notes may stay in the background before it locks."
- **Change PIN…** (secondary button): current PIN, then Set a PIN.
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
