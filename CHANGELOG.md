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
