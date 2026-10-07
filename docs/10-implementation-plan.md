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

**Android first** (decided 2026-10-01): Phase 0 is done for Android on the emulator. The Mac Catalyst and Windows heads,
their spikes and their CI builds follow when the owner turns to those platforms (unticked items below). Core and UI
stay platform-neutral throughout.

Set up:

- [x] The solution and projects as in [02-architecture.md](02-architecture.md#repository-layout): `Directory.Build.props`
      (nullable, warnings as errors, documentation comments), `Directory.Packages.props` (central versions), `global.json`.
- [x] `FalconNotes.App` with a `BlazorWebView` showing a page from `FalconNotes.UI`, built and run on **Android**
      (emulator).
- [ ] The same on **Windows** and **macOS** (needs Xcode 26.5 on the Mac: .NET for Mac Catalyst 26.5 refuses Xcode 27).
- [x] The Tailwind build step ([02](02-architecture.md#styling-pipeline)), with `app.css` copied from the reference.
      One ported component (`Button`) renders identically to the web app (same classes, asserted in its tests; same
      stylesheet and Tailwind version).
- [x] CI (GitHub Actions): build the Android head; run Core and UI tests on Linux, Windows and macOS.
- [ ] CI: build the Windows and Mac Catalyst heads.

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

Android status (2026-10-01): S1–S5 pass on the emulator (results below); S6 is deferred until a real phone is available;
the release build reaches its first screen in 0.7–0.9 s. CI goes green once the repository is on GitHub.

## Phase 1: Core

- [x] `Domain/*`, `Text/*` (titles, todo, habits, tags, tag suggestions, Markdown editing, dates, relative time, bytes,
      menu order, tag tree), each with its tests ported from the reference ([11](11-testing.md)).
- [x] `Crypto/*`: key material, `AttachmentCipher`, `DecryptingAttachmentStream` with the server's tests (tampering,
      truncation, reordering, wrong key, every chunk boundary).
- [x] `Storage/*`: `Database`, migrations, repositories for notes, bodies, tags, labels, attachments and settings.
- [x] `Attachments/*`: `AttachmentStore`, `UploadPolicy`, `AttachmentService`, `PhotoShrinker` (its rules; the
      decoding is the platform's `IImageCodec`, waiting on the SkiaSharp decision in
      [02](02-architecture.md#dependencies)).
- [x] `Notes/*`, `Labels/*`, `Settings/*`: every rule in [04](04-domain-rules.md), including lists and cursors,
      search, the calendar, tag and label counts and filters, trash and purge, daily notes, kind moves, delete all, and
      storage use.
- [x] `Markdown/MarkdownRenderer` with tag links, task offsets and safe links.
- [x] `Maintenance/*`: start-up tasks, purge, cleanup, database copies, temporary files.
- [x] `Events/ChangeFeed`.

**Acceptance**: Core tests pass on Linux, Windows and macOS. A 50,000-note database (from the benchmark generator) runs
every list, count, filter and search query within the budgets in [11](11-testing.md#performance-budgets) on the Mac.

Status (2026-10-01): 283 Core tests pass on the Mac; Linux and Windows run in CI once the repository is on GitHub. The
budgets on an M1 Pro, Release build, 50,000 notes: open and first feed page 0.9 ms, feed page 0.3 ms, pinned 0.3 ms,
habits 0.2 ms, calendar month 0.5 ms, tag counts 38 ms, label counts 22 ms, tag filter 17 ms, label filter 12 ms,
search with no match 140 ms, post, edit and pin under 0.2 ms each.

## Phase 2: backup compatibility

- [x] Copy the server exporter ([05](05-backup-compatibility.md#export)); write ZIPs to the cache folder.
- [x] Port `parse.ts` and `importer.ts` ([05](05-backup-compatibility.md#restore)).
- [x] The conformance tests ([11](11-testing.md#backup-conformance)).

**Acceptance**: all 13 exports in `fixtures/export-vectors.json` match entry for entry, and restoring each one gives back
the original notes. Both demo backups restore with 35 notes and 8 files. Export → restore → export gives the same
archive. Run once on Android and once on Windows with the on-device test runner, not only on the Mac.

Status (2026-10-01): all of it passes on the Mac (Core tests) and on the Android emulator (the Debug spike page's S7,
which runs the same checks inside the app: 13 exports match, 13 restores clean, both demos 35 notes and 8 files, in
5.2 s). Windows waits for the Windows head. A 10,000-note restore takes 0.4 s on the Mac (budget 5 s).

## Phase 3: shell and design system

- [x] The falcon mark ([06](06-design-system.md#app-icon-and-splash)): drawn, approved by the owner, saved as
      `fixtures/falcon-mark.svg`; the app icon and splash made from it.
- [x] UI primitives, `Icon`, `Logo`, `Toaster`, `ConfirmDialog`, `DropdownMenu` ([06](06-design-system.md#components)).
- [x] `AppShell` (sidebar ≥ 1,024 px, drawer below), routes, search box, menu with order and sizes, labels list slot,
      calendar slot.
- [x] Appearance: theme, accent and native chrome; first paint without a flash (Android; Windows and macOS follow
      their heads).
- [x] `AppBootstrapper`, **Welcome**, **Key lost** ([07](07-screens.md#welcome)).

**Acceptance**: side-by-side screenshots of the empty shell match `reference/.../docs/screenshots/*` in light and dark,
at 375 px and 1,280 px wide, apart from the name and mark.

Status (2026-10-07): built on the Android emulator — Welcome, the gated redirect to it when there is no profile, the
drawer/sidebar with search, menu, calendar and profile footer, and Key lost's two ways out all run correctly (checked
against docs/07 by hand; see docs/10's spike results style below for the next session). The falcon mark is approved
and the Android adaptive icon and splash are generated from it. `AppShellTests` (6), `CalendarTests` (4),
`WelcomeTests` (5) and `KeyLostTests` (3) port or newly cover docs/11's Phase 3 list; `KeyLostRecoveryTests` (2) covers
the Core-side move-aside in `Core.Tests`. Still open: the literal side-by-side screenshot diff against
`reference/.../docs/screenshots/*` (Home itself is Phase 4, so only the chrome can be compared yet), and the Windows
and Mac Catalyst heads.

## Phase 4: notes

- [x] `NoteList` with infinite scroll and reload on change; `NoteCard` with menu, double-tap, tick boxes, labels and
      removal with Undo.
- [x] `Composer`: titles, date suggestion, toolbar, shortcuts, tag suggestions, attachments (picker, progress). Paste
      and drag-and-drop, and "shrink photos", are open (below).
- [x] `AttachmentGallery`, `ImageViewer`, players, Open and Save a copy. Share (Android) is left for Phase 7.
- [x] Home (Today card, pinned, feed), filters (`?tag`, `?q`, `?day`, `?label`), Archive, Trash, Quick notes.

**Acceptance**: the ported component tests pass. On each platform, the manual checklist for notes
([11](11-testing.md#manual-qa)) passes. Scrolling a 10,000-note timeline with pictures stays smooth on a mid-range Android
phone.

Status (2026-10-07): built and tested on the Android emulator (bUnit; the manual checklist and the 10,000-note
scroll test still need a device, docs/12). Every component above is ported and covered: `NoteEditor<T>` (optimistic
checkbox ticks and other structured edits), `Markdown.razor` over the already-tested Core renderer, `LabelChips`/
`LabelPicker`, `AttachmentGallery`/`ImageViewer`/`FileLink` (Open and Save a copy; "Copy text" needed the new
`IClipboard` platform service, already named in docs/02 but not yet implemented), the composer's text-editing primitives (`FormatToolbar`, `TagSuggestions`,
`editor.js`), `Composer`, `NoteCard` (`NoteRemoval` as a plain helper, `gestures.js`), `NoteList` (`observe.js`),
`Home`/`FilterHeader`/`TodayCard`, `Archive`, `Quick`, `Trash`/`TrashCard`, and `FeatureOff` (pulled forward from
Phase 5's `TodoPage.tsx`, since Archive and Quick notes both need it). 115 UI tests and 346 Core tests pass.

Two gaps, deliberately left open rather than blocking the phase: "shrink photos" needs the platform's `IImageCodec`
(SkiaSharp), pending the owner's approval of the dependency (docs/02); `Composer` calls it only if it ends up
registered, so it degrades to adding the file unshrunk until then. Paste and drag-and-drop need their own
`IJSStreamReference` plumbing beyond the native picker's; the native picker covers every platform meanwhile.

Several real bugs were caught by writing the tests, not by inspection: `TagSuggestions` and `NoteCard.SetEditing`
mutated state without calling `StateHasChanged`, so arrow-key navigation and double-tap-to-edit would have silently
done nothing in the real app; `NoteCard`'s `Markdown` binding was `Content="Body"` instead of `Content="@Body"` — on
a component tag, a string-typed parameter without `@` is a literal, not a reference to the same-named member, so
every note card rendered the literal word "Body". The same mistake recurred in `Home.razor`'s `Tag`/`Search`
bindings. Also found: a bUnit quirk where `Render<T>` with a completely empty parameter builder fails to register
the root component (use the parameterless overload, or add at least one `.Add`); and repeated sync-over-async
deadlocks where a test's `WaitForAssertion` predicate blocked on a fresh async database call from the renderer's
own dispatcher thread, which a pending save needed in order to complete — the fix is always to wait on a plain
DOM/in-memory signal first, then read the database with a real `await` once that signal confirms the operation has
already finished.

## Phase 5: todo, habits, tags, calendar, labels

- [x] Todo page and `TodoCard` (with Edit as Markdown).
- [x] Habits page, `HabitRow`, `HabitChart`, `HabitCalendar`, archived habits.
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

### Android, 2026-10-01

Android 16 (API 36) emulator `MAUI_Emulator_API_36` (arm64, 4 cores, 2 GB), Android System WebView 133.0.6943.137,
.NET SDK 10.0.300, MAUI workload 10.0.20 with `Microsoft.Maui.Controls` 10.0.110, SQLite3MC 2.4.0 (SQLite 3.53.4).
Run from the Debug-only page `/dev/spikes` (`adb shell am start … --es route "/dev/spikes?auto=1"`).

**S1 Encrypted SQLite: works.** Create, write, reopen and read took 20 ms; a pooled open 4–5 ms. A wrong key fails
with `SQLITE_NOTADB`, reported as `DatabaseKeyException`. The header is not `SQLite format 3`. A database written on
the Mac with the same key opens and reads on Android. The AEGIS variant is pinned with the URI parameter
`algorithm=aegis-256` (also SQLite3MC's default); a database written with another variant does not open, which a Core
test checks. **Decision:** `file:{path}?cipher=aegis&algorithm=aegis-256` with a raw key, as in docs/03.

**S2 Secure storage: works.** The first run creates the key and the database. The key's fingerprint stayed the same
across two restarts and an update installed over the app (versionCode 1 → 2). With the key removed (Debug-only
developer action), the next start shows **Key lost** (a stub until Phase 3), with no crash and the database file
unchanged (same SHA-256). The installed package has no `ALLOW_BACKUP` flag. **Decision:** `SecureStorage` as in docs/02.

**S3 Media: option A works on Android, with the native response built by hand.** `BlazorWebView.WebResourceRequested`
fires for `/_media/{id}` off the UI thread. With MAUI's `SetResponse`, video did not load, for three reasons:

1. `SetResponse` repeats `Content-Type` ("video/mp4, video/mp4").
2. Android's WebView applies a request's `Range` itself: it expects the whole file, checks the range against the
   stream's `available()`, skips to the start, and takes `Content-Length` from `available()`. MAUI's stream adapter
   reports 0 available and skips by reading, so ranged responses failed with "Failed to fetch".
3. The WebView reads 2 KB per call across the Java bridge: the 24 MB image needed 11,860 calls.

The handler therefore sets `PlatformArgs.Response` to its own `WebResourceResponse` on Android: the MIME type once, a
`MediaInputStream` (a Java `InputStream` over the decrypting stream whose `available()` reports the bytes left and
whose `skip()` seeks), a limit at the range's end, and a 64 KiB `BufferedInputStream`. Results: the 200 MB MP4
(index at the end) loads its metadata in about 0.1 s, plays, and a seek to 70% takes about 0.5 s and keeps playing;
the 24 MB, 5250 × 3500 JPEG fetches in 0.3–0.5 s and decodes in about 1 s; `bytes=0-99` returns 100 bytes; the
decrypted tail matches the original's SHA-256; an unknown ID gives 404. Encrypting the two files (225 MB) took 1.5 s.
A range past the end is rejected by the WebView itself ("Failed to fetch") before our 416 is seen, which media players
never ask for. **Decision:** option A, with the Android response path above; WebView2 and WKWebView still to verify.

**S4 Files: works, with our own saver.** The picker is the system's (Storage Access Framework) with no permission;
two files picked from Downloads were encrypted into the store (the 24 MB photo in 0.25 s) and no copy was left in the
cache. The Community Toolkit's `FileSaver` shows the right dialog (`ACTION_CREATE_DOCUMENT`, the suggested name filled
in) but copies 4 KB at a time through Java's `OutputStream.WriteAsync`: 1 GB took 790 s. `AndroidFileSaver` shows the
same dialog and writes to the document's file descriptor with a .NET `FileStream` and a 1 MiB buffer: 1 GB in 5.7 s,
content verified; cancelling returns "cancelled". `Launcher` opened the PDF from `cache/open/` in the system viewer.
**Decision:** our own `IFileSaver` on Android; the toolkit stays for other platforms and the status bar.

**S5 WebView behaviour: passes, with one gap.** `<dialog>` with `showModal()`, `execCommand('insertText')` and Undo,
`content-visibility`, `contain-intrinsic-size` and pointer capture all work. The content security policy blocks remote
images, inline scripts, `eval` and remote `fetch` (violations reported for `img-src`, `script-src` and `connect-src`).
Back goes back through the WebView's history and leaves the app from the first page. The policy needs
`base-uri 'self'` instead of `'none'`, because Blazor's `<base href="/">` must apply (recorded in docs/02). **Gap:**
`env(safe-area-inset-top)` is `0px` in this WebView (Chrome 133), so content drawn edge to edge sits under the status
bar; Phase 3 passes the system insets to the page instead (docs/12).

**S6 Benchmark on a real phone: deferred.** No phone is available yet; the emulator results are in docs/13. Run it
before Phase 7, on the slowest phone to be supported.

**Start-up (release build):** first screen 0.7–0.9 s after the process starts (1.8 s on the first launch after
install), measured by the app itself. On the same emulator under memory pressure (2 GB, heavily swapped) it took
6–7 s, so 2 GB phones are the ones to watch in Phase 7.
