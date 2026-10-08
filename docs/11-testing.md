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

A fixed clock gives rows made one after another the same time, and their order then falls back to their IDs. Version 7
IDs made within one millisecond are in random order, so a test that checks the order of a note's files must give them
distinct times: `TestApp.AddFileAsync` moves the clock on one tick, and a restore test sets `Clock.Step`. A machine
fast enough to make two IDs in a millisecond (the Linux and Windows CI machines) otherwise fails now and then.

## Porting the reference tests

| Reference tests | Port to | What they protect |
|---|---|---|
| `web/lib/titles.test.ts`, `todo.test.ts`, `habits.test.ts`, `markdownEdit.test.ts`, `tagSuggest.test.ts`, `menu.test.ts`, `format.test.ts`, `shrinkPhoto.test.ts` (~44 cases) | `Core.Tests/Text/*`, `Core.Tests/Attachments/PhotoShrinkerTests.cs` | Every text rule |
| `web/components/Markdown.test.tsx` (7) | `Core.Tests/Markdown/*` | Raw HTML not rendered, tags linked, not in code, task offsets, safe links |
| `server/../Notes/*Tests.cs` (notes API, calendar, daily notes, kinds, import, trash), `Labels/LabelsTests.cs`, `Storage/StorageTests.cs`, `Auth/DeleteContentTests.cs` | `Core.Tests/Notes/*`, `Labels/*` | Lists and cursors, filters, search, the calendar's days, daily notes (one per day, given up in the trash), kind moves (no habits), trash and purge, label limits and counts, storage use, delete all |
| `server/../Crypto/AttachmentCipherTests.cs`, `Infrastructure/AttachmentStoreTests.cs` | `Core.Tests/Crypto/*`, `Attachments/*` | Every tamper case and chunk boundary; storage keys cannot escape the folder |
| `server/../Export/ExportTests.cs` | `Core.Tests/Backup/ExportTests.cs` | Options, ranges, daylight-saving gaps, problems in the manifest |
| `web/import/import.test.ts` (5) | `Core.Tests/Backup/RestoreTests.cs` | Every format and layout restores; single files; archives without a manifest; wrong files; skipping existing notes |
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
- Media handler: `Range` requests (start, middle, end, past the end → 416), content types, unknown IDs → 404.
- Welcome, Key lost, Erase all data.

## Backup conformance

The most important tests in the repository: they keep backups interchangeable with the web app
([05](05-backup-compatibility.md)).

**Export: one test per entry of `vectors.exports` (13).**

1. Seed an empty database from `vectors.active.items` and `vectors.archived.items`: `id`, `content`, `kind`, `dailyDate`,
   `isPinned`, `isArchived`, `createdAtUtc` and `updatedAtUtc` at full precision, and each attachment's `id`, `fileName`,
   `contentType`, `sizeBytes` and `createdAtUtc`, with its bytes from `vectors.files` stored through `AttachmentStore`.
   Tags come from the parser and must equal each note's `tags` (assert it).
2. Set the profile name to `vectors.account` (`maple`).
3. Run the exporter with the options from `query`: `format`, `layout`, `includeArchived`, `includeAttachments` (default
   true), `from`, `to`, `timeZone` (default UTC).
4. Read the produced ZIP. Text entries are `.md` and `.json`, plus `.txt` outside `attachments/`. Everything else is
   `base64:` + Base64. Replace the manifest's `"exportedAt": "…"` with `"exportedAt": "EXPORTED_AT"`.
5. Assert the **entry names in order** equal the vector's keys, then every entry's content.

Also: an attachment whose stored file is missing appears in `problems` as
`attachments/00000009__con.txt: the stored file could not be read, so it was left out.`, and `attachmentCount` drops by one.

**Restore: one test per vector export** (as `web/import/import.test.ts`):

- Build a ZIP from the entries (manifest with `EXPORTED_AT` replaced by `2025-09-01T10:00:00+02:00`). Read it: no
  problems, and the item IDs equal the manifest's note IDs in order.
- Each item's content equals the note's content (`TrimEnd()` except for JSON). Created equals the original truncated to
  the second. Updated is the same, except plain text that was not edited gives the created time. Pinned, archived, kind
  and daily date are equal; nothing is missing.
- Attachments: names equal the originals (`SafeFileName` for plain text). Types are equal for JSON. Bytes are equal.
- Then **run** the restore into an empty database and compare the stored notes the same way. Running it a second time
  imports nothing and skips everything.

**Demo backups**: both restore 35 notes (22 timeline notes, 5 quick notes, 4 habits, 4 todo lists) and 8 files, with no
problems.

**Round trip**: restore each demo backup, export with the same options, and compare the entries with the original
archive. The manifests may differ only in `exportedAt` and `account`, and file names in their 8-hex-digit ID prefix:
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
| Feed page, pinned list, habits page, calendar month | 5 ms |
| Tag counts, label counts | 80 ms |
| First page of a tag or label filter | 100 ms |
| Search with no match | 250 ms (first page of matches shown as soon as found) |
| Post, edit, pin | 5 ms |
| Restore of 10,000 notes without files | 5 s |
| App start to Home (release, cold) | 1.5 s Mac and Windows, 2.5 s mid-range Android |

## Manual QA

Run before each release on **Android phone**, **Android tablet (landscape, ≥ 1,024 dp)**, **Windows** and **macOS**,
in light and dark mode:

- [ ] First run: Welcome, name, Start writing; Welcome → Restore a demo backup.
- [ ] Notes: post with a title and date, Markdown, tags, a checklist ticked in place, a picture (picker; paste and drop on
      desktop), a video that seeks, audio, a PDF that opens, Save a copy.
- [ ] Edit with double-tap; pin; labels; move to quick notes and back; copy text; archive and restore; trash, Undo,
      restore from the trash, delete forever, empty trash.
- [ ] Search, tag (nested), label and day filters, calendar dots, Tags page in both orders with the filter.
- [ ] Todo: add, tick, edit, remove, Edit as Markdown, clear completed, pin, archive.
- [ ] Habits: add, tick today and an earlier week, chart (weeks and months, one habit), calendar, archive and restore.
- [ ] Daily notes: the Today card; the first words become the note; it rolls over at midnight (change the clock).
- [ ] Settings: every switch hides and shows its feature without losing data; menu order by drag and by arrows; text
      sizes; accents; week start.
- [ ] Backup: export in each format → restore in the web app; web export → restore here; restore twice skips.
- [ ] App lock: set, lock after delay, biometrics, wrong PIN ×5, Lock now, background cover, screenshots blocked
      (Android), Forgot PIN → erase.
- [ ] Erase all data → Welcome. Removing the device key with the Debug-only developer action leads to Key lost, where
      restore works and the old data is kept aside.
- [ ] Accessibility: keyboard only on desktop; TalkBack, Narrator and VoiceOver read the menu, a note card, the
      composer and dialogs; 130% font size.
- [ ] Offline: with the network off (airplane mode), everything works; nothing in the app ever asks for the network.
