# Changelog

All notable changes to Falcon Notes are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Phase 0 skeleton, Android first: the solution (`FalconNotes.Core`, `FalconNotes.UI`, `FalconNotes.App`, and their
  test projects), central package versions, and an Android app (`dev.falconnotes.app`) that shows a page from the
  Razor class library in a `BlazorWebView`. It has no permissions and Android Auto Backup off.
- Tailwind CSS 4.3.3 build step: the pinned standalone CLI is downloaded once and checked against its published
  SHA-256, and the stylesheet is the web app's `index.css` with Falcon Notes additions at the end.
- `Button` and `Spinner`, ported from the web app with their classes, and their component tests.
- The encrypted database: SQLite3 Multiple Ciphers with AEGIS-256 pinned, a raw 256-bit key, and the app's pragmas.
  A wrong key is reported as `DatabaseKeyException`.
- Start-up check: the device key is created on first run and kept in Android's secure storage; a database whose key is
  missing or wrong shows a Key lost screen and is never touched.
- Attachment encryption ported from the Maple Notes server (the MNAE chunked AES-256-GCM format) and a media handler
  that serves decrypted attachments to the page with HTTP ranges, so large videos play and seek.
- The system file picker, a fast "save as" on Android, and opening files in the system's apps.
- CI on GitHub Actions: Core and UI tests on Linux, Windows and macOS, and an Android release build that fails if the
  app asks for a network permission.
- Core, everything but the screens: the text rules (titles, todo lists, habits and their streaks and charts, dates,
  relative times, Markdown editing, tag suggestions, the tag tree and menu order) with the web app's tests; keys
  derived from the device key; the database schema and migrations, with a copy kept before each upgrade; notes with
  every rule of the web app (lists, search, filters, pin, archive, trash and its 30-day purge, daily notes, moves,
  labels, delete all, storage use); labels, preferences and the profile; attachments added to the encrypted store;
  Markdown rendering; hourly maintenance. Search over 50,000 notes takes about 0.14 s.
- Backups in the Maple Notes web app's format: export to a ZIP of Markdown, plain text or JSON in any folder layout,
  with files, saved where you choose (`falcon-notes-{date}.zip`); restore from this app's, the web app's or a server's
  exports, or single Markdown, text and JSON files. Restoring the same backup twice changes nothing.
- The falcon mark, drawn and approved by the owner, with the Android adaptive icon and splash made from it.
- The app shell: a sidebar at 1,024 px and wider, a drawer below it, the search box, the side menu in the user's
  chosen order and size, the labels list and a month calendar, and the profile footer. `Icon`, `Logo`, `Toaster`,
  `ConfirmDialog` and `DropdownMenu`, ported with the web app's markup and classes. The theme, accent and native
  chrome (Android first) are applied before the first paint.
- Welcome (start writing, or restore from a backup) and Key lost (move the unreadable data aside and start again,
  with or without a restore), wired into start-up: no profile yet always opens Welcome, with the app shell held back
  from both screens.
- Notes: Home (today's note, pinned notes and the feed), the composer (titles with today's date suggestion, the
  format toolbar and its shortcuts, tag suggestions, attaching files through the native picker with progress),
  note cards (the actions menu, double-tap to edit, checkbox ticks saved in the background, labels), the attachment
  gallery and image viewer (Open and Save a copy), and the `?tag`, `?q`, `?day` and `?label` filters, Archive, Quick
  notes and Trash. "Shrink photos" and paste/drag-and-drop for attachments are not yet wired up (the native file
  picker covers every platform meanwhile); Share on Android is left for the platform pass.
- The clipboard, for a note's "Copy text".
- The Todo tab: a form to start a new list, `TodoCard` (tick, add, edit and remove items; edit them all at once as
  Markdown; rename, label, pin, archive or delete the list) and `ItemsEditor`, ported with the web app's markup,
  shortcuts and tests.
- The Habits tab: a form to start a new habit, `HabitRow` (tick a day, rename, archive or delete), `ArchivedHabitRow`,
  `HabitChart` (the share of days done per week or month, with streaks for one habit) and `HabitCalendar` (a month at
  a glance), with the week header and the collapsed Archived habits section, ported with the web app's markup and
  tests.
- The Tags page: every tag with how many notes use it, nested under their parents, a filter and the A–Z/Most used
  orders (`TagBranch`, `Core/Text/TagTree.cs`). Settings → Labels (`LabelSettings`, `LabelRow`, `LabelColorPicker`):
  turning labels on or off, and creating, renaming, recolouring and deleting the account's labels. The side-menu
  calendar and labels list, and the `?label=` filter on Home, were already wired in from earlier phases.
- The Settings page shell (`Settings.razor`), with every section: Profile (display name, storage use, Erase all
  data), Appearance, Side menu (order by arrows, text size — pointer drag is left for a later pass), Writing and
  Editing, Features (without Link previews; "Shrink photos before adding"), Labels, Backup & data (`ExportService`
  and `RestoreReader`/`RestoreRunner` are now wired into the app, which Welcome's restore flow already depended on
  but had never been registered), and Privacy & security.
- The app lock (docs/03): a PIN (PBKDF2-HMAC-SHA256, 210,000 iterations, a growing lockout after five wrong tries),
  the Lock screen (shown before the router itself mounts, so nothing can navigate around it), the sidebar's Lock
  button, the background timer (Android `Window.Stopped`/`Resumed`), and `FLAG_SECURE` while the lock is on, which
  also blanks the app from the recent-apps thumbnail. Biometric unlock is stubbed off for now: AndroidX
  `BiometricPrompt` is not yet an approved dependency (docs/02), the same open question as `SkiaSharp` for photo
  shrinking.
- Erase all data (Settings → Profile): deletes the database, attachments, database copies and cache, and forgets the
  device key, then returns to Welcome.
- Help: the adapted user guide (`UI/Help/Guide.cs`, docs/08), with a contents list and every section.
- Share on Android: the image viewer's Share button hands a decrypted copy of the image to the system share sheet
  (`IShare`, `AndroidShare`), the same `cache/open/` mechanism Open already uses.
- `scripts/check-licenses.py`, ported from the Maple Notes server, checks every NuGet package the app ships or
  builds with against the licence policy and regenerates `THIRD-PARTY-NOTICES.md` from the exact dependency
  versions; it also added the Lucide icon licence that file had been missing. `WindowSoftInputMode = AdjustResize`
  on `MainActivity`, so the composer and dialogs stay above the keyboard.

### Changed

- The falcon mark (`fixtures/falcon-mark.png`, docs/06) replaced with a new feather design, as a transparent PNG
  rather than a hand-drawn SVG: the Android adaptive icon, splash screen and in-app `Logo` (now an `<img>`, not
  inline paths) are all generated from it.

### Fixed

- Turning note titles on or off now updates the notes already on screen. Before, a note could show its title twice,
  or lose it, until the app was restarted.
- Settings → Appearance: choosing a theme or an accent colour, and the device switching between light and dark,
  now change the app at once. Before, the appearance was set only once, before the saved settings had loaded.
- `Placeholders.razor` kept `@page` routes for Todo, Habits and Tags after Phase 5 ported real pages for them, which
  made Blazor's router throw "ambiguous routes" on start-up and left the WebView blank.
