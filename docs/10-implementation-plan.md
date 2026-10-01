# 10. Implementation plan

Eight phases, each ending in something that runs and is tested. Do them in order. Within a phase, build the Core rules
first, then their tests, then the screens. Estimates are focused developer-weeks of conventional work; with an AI
agent doing the porting, calendar time is usually much shorter, but the relative sizes hold.

| Phase | Outcome | Estimate |
|---|---|---|
| 0 | Skeleton on three platforms; the risky parts proven | 1 week |
| 1 | Core: storage, crypto, every rule, fully tested | 1.5–2 weeks |
| 2 | Backup compatibility proven against the shared vectors | 1 week |
| 3 | Shell, design system, appearance, first run | 1 week |
| 4 | Notes: Home, composer, cards, attachments, filters, archive, trash, daily notes, quick notes | 2 weeks |
| 5 | Todo, Habits, Tags, Calendar, Labels | 1–1.5 weeks |
| 6 | Settings, backup screens, app lock, Help | 1 week |
| 7 | Platform polish, packaging, performance and QA | 1–1.5 weeks |
| | **Total** | **9.5–12 weeks** |

Keep `CHANGELOG.md` under `[Unreleased]` as you go, in the reference's style.

## Phase 0: skeleton and spikes

Set up:

- [ ] The solution and projects as in [02-architecture.md](02-architecture.md#repository-layout): `Directory.Build.props`
      (nullable, warnings as errors, documentation comments), `Directory.Packages.props` (central versions), `global.json`.
- [ ] `MapleNotes.App` with a `BlazorWebView` showing a page from `MapleNotes.UI`, built and run on **Android**
      (emulator), **Windows** and **macOS**.
- [ ] The Tailwind build step ([02](02-architecture.md#styling-pipeline)), with `app.css` copied from the reference.
      One ported component (`Button`) renders identically to the web app.
- [ ] CI (GitHub Actions): build all three heads; run Core and UI tests on Linux, Windows and macOS.

Spikes. Each ends with a written result appended to this file under "Spike results":

| Spike | Question | Done when |
|---|---|---|
| S1 Encrypted SQLite | Do `SQLite3MC.PCLRaw.bundle` and `cipher=aegis` with a raw key work in the packaged app on all three platforms? | On each platform: create, write, reopen with the key, fail with a wrong key; the header is unreadable; a database copied from macOS opens on Android with the same key |
| S2 Secure storage | Does `SecureStorage` keep a key across restarts in the packaged app? | Windows MSIX, Mac Catalyst with the keychain entitlement, Android with Auto Backup off: the key survives a restart and an app update. Removing the key while the database exists (a Debug-only developer action) leads to **Key lost**, not a crash. |
| S3 Media | Which mechanism in [02](02-architecture.md#serving-attachments-to-the-webview) serves encrypted attachments with HTTP ranges? | A 200 MB MP4 plays and seeks instantly, and a 20 MB JPEG shows, on all three platforms |
| S4 Files | Do the file picker, `FileSaver` (with a 1 GB file) and `Launcher` (open a PDF) work? | Yes on all three; Android uses the system pickers with no storage permission |
| S5 WebView behaviour | Do `<dialog>`, `execCommand('insertText')` with Undo, `content-visibility`, pointer capture and the content security policy behave the same in WebView2, Android WebView and WKWebView (Catalyst)? Does Android's Back button follow the WebView history? Do ⌘B/I/K reach the page on macOS? | A small test page passes on all three; deviations are recorded with their workaround |
| S6 Benchmark on a real phone | Do [13-storage-benchmark.md](13-storage-benchmark.md)'s conclusions hold on the slowest Android phone to be supported? | `benchmarks/storage` run on it, results committed, doc 13 updated |

**Acceptance**: the empty app starts in under 2 s (release build) on every platform, all six spikes are recorded, and CI
is green.

## Phase 1: Core

- [ ] `Domain/*`, `Text/*` (titles, todo, habits, tags, tag suggestions, Markdown editing, dates, relative time, bytes,
      menu order, tag tree), each with its tests ported from the reference ([11](11-testing.md)).
- [ ] `Crypto/*`: key material, `AttachmentCipher`, `DecryptingAttachmentStream` with the server's tests (tampering,
      truncation, reordering, wrong key, every chunk boundary).
- [ ] `Storage/*`: `Database`, migrations, repositories for notes, bodies, tags, labels, attachments and settings.
- [ ] `Attachments/*`: `AttachmentStore`, `UploadPolicy`, `AttachmentService`, `PhotoShrinker`.
- [ ] `Notes/*`, `Labels/*`, `Preferences/*`: every rule in [04](04-domain-rules.md), including lists and cursors,
      search, the calendar, tag and label counts and filters, trash and purge, daily notes, kind moves, delete all, and
      storage use.
- [ ] `Markdown/MarkdownRenderer` with tag links, task offsets and safe links.
- [ ] `Maintenance/*`: start-up tasks, purge, cleanup, database copies, temporary files.
- [ ] `Events/ChangeFeed`.

**Acceptance**: Core tests pass on Linux, Windows and macOS. A 50,000-note database (from the benchmark generator) runs
every list, count, filter and search query within the budgets in [11](11-testing.md#performance-budgets) on the Mac.

## Phase 2: backup compatibility

- [ ] Copy the server exporter ([05](05-backup-compatibility.md#export)); write ZIPs to the cache folder.
- [ ] Port `parse.ts` and `importer.ts` ([05](05-backup-compatibility.md#restore)).
- [ ] The conformance tests ([11](11-testing.md#backup-conformance)).

**Acceptance**: all 13 exports in `fixtures/export-vectors.json` match entry for entry, and restoring each one gives back
the original notes. Both demo backups restore with 35 notes and 8 files. Export → restore → export gives the same
archive. Run once on Android and once on Windows with the on-device test runner, not only on the Mac.

## Phase 3: shell and design system

- [ ] UI primitives, `Icon`, `Logo`, `Toaster`, `ConfirmDialog`, `DropdownMenu` ([06](06-design-system.md#components)).
- [ ] `AppShell` (sidebar ≥ 1,024 px, drawer below), routes, search box, menu with order and sizes, labels list slot,
      calendar slot.
- [ ] Appearance: theme, accent and native chrome; first paint without a flash.
- [ ] `AppBootstrapper`, **Welcome**, **Key lost** ([07](07-screens.md#welcome)).

**Acceptance**: side-by-side screenshots of the empty shell match `reference/.../docs/screenshots/*` in light and dark,
at 375 px and 1,280 px wide.

## Phase 4: notes

- [ ] `NoteList` with infinite scroll and reload on change; `NoteCard` with menu, double-tap, tick boxes, labels and
      removal with Undo.
- [ ] `Composer`: titles, date suggestion, toolbar, shortcuts, tag suggestions, attachments (picker, paste, drop,
      progress, shrink).
- [ ] `AttachmentGallery`, `ImageViewer`, players, Open and Save a copy.
- [ ] Home (Today card, pinned, feed), filters (`?tag`, `?q`, `?day`, `?label`), Archive, Trash, Quick notes.

**Acceptance**: the ported component tests pass. On each platform, the manual checklist for notes
([11](11-testing.md#manual-qa)) passes. Scrolling a 10,000-note timeline with pictures stays smooth on a mid-range Android
phone.

## Phase 5: todo, habits, tags, calendar, labels

- [ ] Todo page and `TodoCard` (with Edit as Markdown).
- [ ] Habits page, `HabitRow`, `HabitChart`, `HabitCalendar`, archived habits.
- [ ] Tags page; side-menu calendar; labels (picker, chips, side menu, filter, Settings → Labels).

**Acceptance**: the ported tests pass, and the manual checklist for these screens passes on all three platforms.

## Phase 6: settings, backups, lock, help

- [ ] Settings sections ([07](07-screens.md#settings)): Profile, Appearance, Side menu, Writing, Features, Labels,
      Backup & data (export, restore, trash, delete all), Privacy & security (app lock, data protection).
- [ ] The app lock: PIN set and change, biometrics per platform, lock after a delay, background cover, `FLAG_SECURE`,
      tries limit, Lock screen, Forgot PIN → erase ([03](03-data-storage-and-security.md#app-lock)).
- [ ] Erase all data.
- [ ] Help with the adapted guide ([08](08-help-guide.md)).

**Acceptance**: export from the app restores in the web app (run the reference with Docker or `dotnet run`), and a web
export restores in the app. The lock cannot be bypassed by Back, the app switcher, deep links or reopening.

## Phase 7: platforms, packaging, QA

- [ ] Platform details in [12-platforms.md](12-platforms.md): icons, splash, manifests and entitlements, single
      instance on Windows, window sizes, keyboard on macOS, Android insets, keyboard resize, Back.
- [ ] `THIRD-PARTY-NOTICES.md` and a licence check script (port `reference/.../scripts/check-licenses.py` to read
      `packages.lock.json` / `deps.json` and the copied Lucide icons).
- [ ] Release builds: Android AAB/APK signed, Windows MSIX signed, macOS app signed and notarized (or an unsigned
      build for personal use, by decision).
- [ ] The full manual QA checklist on every platform, the performance budgets, and an accessibility pass (keyboard
      only, TalkBack, Narrator, VoiceOver).

**Acceptance**: version 1.0.0 tagged; CHANGELOG written; all checklists ticked.

## Spike results

_Append each spike's result here, with the date, the platform versions and the decision taken._
