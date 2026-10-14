# 11. Testing

The web app is well tested: about 470 server tests and 295 web tests. Its tests are the best description of the edge
cases, so **port them with the code**. A rule is not done until its tests from the reference pass here.

## Projects

| Project | Framework | Runs on | Covers |
|---|---|---|---|
| `tests/FalconNotes.Core.Tests` | xUnit v3 | CI on Linux, Windows, macOS; also on device (below) | Text rules, Markdown rendering, crypto, storage with real encrypted SQLite files, services, maintenance, export and restore conformance, performance budgets |
| `tests/FalconNotes.UI.Tests` | xUnit v3 + bUnit | CI | Component behaviour: what is shown, what a click or key does, ARIA, toasts. JS interop is mocked with bUnit's `JSInterop` in strict mode, with each expected call set up. |
| On-device run | A small MAUI test head (`tests/FalconNotes.DeviceTests`) that runs Core's storage, crypto and conformance tests and logs the results, or an on-device xUnit runner (check its licence first) | Android emulator and phone, Windows, macOS, before each release | Native SQLite3MC, secure storage, ICU and time zone data, file system |

Use temporary folders per test (`TempDirectory` in the reference's test support) and a fixed clock (`TimeProvider`).
Fake every platform interface (`ISecretStore`, `IFileSaver`, …) in tests.

Both projects run their tests one at a time (`AssemblyInfo.cs`). `Microsoft.Data.Sqlite` keeps its connection pools
for the whole process, and `SqliteConnection.ClearAllPools()`, which the test support and the app's erase, key-lost
and upgrade steps call to let go of the database files, can close a connection that another test is opening. Run in
parallel, about one run in ten failed at random with `ObjectDisposedException: SQLitePCL.sqlite3`.

In a component test, look at a page that is still loading only from inside `WaitForAssertion`, or through
`cut.Markup`. `cut.Find` and `FindAll` keep the parsed elements until the next render; called from the test's own
thread while renders are still arriving, they can keep the elements of an earlier render, and when no render follows,
every later look sees the old page. This was measured on 2026-10-12 in `CalendarTests`: `cut.Markup` had a day's
"3 notes" while `cut.FindAll` still returned the day without them, in every run of the whole suite and in none of
the class alone. A click made while a list is rendering what it loaded also waits its turn, so wait for what it
opens before reaching for it. Both cost a CI run that day (`HomeTests`, `LockedNotesTests`), and neither showed on a
fast machine, even under load.

A fixed clock gives rows made one after another the same time, and their order then falls back to their IDs. Version 7
IDs made within one millisecond are in random order, so a test that checks the order of a note's files must give them
distinct times: `TestApp.AddFileAsync` moves the clock on one tick, and a restore test sets `Clock.Step`. A machine
fast enough to make two IDs in a millisecond (the Linux and Windows CI machines) otherwise fails now and then.

## Updates keep every note

**A new version must never cost anyone a note.** The app is offline: the copy on the device is the only one, and an
update is installed over it by the store without asking. So every release, and every feature that touches the
database, the attachment files, the keys or start-up, is tested as an update before it ships, not only as a fresh
install (the owner's rule, 2026-10-12).

1. **Migrations only add.** A migration adds a table, a column with a default, or an index. It does not drop, rename
   or rewrite what holds notes, and a released migration is never changed
   ([03](03-data-storage-and-security.md#schema)). Start-up already keeps a copy of the database before it runs one
   ([02](02-architecture.md#start-up-sequence)); that copy is the last resort, not the plan.
2. **The automated proof** is `UpgradeKeepsNotesTests`: a database holding one of everything (a pinned and labelled
   note with a file and a nested tag, a todo list, a quick note, a habit, a daily note, an archived note and one in
   the trash) is taken back to the schema of **each** earlier version, started by the current code as an update
   would start it, and compared **row by row** in every table, and file by file, with what was there before; then
   read back through the services. `SchemaHistory.Undo` holds what takes each migration back. A migration added
   without its undo fails `Every_migration_has_its_undo_so_every_update_is_tested`, so a new one cannot ship
   untested. A change that needs data moved, not just added, also gets a test of its own with rows as the old
   version wrote them (`DatabaseStartupTests` has one per migration).
3. **The check on a device**, before each release ([Manual QA](#manual-qa)): install the **released** version, make
   notes of every kind in it, with a file, a label, a pin, an archived note and one in the trash; if the release
   touches them, turn on the app lock and automatic backups too. Install the new version **over it**
   (`adb install -r`, never an uninstall, and never `pm clear`). Every note, file and label must be there, in the
   same lists, and the settings as they were. Debug builds of both versions update each other; a release-signed
   build only updates a release-signed one ([CLAUDE.md](../CLAUDE.md)). With Debug builds,
   `scripts/update-check/inventory.mjs` writes down every card on every page, the storage counts, the tags and the
   appearance, and `compare.mjs` compares the walk before with the walk after, so nothing rests on a glance. An
   emulator started with `-read-only` forgets the whole test when it closes.
4. **Write down what was done** under the release in [10](10-implementation-plan.md): which version was updated from,
   on what, and what was looked at. "Not checked" is written down too.

Anything that could delete, move or rewrite notes by design (a clean-up, a new purge, a change to the trash) needs
the owner's word first ([CLAUDE.md](../CLAUDE.md), ground rules).

## Porting the reference tests

| Reference tests | Port to | What they protect |
|---|---|---|
| `web/lib/titles.test.ts`, `todo.test.ts`, `habits.test.ts`, `markdownEdit.test.ts`, `tagSuggest.test.ts`, `menu.test.ts`, `format.test.ts`, `shrinkPhoto.test.ts` (~44 cases) | `Core.Tests/Text/*`, `Core.Tests/Attachments/PhotoShrinkerTests.cs` | Every text rule |
| `web/components/Markdown.test.tsx` (7) | `Core.Tests/Markdown/*` | Raw HTML not rendered, tags linked, not in code, task offsets, safe links |
| `server/../Notes/*Tests.cs` (notes API, calendar, daily notes, kinds, import, trash), `Labels/LabelsTests.cs`, `Storage/StorageTests.cs`, `Auth/DeleteContentTests.cs` | `Core.Tests/Notes/*`, `Labels/*` | Lists and cursors, filters, search, the calendar's days, daily notes (one per day, given up in the trash), kind moves (no habits), trash and purge, label limits and counts, storage use, delete all |
| `server/../Crypto/AttachmentCipherTests.cs`, `Infrastructure/AttachmentStoreTests.cs` | `Core.Tests/Crypto/*`, `Attachments/*` | Every tamper case and chunk boundary; storage keys cannot escape the folder |
| `server/../Export/ExportTests.cs` | `Core.Tests/Backup/ExportConformanceTests.cs`, `ExportServiceTests.cs` | Options, ranges, daylight-saving gaps, problems in the manifest, labels by name with their colours (Maple Notes 1.9.0) |
| `web/import/import.test.ts` (5, and the label case of Maple Notes 1.9.0) | `Core.Tests/Backup/RestoreConformanceTests.cs`, `RestoreRunnerTests.cs`, `DemoBackupTests.cs` | Every format and layout restores; single files; archives without a manifest; wrong files; skipping existing notes; labels reused by name, created in their colour, or reported |
| `web/components/*.test.tsx`, `web/pages/*.test.tsx` except encryption, link previews, auth and end-to-end (~130) | `UI.Tests/*` | Composer (13), TodoCard (13), NoteCard (7), AttachmentGallery (7), HabitsPage (8), HabitChart (5), Calendar (5), Labels (5), QuickNotesPage (8), SettingsPage (22, minus the removed sections), TrashPage (5), TagsPage (3), HelpPage (4), AppShell (4), RestorePanel and ExportSection |

New tests the reference does not have:

- `Database`: wrong key → `NOTADB`; the header is unreadable; migrations from each schema version, with the database
  copy kept; a lost key leads to **Key lost** and no deletion.
- App lock: PIN hashing and checking; the tries counter survives a restart; the delay grows; the lock cannot be skipped
  by navigation (bUnit).
- Biometric unlock (bUnit, `BiometricUnlockTests`, with a fake prompt): the prompt opens by itself once per lock; it
  is offered only while it is turned on and the device has biometrics; the switch turns on only after one check
  succeeds; a cancelled prompt leaves the PIN working and shows no error; a check stands in for the PIN where
  Settings asks for it. The real prompt (`AndroidAppLock`) can only be checked on a device or an emulator with a
  fingerprint enrolled.
- Shrinking photos in the composer (bUnit, `ComposerTests`, with a fake codec): a photo is shrunk when the setting is
  on, added as it is when it is off, and added as it is on a platform with no `IImageCodec`. The real codec
  (`AndroidImageCodec`) can only be checked on a device or an emulator ([10](10-implementation-plan.md), Phase 4).
- Brought over with Maple Notes 1.15.0, from its tests: the photo sizes (`PhotoShrinkerTests`, and the composer
  passing the chosen one on); the daily-note template in a note's menu (`NoteCardTests`) and on the Today card
  (`HomeTests`); Help in the menu (`AppShellTests`); the new preferences and their defaults (`SettingsTests`); `//host`
  links and images (`MarkdownRendererTests`); text-reordering characters in file names (`AttachmentServiceTests`).
  New here: `FeaturesSectionTests` (the web app tests these inside its whole Settings page), including the Help link
  at the foot of Settings; and that the Labels dialog asks for the focus itself (`LabelPickerTests`), since moving it
  is `dialogs.js`'s work and only runs in a browser.
- Brought over with Maple Notes 1.16.0, from its tests: a label hiding its notes, with every list the server's case
  names (`LabelServiceTests`); the eye in Settings (`LabelSettingsTests`); the line on the label's page (`HomeTests`).
  New here: Home dropping a note and taking it back as the eye turns (`HomeTests`), the mark in the side menu
  (`LabelLinksTests`), a version 1 database upgrading with nothing hidden and a copy kept (`DatabaseStartupTests`),
  and budgets for the feed and pinned lists with a label hiding its notes.
- Labels in backups: where a note's labels are read from in each format, and what is not a label
  (`RestoreConformanceTests`); at most 20 labels on a restored note and the limit of 100 (`RestoreRunnerTests`); the
  restore flow passes the manifest's colours on and says "Added {n} labels." (bUnit, `WelcomeTests`).
- Automatic backups (`AutoExportServiceTests`, over a fake folder): off until a folder is chosen; the first backup is
  due at once and equals the shared vectors' Markdown-by-month export entry for entry; due by calendar days in the
  device's zone, and when the clock was set back; only the newest automatic backups are kept and no other file in
  the folder is ever deleted; a failed backup deletes nothing, says why and is tried again; turning it off, another
  folder and Erase all data give up the folder and leave its backups; maintenance makes the backup due at start.
  The screens (bUnit, `AutoBackupSectionTests`): the switch asks for the folder and makes the first backup, a
  cancelled picker leaves it off, the choices are saved, a failure shows in Settings and on Home until one works,
  and a platform without `IBackupFolders` shows neither. The real folder (`AndroidBackupFolders`) can only be
  checked on a device or an emulator.
- Festival backgrounds: the preference's default and that a value not offered reads as plain (`SettingsTests`); the
  page is told the background by name, and only when it changes (`AppearanceServiceTests`); Settings offers every
  background by name with its small picture and saves the choice, and every background in the enum has its name, its
  colours in `app.css` and a tile for each theme that reaches for nothing outside itself (bUnit, `BackgroundTests`).
  **Remove background** is covered there too. How they look is for the eye: the script's contact sheet and a device
  ([10](10-implementation-plan.md), Phase 3, has what was looked at and the scrolling measurements).
- The drawer and the sidebar keep clear of the status and navigation bars (`AppShellTests`): by their classes, since
  bUnit has no screen; the real check is the manual one below, with three-button navigation.
- Locked notes (1.4.0; this app's own, so every test is new). Core, `LockedNotesTests`: locking and unlocking
  leave `UpdatedAt` and the text; other changes leave the lock; a habit cannot be locked; a locked note stays in its
  lists and on its label's page, and is in no search result (text or file name), tag list or tag count, open to be
  read or not; its files are opened only while it is open; open notes lock again together and are kept in memory
  only; a note being edited stays open until the edit ends, and locks then if the app was left meanwhile; a locked
  template starts no daily note; a first PIN leaves the app lock off; when the PIN is in use, and that it cannot be
  chosen again without the current one. Note lock is off until it is turned on (`SettingsTests`). A version 2
  database upgrades with nothing locked (`DatabaseStartupTests`), and every earlier version updates with every row
  kept ([Updates keep every note](#updates-keep-every-note)). The screens, bUnit, `LockedNotesTests`: Lock is
  offered only while Note lock is on, and a locked note keeps Unlock… with it off; Privacy & security has the switch,
  off at first, and it asks for no PIN; the first lock asks for a PIN and leaves the app lock off; with a PIN in use
  Lock locks at once, and so does a note whose lock was just removed, while a stored PIN nobody has typed since the
  app started is chosen anew; the locked card shows the lock mark and none of the title, text, tag, file name or
  label, and its menu is Pin, Unlock…, Archive and Move to trash; a wrong PIN leaves it locked, the right one opens
  it with its lock left on and Lock now and Remove lock in its menu, and after five the dialog says to wait; an open
  note locks again when the app goes to the background, when another page is opened, on Lock now and when the app
  lock locks; one being edited stays open until the edit is cancelled; a habit is offered no lock; a todo list locks
  from its menu, shows locked in its tab, is the list's own card while it is open and locks again; the trash hides
  it; an export and the automatic backups' folder ask for the PIN while a note is locked, and not otherwise; turning
  the app lock on asks for the PIN in use instead of a new one.
- Moods (1.7.0; this app's own, so every test is new). Core, `MoodTests`: a note has no mood unless one is chosen;
  a change of mood alone leaves `UpdatedAt` and raises `Revision`; an edit saves the text and the mood together
  and keeps the mood when none is given; other changes leave it; only notes and quick notes can have one, and a
  note that becomes a todo list gives it up; the daily note takes the mood chosen with its first words and keeps
  it when more are added without one; the names backups use, and the stored numbers. `MoodBackupTests`: in each
  format a mood is one more line, in its place, and nothing else in the note file or the manifest changes; a
  backup restored into an empty app brings each mood back, in each format; a mood is read in any case and an
  unknown one is none; a todo list or a habit that names one arrives without it. The mood tracker is off until it
  is turned on (`SettingsTests`). A version 3 database upgrades with no moods (`DatabaseStartupTests`), and every
  earlier version updates with every row kept ([Updates keep every note](#updates-keep-every-note)). The screens,
  bUnit, `MoodTests`: the switch is off as installed; no smilies where a note is written until it is on; the
  smiley button opens five, worst to best, each named, and shows the one chosen; choosing it again takes it off; a
  note and a quick note are posted with the mood and the next starts without; a mood alone cannot be posted; the
  daily note; editing shows the note's mood, and changing it alone does not make the note edited; an edit saves
  both; with the switch off an edit keeps the mood; a todo list is offered none; the card shows the smiley with
  its name, nothing with the switch off, and nothing on a locked note.
- Stickers (1.7.0; this app's own, so every test is new). Core, `StickerPackServiceTests`: a ZIP of pictures is a
  pack named after its file, every picture in path order, other files, hidden files and `__MACOSX/` passed over;
  `sticker-pack.json` names the pack and lists its stickers, in a top folder too; a pack added again with its ID
  replaces it; packs are listed by name; removing one deletes its files; a file that is not a pack is refused with
  the reason (not a ZIP, no pictures, a picture that is not one by its bytes, one over 2 MB, a listed file not there,
  an unreadable description, over 300 stickers) and leaves nothing behind; nothing in a ZIP names a path on the
  device; only a sticker's own numbered file is opened; a new service reads the packs already there.
  `StickerBackupTests`: in each format a sticker is one more list after the files, and a note without stickers has
  none; a backup restored into an empty app brings each sticker back as a sticker, in each format; the list makes
  only the note's own pictures stickers. `PdfNoteBuilderTests`: a sticker is a picture drawn small. A version 4
  database upgrades with no file a sticker (`DatabaseStartupTests`), and every earlier version updates with every row
  kept. The screens, bUnit, `StickerTests`: the switch is off as installed and shows the packs under it once on; a
  pack is added from a ZIP and listed with its details; a file that is not a pack is refused with the reason;
  removing a pack asks first and leaves the stickers on notes; no sticker button until the switch is on; with no
  pack the picker says where to add one; a tab for each pack; stickers chosen are posted as the note's own sticker
  files, in a note and a quick note, with the picker staying open; the stickers and the smilies close each other;
  stickers show in a row of their own, out of the picture grid and the viewer.
- Media handler: `Range` requests (start, middle, end, past the end → 416), content types, unknown IDs → 404.
- Welcome, Key lost, Erase all data.

## Backup conformance

The most important tests in the repository: they keep backups interchangeable with the web app
([05](05-backup-compatibility.md)).

**Export: one test per entry of `vectors.exports` (13).**

1. Seed an empty database from `vectors.active.items` and `vectors.archived.items`: `id`, `content`, `kind`, `dailyDate`,
   `isPinned`, `isArchived`, `createdAtUtc` and `updatedAtUtc` at full precision, and each attachment's `id`, `fileName`,
   `contentType`, `sizeBytes` and `createdAtUtc`, with its bytes from `vectors.files` stored through `AttachmentStore`.
   Tags come from the parser and must equal each note's `tags` (assert it). First insert `vectors.labels` with their
   `id`, `name` and `color`, then each note's `labelIds`.
2. Set the profile name to `vectors.account` (`maple`).
3. Run the exporter with the options from `query`: `format`, `layout`, `includeArchived`, `includeAttachments` (default
   true), `from`, `to`, `timeZone` (default UTC).
4. Read the produced ZIP. Text entries are `.md` and `.json`, plus `.txt` outside `attachments/`. Everything else is
   `base64:` + Base64. Replace the manifest's `"exportedAt": "…"` with `"exportedAt": "EXPORTED_AT"`.
5. Assert the **entry names in order** equal the vector's keys, then every entry's content.

Also: an attachment whose stored file is missing appears in `problems` as
`attachments/00000013__con.txt: the stored file could not be read, so it was left out.`, and `attachmentCount` drops by one.

**Restore: one test per vector export** (as `web/import/import.test.ts`):

- Build a ZIP from the entries (manifest with `EXPORTED_AT` replaced by `2025-09-01T10:00:00+02:00`). Read it: no
  problems, and the item IDs equal the manifest's note IDs in order.
- Each item's content equals the note's content (`TrimEnd()` except for JSON). Created equals the original truncated to
  the second. Updated is the same, except plain text that was not edited gives the created time. Pinned, archived, kind
  and daily date are equal; nothing is missing. Its labels are the names of the note's `labelIds`. The plan's label
  colours equal the manifest's `labels`, which do not include `Unused`.
- Attachments: names equal the originals (`SafeFileName` for plain text). Types are equal for JSON. Bytes are equal.
- Then **run** the restore into an empty database and compare the stored notes the same way, and the labels with the
  manifest's names and colours. Running it a second time imports nothing, creates no label and skips everything.

**Demo backups**: both restore 35 notes (22 timeline notes, 5 quick notes, 4 habits, 4 todo lists) and 8 files, with no
problems.

**Round trip**: restore each demo backup, export with the same options, and compare the entries with the original
archive. The demo backups are manifest version 2, so first take out of the new export what version 3 adds: the empty
`labels` lists and the version number. The manifests may then differ only in `exportedAt` and `account`, and file names in their 8-hex-digit ID prefix:
exports name files `attachments/{last 8 hex of the file's ID}_{name}` but do not record the file IDs, so a restore
gives files new IDs, as the web app's restore does (found in Phase 2).

## Performance budgets

Measured on the 50,000-note database from the benchmark generator (`benchmarks/storage/Shared/Bench.cs` `Generate`,
ported as `tests/FalconNotes.Core.Tests/Performance/LargeDatabase.cs`). A `[Trait("Category", "Performance")]` test runs
them on the CI Mac, and locally with `FALCON_PERF=1` (`FALCON_PERF_REPORT=path` writes the timings to a file). The CI Mac
is a shared virtual machine whose speed varies by up to 2× between runs, so CI sets `FALCON_PERF_SLACK=1.5` and holds it
to 1.5× these budgets; locally they apply as written. Phase 7 repeats them by hand on the slowest Android
phone supported, against 4× these budgets:

| Operation | Budget (Mac, release build) |
|---|---|
| Open database + first feed page | 20 ms |
| Feed page and pinned list (also with a label hiding its notes), habits page, calendar month | 5 ms |
| Tag counts, label counts | 80 ms |
| First page of a tag or label filter | 100 ms |
| Search with no match | 250 ms (first page of matches shown as soon as found) |
| Post, edit, pin | 5 ms |
| Restore of 10,000 notes without files | 5 s |
| App start to Home (release, cold) | 1.5 s Mac and Windows, 2.5 s mid-range Android |

## Manual QA

Run before each release on **Android phone**, **Android tablet (landscape, ≥ 1,024 dp)**, **Windows** and **macOS**,
in light and dark mode:

- [ ] **Update over the released version** ([Updates keep every note](#updates-keep-every-note)): notes of every
      kind made in the released version, with a file, a label, a pin, an archived note and one in the trash, are all
      there after the new version is installed over it, and the settings are as they were.
- [ ] First run: Welcome, name, Start writing; Welcome → Restore a demo backup.
- [ ] Notes: post with a title and date, Markdown, tags, a checklist ticked in place, a picture (picker; paste and drop on
      desktop), a video that seeks, audio, a PDF that opens, Save a copy. With **Shrink photos before adding** on: a
      large camera photo taken upright comes out upright, smaller and as a `.jpg`; a PNG with transparency and a GIF
      stay as they are.
- [ ] Edit with double-tap; pin; labels; move to quick notes and back; copy text; archive and restore; trash, Undo,
      restore from the trash, delete forever, empty trash.
- [ ] Search, tag (nested), label and day filters, calendar dots, Tags page in both orders with the filter.
- [ ] Todo: add, tick, edit, remove, Edit as Markdown, clear completed, pin, archive.
- [ ] Habits: add, tick today and an earlier week, chart (weeks and months, one habit), calendar, archive and restore.
- [ ] Daily notes: the Today card; the first words become the note; it rolls over at midnight (change the clock).
- [ ] Labels that hide their notes: a database made by an earlier version opens after the update with no note
      hidden. Turn the eye on for a label: its notes leave Home and Quick notes, stay on the label's page and in
      search, and todo lists stay in their tab. Turn Labels off: the notes are back.
- [ ] Settings: every switch hides and shows its feature without losing data; menu order by drag and by arrows; text
      sizes; accents; week start. Each background in the light and the dark theme: headings and empty states that lie
      on it stay easy to read, a long list scrolls smoothly, and None and **Remove background** take it away.
- [ ] Backup: export in each format → restore in the web app; web export → restore here; restore twice skips.
- [ ] Automatic backups: turn on and choose a folder → a `falcon-notes-auto-….zip` appears there and restores; **Back
      up now** twice in a minute leaves one file; with Keep at 3, a fourth backup deletes the oldest and leaves a file
      of another name alone; set the clock a day on and reopen the app → a new backup; delete the folder in the
      Files app → Settings and Home say it failed, and **Change…** mends it; a folder on a memory card or USB drive;
      turning it off leaves the backups.
- [ ] Android, with **three-button navigation** and again with gesture navigation: the drawer's last row (the name, and
      **Lock now** with the app lock on) is above the navigation bar and can be tapped; the same for the sidebar on a
      tablet; toasts and the picture viewer's buttons are clear of the bars; a phone in landscape.
- [ ] Locked notes: with **Note lock** off (as installed) no menu offers Lock; turned on in Privacy & security, Lock
      on a note with a picture, a tag and a label asks for a PIN the first time and not the second; the card shows
      the lock in its corner and nothing else; it is not found by search, its tag or the Tags page, and is on its
      label's page; Unlock… with a wrong PIN, then the right one, then a fingerprint with the app lock on: the note
      shows with an open lock, its pictures load, and it can be edited; it is locked again after going to another
      page and back, after the home button, after the screen turned off, and after the app was closed; attaching a
      file to an open note through the picker keeps the editor, and the note locks once it is saved; Lock now and
      Remove lock; a todo list in its tab and a quick note; pin, archive and trash it while locked, and restore it;
      Export ZIP… and turning automatic backups on ask for the PIN, and the ZIP holds the note; restored on another
      install, it is not locked; turning the app lock on asks for the same PIN; Change PIN… with the app lock off;
      Note lock turned off again leaves the locked notes locked, with Unlock… in their menus.
- [ ] Export to PDF: with the switch off (as installed) no menu offers it; turned on in Settings → Features, a note
      with a title, a checklist, a table, a code block, a link, Sinhala or Tamil text, an emoji and two photos (one
      taken on its side) saves as a PDF from its ⋯ menu, under its title as the file's name; opened in another app
      it has the text as text (English words can be selected and searched), the boxes ticked as in the note, the photos upright
      and the date under the title; a note long enough for three pages breaks between lines and numbers its pages; a
      todo list and a quick note; a note with a video names the file at the end; cancelling the save dialog saves
      nothing and says nothing; a locked note offers it only while it is unlocked; nothing is left in
      `cache/export/`; and the app still has no storage permission.
- [ ] Moods: as installed no smiley button stands by the paperclip and no note shows a smiley; with **Mood
      tracker** on in Settings → Features the button is there on Home, in the Today card and in Quick notes; it
      opens five smilies, drawn in colour by the device, that fit beside each other on a narrow phone; a note
      posted with one shows it after its time, in light and dark; edited to another, the note does not turn
      "· edited"; chosen again, it is gone; a locked note hides it; an export holds `mood:` for that note alone,
      and restored on another install the smiley is back; with the switch off again every smiley is hidden, and on
      again they are all there; TalkBack reads "Mood: Good".
- [ ] Stickers: as installed no sticker button stands by the paperclip and Settings → Features shows **Stickers**
      off; turned on, **Add pack…** opens the system picker for ZIP files, and a pack added shows with its first
      sticker; the sticker button opens the picker on Home, in the Today card and in Quick notes, its stickers drawn
      from `/_stickers/…`, an animated GIF moving; several chosen are posted and show small above the note's
      pictures, in light and dark; removing the pack leaves them; a note's PDF draws them small; an export holds the
      `stickers:` list, and restored on another install they are stickers again; TalkBack reads "Add sticker: …".
- [ ] The app made again: leave Home with Back, open the app again from the launcher before Android has ended its
      process, and open the drawer: its name and mark are under the status bar's clock, not behind it, and its last
      row is above the navigation bar; the theme, accent and background are as chosen; rotate, and it still fits.
- [ ] Keep line breaks: as installed, a note of three lines typed one under another shows as one paragraph; with
      the switch on in Settings → Writing each line is on its own, in a paragraph, a checklist item and a quote, on
      Home, in the Archive and in the trash; a code block, a table and a blank line between paragraphs look as
      before; ticking a box still ticks the right one; editing the note shows the text as typed; its PDF breaks
      where the screen does; Help reads as before; switched off again, every note is as it was.
- [ ] App lock: set, lock after delay, biometrics, wrong PIN ×5, Lock now, background cover, screenshots blocked
      (Android), Forgot PIN → erase.
- [ ] Erase all data → Welcome. Removing the device key with the Debug-only developer action leads to Key lost, where
      restore works and the old data is kept aside.
- [ ] Accessibility: keyboard only on desktop; TalkBack, Narrator and VoiceOver read the menu, a note card, the
      composer and dialogs; 130% font size.
- [ ] Offline: with the network off (airplane mode), everything works; nothing in the app ever asks for the network.
