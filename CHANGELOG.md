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
