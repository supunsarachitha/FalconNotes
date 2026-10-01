# 5. Backup compatibility (the contract)

Backups made by this app must restore in the web app and on any Maple Notes server, and backups made there must restore
here. The format is the web app's **export ZIP, manifest version 2**, unchanged.

**Never change this format in only one of the two projects.** Any change needs a new manifest version, implemented and
released in the web app and here together ([Future: labels in backups](#future-labels-in-backups)).

## Proof: the shared export vectors

`fixtures/export-vectors.json` was written by the server's own test (`ExportVectorTests` in the reference) from a
dataset chosen to be awkward:

- time zones across a New Year and a daylight-saving change in `Europe/Paris`
- slug collisions in the same minute
- Unicode, emoji, `#tags` only, a line separator, a bell character, quotes and backslashes
- attachment names that are reserved on Windows (`con.txt`), have brackets or spaces
- a note with only a file
- archived notes, a todo list, a quick note, a daily note and a habit

It holds the input notes (`active`, `archived`, in the API's JSON shape), the attachment bytes (`files`, base64), and
**13 exports**: every format × layout with `includeArchived=true`, plus a dated range without attachments. Each export
lists every archive entry in order, with its exact text (or `base64:` for binary entries), and the manifest's
`exportedAt` replaced by `EXPORTED_AT`.

The app's exporter must reproduce every entry of all 13 exports, in the same order and byte for byte. Its restore
must read every one of them back into the original notes. Both are tests, run on every platform
([11-testing.md](11-testing.md)). `fixtures/demo-backups/*.zip` (35 notes, 8 files, JSON and Markdown) are real archives
for manual and smoke tests.

## Export

Port these files from `reference/maple-notes-1.8.0/src/MapleNotes.Server/Features/Export/` almost verbatim:
`NoteExporter.cs` (`WriteAsync` and its helpers), `NoteFormatter.cs`, `ExportNaming.cs` and `ExportModels.cs`. They are
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

- Notes **not in the trash**; archived notes only when asked. All four kinds.
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
  `created`, `updated`, `tags` (JSON strings in `[…]`), `pinned`, `archived`, and `attachments` (relative paths, JSON
  strings). Then a blank line, the text with trailing white space trimmed, and `\n`. Then, with files,
  `\n## Attachments\n\n` and one `- ![name](target)` (images) or `- [name](target)` line each. The name escapes `\ [ ]`;
  the target is percent-encoded per segment with `Uri.EscapeDataString`, keeping `..`.
- **Plain text**: `Created:`, `Updated:` (only if more than one minute after Created), `Kind:` (not for notes),
  `Daily:`, `Tags: #a #b`, `State: pinned, archived`, one `Attachment:` line per file. Then a blank line, the trimmed
  text and `\n`.
- **JSON**: `{id, kind, dailyDate, createdAt, updatedAt, tags, pinned, archived, content, attachments: [{fileName,
  contentType, sizeBytes, path}]}`, then `\n`.
- **JSON settings**: `JsonSerializerDefaults.Web` (camelCase), `WriteIndented = true` (two spaces), and
  `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. Tags are sorted ordinal.
- Line endings are always `\n`, and files are UTF-8 without BOM.

### Manifest

```json
{
  "application": "Maple Notes",
  "manifestVersion": 2,
  "exportedAt": "2026-10-01T14:30:00+02:00",
  "account": "<profile display name>",
  "options": { "format": "md", "layout": "month", "timeZone": "Europe/Paris", "includeArchived": false,
               "includeAttachments": true, "from": null, "to": null },
  "noteCount": 35,
  "attachmentCount": 8,
  "problems": [],
  "notes": [ { "id": "…", "path": "…", "kind": "note", "dailyDate": null, "createdAt": "…", "tags": [], "archived": false,
               "attachments": ["attachments/…"] } ]
}
```

- `application` is always `"Maple Notes"`: it is the format's name, not this app's (D12). The web app's restore uses a
  manifest only when it says exactly that (`parse.ts`), and every vector expects it.
- `account` is the profile's display name. Restores ignore it; it only labels the file.
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

## Restore

Port `reference/maple-notes-1.8.0/src/maple-web/src/import/parse.ts` (reading) and `importer.ts` (restoring) to C#, and
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
  too): exactly the notes it lists, in its order, each with its `id`, `kind`, `createdAt`, `archived` and `dailyDate`
  from the manifest, taking precedence where `toItem` says. A listed path that is missing: "{file}: {path} is listed but
  missing." An unreadable manifest: "{file}: manifest.json could not be read; its notes are read without it."
- **Any other archive**: every `.md`, `.markdown`, `.txt` and `.json` entry outside `attachments/` and `__MACOSX/`.
- **A note file**: strip a UTF-8 BOM and normalise `\r\n` to `\n`. Then:
  - `.md` / `.markdown`: front matter starting `---\n` with a `created` field.
  - `.txt`: header lines starting with `Created: `.
  - `.json`: an object with a string `content`.
  
  Attachment names come from the Markdown "Attachments" section, if it has one line per path, or from the JSON. Without
  a recognised header, the whole text is the note, dated by the file's modified time. A `.json` file that is not a note
  is an error: "This JSON file is not a Falcon Notes or Maple Notes note."
- **Fields**: an `id` counts only when it is a UUID. Kinds are matched case-insensitively (anything else is `note`). A
  daily date counts only as `yyyy-MM-dd`. Dates parse as ISO 8601 with their offset.
- **Attachments** resolve relative to the note's folder (`..` pops a folder). Their name is the recorded file name,
  else the entry's name without its 8-hex prefix. Their type is the recorded type, else a guess from the extension. A
  path not in the archive goes to `missing`.
- Entries over 2 GiB are refused: "\"{name}\" is too large to restore."

Then show "{file or n files}: {n} note(s) with {m} attached file(s)." and any problems, with **Restore {n} notes** and
**Cancel**.

### Restoring

As `importer.ts` and the server's `NoteService.ImportAsync`, in transactions of up to 200 notes:

1. Skip notes whose `id` already exists (compared as GUIDs). Restoring the same backup twice changes nothing.
2. Store the note's files (sanitised name, type as above), then the note:
   - `Id`: the original if present and free, else a new UUID v7.
   - Content: validated as on creation. Too long, or blank without files, fails that note with the same message.
   - `CreatedAt`: must not be more than 24 h in the future ("A note cannot have been created in the future.").
   - `UpdatedAt`: clamped to `[CreatedAt, now]`.
   - Kind and pinned as given. Archived → `ArchivedAt = UpdatedAt`.
   - Daily date only for `Note` kind, and only if no note has that date already; otherwise dropped.
   - Tags parsed from the text. No labels: backups do not carry them.
3. A note with missing files is restored without them and reported: "Restored without {names}, which the archive does
   not contain."
4. A note that fails is reported with its reason, its stored files are deleted, and the rest carry on. Running out of
   disk space stops the restore: "Not enough space on this device." The user can run it again, and notes already
   restored are skipped.
5. Progress: "Restoring… {done} of {total}" with a bar. At the end: "Restored {n} note(s) and {m} file(s)." plus "{k}
   note(s) were already here." Problems are listed, the first 50 shown, then "…and {k} more."

## Moving between devices

Exporting on one device and restoring on another is the only way to move notes. Labels, preferences, the profile and
the app lock stay behind. Help says so ([08](08-help-guide.md)).

## Future: labels in backups

Not in 1.0. When wanted, define **manifest version 3** with the web app maintainers (here, the same person). It adds an
optional `labels` array (`{id, name, color}`) and a `labels` list of label IDs on each manifest note. Readers of
version 2 ignore unknown fields: `parse.ts` reads only the fields it knows, and the server's restore never sees the
manifest. So a version 3 archive would still restore without labels in today's web app. Implement it in both
projects, extend the shared vectors in the web repository, and copy them here.
