# 5. Backup compatibility (the contract)

Backups made by this app must restore in the web app and on any Maple Notes server, and backups made there must restore
here. The format is the web app's **export ZIP, manifest version 3**, unchanged: version 2 as in Maple Notes 1.8.0,
plus the labels Maple Notes 1.9.0 added ([Labels in backups](#labels-in-backups)). It is the format of the latest
Maple Notes, 1.16.1 (checked 2026-10-13: its export and restore code and its vectors are as in 1.16.0), with one
line of this app's own for a note that has a mood ([Moods in backups](#moods-in-backups)) and one list for a note
that has stickers ([Stickers in backups](#stickers-in-backups)).

**Never change this format in only one of the two projects.** Any change needs a new manifest version, implemented and
released in the web app first, with its vectors, and then here. The one exception so far is a note's mood, added
here first on the owner's word that Maple Notes gets it next ([Moods in backups](#moods-in-backups)), and which
attachments are stickers ([Stickers in backups](#stickers-in-backups)); each is built so
that it breaks nothing in either app meanwhile.

## Proof: the shared export vectors

`fixtures/export-vectors.json` was written by the server's own test (`ExportVectorTests` in the reference) from a
dataset chosen to be awkward:

- time zones across a New Year and a daylight-saving change in `Europe/Paris`
- slug collisions in the same minute
- Unicode, emoji, `#tags` only, a line separator, a bell character, quotes and backslashes
- attachment names that are reserved on Windows (`con.txt`), have brackets or spaces
- a note with only a file
- archived notes, a todo list, a quick note, a daily note and a habit

- labels (since Maple Notes 1.9.0) whose names need escaping and sort differently by code unit than by culture
  (`Work`, `été ☀️`, `Zebra, "quoted" #1`), and one on no note (`Unused`), which exports leave out

It holds the input notes (`active`, `archived`, in the API's JSON shape), the labels (`labels`, with their IDs and
colours; notes name them in `labelIds`), the attachment bytes (`files`, base64), and
**13 exports**: every format × layout with `includeArchived=true`, plus a dated range without attachments. Each export
lists every archive entry in order, with its exact text (or `base64:` for binary entries), and the manifest's
`exportedAt` replaced by `EXPORTED_AT`.

The app's exporter must reproduce every entry of all 13 exports, in the same order and byte for byte. Its restore
must read every one of them back into the original notes. Both are tests, run on every platform
([11-testing.md](11-testing.md)). `fixtures/demo-backups/*.zip` (35 notes, 8 files, JSON and Markdown) are real archives
for manual and smoke tests. They are manifest version 2 archives, from before labels, as the web app still ships them
(`demo-backup-samples/` in its repository).

The file here is a copy of `src/maple-web/src/export/export-vectors.json` in the Maple Notes repository, last taken
from its `main` at commit `941f656` (2026-10-10, release 1.16.0). Its notes and archives are the same as in release
1.9.0; in 1.16.0 each label of the dataset gained `hideNotes`, which the tests here do not read.

## Export

Port these files from the web app's `src/MapleNotes.Server/Features/Export/` almost verbatim:
`NoteExporter.cs` (`WriteAsync` and its helpers), `NoteFormatter.cs`, `ExportNaming.cs` and `ExportModels.cs`, and the
label lines of the first, second and fourth came with Maple Notes 1.9.0 (commit `20ee12f`). They are
already .NET, so their string, Unicode and JSON behaviour is identical by construction. Replace only the data access
(EF Core → the app's repositories) and the decryption (the server's note cipher → plain text from `NoteBodies`; files →
`DecryptingAttachmentStream`).

### Options (Backup & data screen)

| Option | Values | Default |
|---|---|---|
| Format | `md` (Markdown with front matter), `txt`, `json` | `md` |
| Folders | `month` (By month), `year`, `day`, `flat` (All in one folder) | `month` |
| From / To | optional dates, inclusive, in the time zone | none |
| Include attachments | | on |
| Include archived notes | | off |
| Time zone | the device's IANA zone (see below) | |

The start date must not be after the end date: "The start date must not be after the end date."

### What goes in

- Notes **not in the trash**; archived notes only when asked. All four kinds. Locked notes too
  ([Locked notes in backups](#locked-notes-in-backups)).
- Created within `[From 00:00, To+1 00:00)` in the time zone. When midnight does not exist on a day (a
  daylight-saving gap), the day starts at the first valid half hour (`NoteExporter.StartOfDayUtc`).
- In `(CreatedAt, Id)` ascending order. `Id` is compared as its canonical lower-case text, which is the same order as
  `Guid.CompareTo`.

### The archive

```text
manifest.json                                    always the last entry
2026-09/2026-09-28_1430_buy-maple-syrup.md       timeline notes: {layout folder}/{yyyy-MM-dd_HHmm}_{slug}.{ext}
todo/2026-09/2026-09-28_1500_groceries.md        todo/, quick-notes/, habits/ before the layout folder
attachments/5d1e0a7c_sunset.png                  last 8 hex of the attachment ID ("N" format) + "_" + SafeFileName
```

Per note: first its attachments (ordered by `CreatedAt`), then the note file. The manifest comes at the end. Paths are
made unique ignoring case (`ExportNaming.Unique`: `-2`, `-3`, … before the extension), with `manifest.json` reserved.

| Detail | Rule |
|---|---|
| Times | `TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), zone)`, written `yyyy-MM-dd'T'HH:mm:sszzz` with `CultureInfo.InvariantCulture`. Fractions are truncated. |
| Slug | `ExportNaming.Slug`: the first non-blank line, line markers removed, `#tags` removed unless nothing else is left, letters and digits lower-cased with `Rune.ToLowerInvariant`, separated by `-`, apostrophes dropped, at most 60 characters, `note` when empty |
| File names | `ExportNaming.SafeFileName`: control characters, white space and `\/:*?"<>|` become `-`, runs of `-` collapse, `. -` are trimmed, Windows device names get a `_` prefix, extensions over 20 characters are dropped, at most 100 characters |
| Entry compression | Text: `CompressionLevel.Optimal`. Images (not SVG), video, audio, zip, gzip, 7z and PDF: `NoCompression`. Other files: `Fastest`. Not part of the contract, but keep it. |
| Entry time | Note: its updated time in the zone. Attachment: its created time. Manifest: the export time. |
| Damaged file | If an attachment cannot be opened, leave it out and add `"{path}: the stored file could not be read, so it was left out."` to `problems`. If it breaks part-way, keep the partial entry and add `"{path}: the stored file is damaged, so this copy is incomplete."` |
| Download name | `falcon-notes-{yyyy-MM-dd}.zip`, the export date in the zone. The web app's is `maple-notes-…`; the name is not part of the format. |

### Note formats

Exactly as `NoteFormatter`:

- **Markdown**: front matter with `id`, `kind` (`note`, `todo`, `quick`, `habit`), `daily` (only for daily notes),
  `created`, `updated`, `tags` (JSON strings in `[…]`), `labels` (the same, always written, `[]` when there are none),
  `pinned`, `archived`, `mood` (only for a note that has one: [Moods in backups](#moods-in-backups)), and
  `attachments` (relative paths, JSON strings), then `stickers` (only for a note that has one: [Stickers in
  backups](#stickers-in-backups)). Then a blank line, the text with trailing white space trimmed, and `\n`. Then, with files,
  `\n## Attachments\n\n` and one `- ![name](target)` (images) or `- [name](target)` line each. The name escapes `\ [ ]`;
  the target is percent-encoded per segment with `Uri.EscapeDataString`, keeping `..`.
- **Plain text**: `Created:`, `Updated:` (only if more than one minute after Created), `Kind:` (not for notes),
  `Daily:`, `Tags: #a #b`, `Labels: ["a", "b"]` (only with labels; a JSON array, because names may hold spaces and
  commas), `State: pinned, archived`, `Mood:` (only with a mood), one `Attachment:` line per file, one `Sticker:` line per sticker. Then a blank line, the trimmed
  text and `\n`.
- **JSON**: `{id, kind, dailyDate, createdAt, updatedAt, tags, labels, pinned, archived, content, attachments:
  [{fileName, contentType, sizeBytes, path}]}`, then `\n`. A note with a mood has `mood` between `archived` and
  `content`; a note without one has no such property, not a null one. A note with stickers ends with `stickers`.
- **Labels** are written by name, sorted ordinal like tags. A note's label that no longer exists is left out.
- **JSON settings**: `JsonSerializerDefaults.Web` (camelCase), `WriteIndented = true` (two spaces), and
  `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. Tags are sorted ordinal.
- Line endings are always `\n`, and files are UTF-8 without BOM.

### Manifest

```json
{
  "application": "Maple Notes",
  "manifestVersion": 3,
  "exportedAt": "2026-10-01T14:30:00+02:00",
  "account": "<profile display name>",
  "options": { "format": "md", "layout": "month", "timeZone": "Europe/Paris", "includeArchived": false,
               "includeAttachments": true, "from": null, "to": null },
  "noteCount": 35,
  "attachmentCount": 8,
  "problems": [],
  "labels": [ { "name": "Work", "color": "Blue" } ],
  "notes": [ { "id": "…", "path": "…", "kind": "note", "dailyDate": null, "createdAt": "…", "tags": [], "labels": ["Work"],
               "archived": false, "attachments": ["attachments/…"] } ]
}
```

- `application` is always `"Maple Notes"`: it is the format's name, not this app's (D12). The web app's restore uses a
  manifest only when it says exactly that (`parse.ts`), and every vector expects it.
- `account` is the profile's display name. Restores ignore it; it only labels the file.
- `labels` lists the labels **the exported notes carry**, sorted ordinal by name, each with its colour as the web app
  names it (`Grey`, `Red`, `Orange`, `Amber`, `Green`, `Teal`, `Blue`, `Indigo`, `Purple`, `Pink`). A label on no
  exported note is not in the backup. Label IDs are not written: labels are matched by name.
- **`timeZone` must be an IANA name.** On Windows, `TimeZoneInfo.Local.Id` is a Windows name ("W. Europe Standard
  Time"): convert it with `TimeZoneInfo.TryConvertWindowsIdToIanaId`. On Android and macOS it is already IANA. Fall
  back to `UTC`.
- **`Path.GetExtension`** treats `\` and `:` as separators on Windows. Archive paths never contain them after
  sanitising, but use a separator-independent helper (last `.` after the last `/`; empty for a trailing dot) so the
  output cannot differ between platforms.

### Writing the file

Write the ZIP with `ZipArchive.CreateAsync` into `{CacheDirectory}/export/{random}.zip`, reporting progress ("Reading
your notes… {n}"). Then hand it to `IFileSaver` with the suggested name and delete it whatever happens. On success,
store `Settings['lastExportAt']` and show "Exported {n} notes and {m} files." Exports are not encrypted: the screen says
so, as the web app does.

## Automatic backups

New in this app (2026-10-08, at the owner's request): the web app has none, because its server keeps the notes. Here
the only copy is on the device, and uninstalling the app deletes it. **The format is not touched**: an automatic backup
is the ordinary export, written by the same `NoteExporter`, and restores wherever an export does.

- **Off by default**, because the backups are not encrypted. The user turns them on in Settings → Backup & data and
  chooses a folder in the system's picker ([07](07-screens.md#settings--backup--data)). The app keeps the access to
  that one folder (`IBackupFolders`, [12](12-platforms.md)); no storage permission is involved.
- **What is written**: everything, so that one backup restores it all. Markdown, by month, with files and archived
  notes: the export with `format=md`, `layout=month`, `includeArchived=true`, `includeAttachments=true` and no dates.
  There are no options: a backup that leaves things out is one to be sorry about later.
- **When**: the app cannot run while it is closed, so it checks when it starts and after each hourly maintenance pass
  ([02](02-architecture.md#start-up-sequence)). A backup is due when there has been none in this folder, or when the
  last one was made the chosen number of **calendar days** ago or more, in the device's time zone: every day (1), every
  week (7, the default) or every month (30). Days, not hours, so that someone who opens the app each morning gets a
  backup each morning. A clock set back to before the last backup makes one due.
- **The file**: `falcon-notes-auto-{yyyy-MM-dd_HHmm}.zip`, the time in the device's zone. The name differs from a
  manual export's (`falcon-notes-{yyyy-MM-dd}.zip`) on purpose. A second backup in the same minute replaces the first.
- **Writing**: list the folder first, which finds a folder that is gone before any note is read; write the ZIP into
  `{CacheDirectory}/export/{random}.zip`; copy it into the folder; delete the cache file whatever happens. A copy that
  fails part-way is removed from the folder.
- **Keeping**: after a backup has been written, the automatic backups beyond the newest 3 (the default), 5 or 10 are
  deleted, oldest first by name. **Only files named exactly as above are ever deleted**, and never the one just
  written: an export saved by hand, a renamed copy and every other file in the folder stay. A file that cannot be
  deleted is left for the next time. Nothing is deleted when the backup failed.
- **Afterwards**: `Settings['autoExport']` records the time, and `Settings['lastExportAt']` too, since it is an
  export. Nothing is shown for a backup made in the background; **Back up now** and turning it on show the export's
  toast, "Exported {n} notes and {m} files."
- **Failure**: the reason is kept in `Settings['autoExport']` and shown in Settings and on Home until a backup works
  again: "Falcon Notes can no longer reach the folder. Choose it again." (deleted, moved, its card or drive not
  there, or the access taken back), "There was not enough space.", or "The backup could not be saved." The backup
  stays due, so each hourly pass and each start tries again. Logs get the kind of error and counts, never a name.
- **Locked notes** are in every automatic backup. While a note is locked, turning automatic backups on and
  **Change…** ask for the PIN before the folder is chosen; a backup to a folder already chosen asks for nothing, or
  it could not be automatic.
- **Turning it off, or choosing another folder**, gives up the access to the folder. The backups in it stay: they are
  the user's. Erase all data does the same, and deletes none of them.

## Restore

Port the web app's `src/maple-web/src/import/parse.ts` (reading) and `importer.ts` (restoring) to C#, and
open ZIPs with `System.IO.Compression.ZipArchive`. It reads stored and deflated entries and ZIP64 archives. The web
app's hand-written ZIP reader is not needed.

### Choosing files

`IFilePicker` with several files allowed, of types `.zip, .md, .markdown, .txt, .json`. Copy each ZIP into
`{CacheDirectory}/restore/` first (Android content streams cannot seek), and delete the copy when done. A file's
modified time, when the platform gives one, stands in for missing dates; otherwise use now. Any other type:
"{name}: choose .zip, .md, .txt or .json files."

### Reading

Exactly as `parse.ts`:

- **An archive with a Maple Notes manifest** (`application == "Maple Notes"` and a `notes` array, as this app writes
  too): exactly the notes it lists, in its order, each with its `id`, `kind`, `createdAt`, `archived`, `dailyDate` and
  `labels` from the manifest, taking precedence where `toItem` says (the manifest's labels count only when the note
  file names none). The manifest's `labels` give the colours, kept by name in lower case; a colour the app does not
  know is ignored. A listed path that is missing: "{file}: {path} is listed but
  missing." An unreadable manifest: "{file}: manifest.json could not be read; its notes are read without it."
- **Any other archive**: every `.md`, `.markdown`, `.txt` and `.json` entry outside `attachments/` and `__MACOSX/`.
- **A note file**: strip a UTF-8 BOM and normalise `\r\n` to `\n`. Then:
  - `.md` / `.markdown`: front matter starting `---\n` with a `created` field.
  - `.txt`: header lines starting with `Created: `.
  - `.json`: an object with a string `content`.
  
  Attachment names come from the Markdown "Attachments" section, if it has one line per path, or from the JSON. Without
  a recognised header, the whole text is the note, dated by the file's modified time. A `.json` file that is not a note
  is an error: "This JSON file is not a Falcon Notes or Maple Notes note."
- **Labels**: `labels:` (Markdown) and `Labels:` (plain text) are read as a JSON array, and `labels` in JSON as an
  array; only its strings count, and anything else gives no labels.
- **Fields**: an `id` counts only when it is a UUID. Kinds are matched case-insensitively (anything else is `note`). A
  daily date counts only as `yyyy-MM-dd`. Dates parse as ISO 8601 with their offset.
- **Attachments** resolve relative to the note's folder (`..` pops a folder). Their name is the recorded file name,
  else the entry's name without its 8-hex prefix. Their type is the recorded type, else a guess from the extension. A
  path not in the archive goes to `missing`.
- Entries over 2 GiB are refused: "\"{name}\" is too large to restore." In an archive, a note file over 4 MB is
  refused before it is read ("This file is too large to be a Falcon Notes or Maple Notes note or manifest."), and a
  manifest over 64 MB is treated as unreadable (as the web app since 1.11).

Then show "{file or n files}: {n} note(s) with {m} attached file(s)." and any problems, with **Restore {n} notes** and
**Cancel**.

### Restoring

As `importer.ts` and the server's `NoteService.ImportAsync`, in transactions of up to 200 notes:

1. Skip notes whose `id` already exists (compared as GUIDs). Restoring the same backup twice changes nothing.
2. Find or create the labels the other notes name (`resolveLabels`): a label is matched to one already here by name,
   ignoring case and surrounding spaces; otherwise it is created, in the colour the manifest recorded or else the next
   colour in turn. One that cannot be created (a name over 40 characters, or past the limit of 100) is reported as
   "Label “{name}”: {reason} Notes are restored without it.", and the notes arrive without it. The labels of notes that
   are skipped are not created.
3. Store the note's files (sanitised name, type as above), then the note:
   - `Id`: the original if present and free, else a new UUID v7.
   - Content: validated as on creation. Too long, or blank without files, fails that note with the same message.
   - `CreatedAt`: must not be more than 24 h in the future ("A note cannot have been created in the future.").
   - `UpdatedAt`: clamped to `[CreatedAt, now]`.
   - Kind and pinned as given. Archived → `ArchivedAt = UpdatedAt`.
   - Daily date only for `Note` kind, and only if no note has that date already; otherwise dropped.
   - Tags parsed from the text. Its labels as resolved above, at most 20.
4. A note with missing files is restored without them and reported: "Restored without {names}, which the archive does
   not contain."
5. A note that fails is reported with its reason, its stored files are deleted, and the rest carry on. Running out of
   disk space stops the restore: "Not enough space on this device." The user can run it again, and notes already
   restored are skipped.
6. Progress: "Restoring… {done} of {total}" with a bar. At the end: "Restored {n} note(s) and {m} file(s)." plus
   "Added {k} label(s)." when labels were created, and "{k} note(s) were already here." Problems are listed, the first 50 shown, then "…and {k} more."

## Moving between devices

Exporting on one device and restoring on another is the only way to move notes. Labels travel with the notes that
carry them, and so do each note's mood and stickers. Labels on no note, sticker packs, preferences, the profile, the app lock and the locks on single notes stay behind. Help says so
([08](08-help-guide.md)).

## Labels in backups

Since Falcon Notes 1.2.0 (2026-10-08), as Maple Notes 1.9.0 defined them: **manifest version 3**. Every note file
lists its labels by name, the manifest lists them again per note and gives the colours of the labels in use, and a
restore matches a label by name or creates it. Nothing here was designed in this project: the exporter's lines are the
server's, the restore is `parse.ts` and `importer.ts`, and the vectors are the web repository's.

Both ways, and across versions:

| Backup made by | Restored in | Labels |
|---|---|---|
| Falcon Notes 1.2.0 or later | Maple Notes 1.9.0 or later | Kept |
| Maple Notes 1.9.0 or later | Falcon Notes 1.2.0 or later | Kept |
| Either, version 3 | Maple Notes up to 1.8.1, Falcon Notes up to 1.1.0 | Left out; the notes restore as before, because version 2 readers read only the fields they know |
| A version 2 backup | Anything | There are none in it |

An earlier sketch in this document had label IDs in the manifest. The web app chose names instead, so that is the
format.

Whether a label hides its notes (Maple Notes 1.16.0) is not in a backup, in either app: the manifest gives a label's
name and colour only. A restored label does not hide its notes until its eye is turned on again in Settings → Labels.
Carrying it needs a new manifest version in the web app first.

## Locked notes in backups

Locked notes (Falcon Notes 1.4.0, this app's own) do not change the format. A locked note is exported like any other
note, with its text and files, because a backup that leaves notes out is one to be sorry about later, and because the
lock is a privacy screen inside the app and not encryption ([03](03-data-storage-and-security.md#locked-notes)). The
archive says nothing of the lock: Maple Notes' format has no place for it, its restore would ignore one, and adding
it here would change the format in one project only. So a restored note is not locked, in either app, and is locked
again by hand.

Since an export holds a locked note's text, the screen asks for the PIN, or the fingerprint or face where the app
lock allows it, before it writes one, while any note is locked ([07](07-screens.md#settings--backup--data)).
`ExportService` and `NoteExporter` are unchanged, and the conformance vectors with them.

## Moods in backups

New in Falcon Notes 1.7.0 (2026-10-13). A note's mood ([04](04-domain-rules.md#moods)) is written into its file in
every format, and read back on restore. **This is the one part of the format defined here first**: Maple Notes
1.16.1 has no moods. Asked whether backups should carry them, the owner answered that the same option is being
added to Maple Notes next, so that it will be compatible. Until Maple Notes has it, this section is the definition
it is to follow; if it settles on something else, this app follows it, as it does for everything else in the
format, and keeps reading what 1.7.0 wrote.

What is written, and only for a note that has a mood:

| Format | Line | Where |
|---|---|---|
| Markdown | `mood: great` in the front matter | after `archived`, before `attachments` |
| Plain text | `Mood: great` in the header | after `State`, before the `Attachment` lines |
| JSON | `"mood": "great"` | after `archived`, before `content` |

- **The value** is the mood's name in lower case: `awful`, `bad`, `okay`, `good` or `great`. A name and not a
  number or a smiley, as kinds and label colours are names, so each app can draw the mood as it likes.
- **A note without a mood is written exactly as before**: no line, no `null`. So every export of notes without
  moods is byte for byte what the web app writes, and the 13 shared vectors, which hold no mood, pass unchanged;
  `MoodBackupTests` proves that a mood adds its one line and changes nothing else.
- **The manifest is untouched**: version 3, and no `mood` in its `notes`. The note file is the one place a mood
  is read from, in every format, so the manifest needs no copy.
- **Reading**: `mood` (Markdown), `Mood` (plain text) and `mood` (JSON, a string), matched ignoring case. Anything
  else, such as a mood a later version adds, is no mood, and the note is restored without one and without a
  problem being reported. A todo list or a habit that names a mood arrives without it.
- **Older readers** pass the line over: Maple Notes up to 1.16.1 and Falcon Notes up to 1.6.0 read the fields they
  know from the front matter, the header and the JSON, and ignore the rest (`parse.ts`, `RestoreReader`). The note
  restores as before, without its mood.

| Backup made by | Restored in | Moods |
|---|---|---|
| Falcon Notes 1.7.0 or later | Falcon Notes 1.7.0 or later | Kept |
| Falcon Notes 1.7.0 or later | Maple Notes up to 1.16.1, Falcon Notes up to 1.6.0 | Left out; the notes restore as before |
| Falcon Notes 1.7.0 or later | A Maple Notes that reads moods | Kept, if it reads these lines; that is what it is asked to do |
| Anything earlier | Anything | There are none in it |

Whether the mood tracker is turned on is a preference and, like every preference, not in a backup
([Settings in backups](#settings-in-backups)). A restored note has its mood either way, and shows it once the
switch is on.


## Stickers in backups

New in Falcon Notes 1.7.0 (2026-10-14). A sticker ([04](04-domain-rules.md#stickers)) is one of its note's files,
so it is written to the archive and listed among the note's attachments like any other picture. **One more list
says which of those files are stickers**, written only for a note that has one:

| Format | Lines | Where |
|---|---|---|
| Markdown | `stickers:` and one `  - "attachments/…"` item per sticker, in the front matter | after the `attachments` list |
| Plain text | one `Sticker: attachments/…` line per sticker, in the header | after the `Attachment` lines |
| JSON | `"stickers": ["attachments/…"]` | last, after `attachments` |

- **Each item is the path already in `attachments`**, written the same way, so the sticker's file is in the
  archive once. The pack it came from is not written: the picture is the note's own.
- **A note without stickers is written exactly as before**: no list, no empty one. So the 13 shared vectors, which
  hold none, pass unchanged; `StickerBackupTests` proves the lines and that nothing else changes.
- **The manifest is untouched**: version 3. The note file is the one place stickers are read from.
- **Reading**: a path in the list that is also among the note's attachments makes that file a sticker, if it is a
  picture. Any other path is passed over without a problem reported, and so is a list in a backup that has no
  attachments section.
- **Maple Notes reads the sticker as an ordinary picture.** Checked against its `parse.ts` on `main` (1.16.1,
  2026-10-14): in Markdown, any new front-matter key ends the `attachments` list, so the `stickers` items are not
  read as more files; in plain text, a `Sticker:` line is an unknown field; in JSON, `stickers` is an unknown
  property. The picture shows in the note's grid of pictures there, at its full size. The same holds for Falcon
  Notes up to 1.6.0.

| Backup made by | Restored in | Stickers |
|---|---|---|
| Falcon Notes 1.7.0 or later | Falcon Notes 1.7.0 or later | Kept, as stickers |
| Falcon Notes 1.7.0 or later | Maple Notes up to 1.16.1, Falcon Notes up to 1.6.0 | Kept, as ordinary pictures |
| Anything earlier | Anything | There are none in it |

Sticker packs are not in backups ([04](04-domain-rules.md#sticker-packs)), and whether stickers are turned on is a
preference, which is not either ([Settings in backups](#settings-in-backups)).

## Settings in backups

Not built, and not possible in this project alone. Maple Notes has no place for preferences in its export, in any
version up to 1.16.0, and its restore would ignore one. Adding it here would change the format in one project only,
which this document forbids. It needs a new manifest version in the web app first, with its vectors, and then the
port here. Until then preferences, the profile's name and the app lock are set up again on a new device.
