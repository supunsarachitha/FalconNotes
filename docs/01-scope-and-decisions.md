# 1. Scope and decisions

## The product

**Falcon Notes** for Android, Windows and macOS: a fully offline app for quick notes, with the same features and look as
the Maple Notes web app version 1.8.0, under its own name and mark, minus everything that exists only because there is a
server. Notes stay on the device, encrypted. Backups are ZIP files in exactly the web app's export format, so notes move
freely between this app, another device running it, and any Maple Notes server.

The web app is the **reference implementation**. Its source, at the commit this plan was written against, is copied
into `reference/maple-notes-1.8.0/` of this repository (see the [README](../README.md)). When this documentation does not
say how something behaves, the reference does: port its behaviour, its copy text and its Tailwind classes.

## Decisions

| # | Decision | Why |
|---|---|---|
| D1 | **Platforms: Android (phones and tablets), Windows 10/11 (WinUI 3, MSIX), macOS (Mac Catalyst).** iOS is not planned, but nothing may rule it out. | The user's choice. Mac Catalyst and iOS share most code, so iOS stays cheap to add later. |
| D2 | **.NET 10 MAUI Blazor Hybrid.** The interface is Razor components inside a `BlazorWebView`. Every other part is C#. | The React components port almost line for line, with the same Tailwind classes, so the app looks the same. The server's C# (export, tag parsing, naming) is reused directly. |
| D3 | **Offline only.** No network access at all: no accounts, sign-in, sync, telemetry, link previews or remote content. One local profile per installation. | The user's requirement: never connect to a server. |
| D4 | **Encrypted storage, always on.** SQLite via SQLite3 Multiple Ciphers with the AEGIS-256 cipher. Attachment files use Maple Notes' chunked AES-256-GCM format. A random 256-bit device key is kept in the OS secure storage. There is no setting to turn encryption off. | The user's choice: "Encrypted + optional lock". The benchmark showed it costs almost nothing ([13](13-storage-benchmark.md)). |
| D5 | **Optional app lock**: a PIN, plus biometrics where available (Windows Hello, Android BiometricPrompt, Touch ID). It is a privacy screen, not a key: the data key does not depend on it. | Losing a PIN must never lose notes. See the [threat model](03-data-storage-and-security.md#threat-model). |
| D6 | **Backups are the web app's export ZIPs, byte for byte.** Manifest version 3 (version 2 until Falcon Notes 1.1.0; version 3 adds labels, as Maple Notes has written them since 1.9.0), Markdown/text/JSON, every folder layout. Restore accepts everything the web app's restore accepts. | The user's requirement: backups compatible with the web app. Proven by the shared export vectors ([05](05-backup-compatibility.md)). |
| D7 | **Same features as web 1.8.0, minus server-only ones** (tables below). | The user's requirement: same functions and design. |
| D8 | **Same copy text**, in English, with British spelling ("colour", "organise"), except where a server concept changes. New and changed text is spelled out in [07-screens.md](07-screens.md) and [08-help-guide.md](08-help-guide.md). | Consistency with the web app. |
| D9 | **SQL through `Microsoft.Data.Sqlite`, no EF Core.** Note text is kept in its own table. | EF Core's first query costs about 0.8 s on Android (about 4 s on a budget phone) at every launch ([13](13-storage-benchmark.md)). |
| D10 | **A separate repository**, starting at version **1.0.0**. The About line reads "Falcon Notes 1.0.0 · based on Maple Notes 1.8.0". | The user's choice. |
| D11 | **The same licence and dependency policy** as the web app: PolyForm Noncommercial 1.0.0 for our code, only permissive third-party licences, a `THIRD-PARTY-NOTICES.md` (see [02-architecture.md](02-architecture.md#dependencies)). | Continuity with `reference/maple-notes-1.8.0/docs/licensing.md`. |
| D12 | **The name is Falcon Notes, with its own mark** (decided 2026-10-01). Code uses `FalconNotes.*`, and the app ID is `lk.stechbuzz.falconnotes` ([12](12-platforms.md); changed from `dev.falconnotes.app` on 2026-10-07, before the first release, to sit under the owner's `lk.stechbuzz` name). The mark is an original falcon drawn for this app ([06](06-design-system.md#app-icon-and-splash)). The default accent is called **Falcon**: the web app's Maple colours, unchanged. Names users never see keep the reference's: the `maple-*` colour classes and the `MNAE` attachment header. Backups keep `"application": "Maple Notes"`, the format's name, which the web app checks ([05](05-backup-compatibility.md#manifest)). | The user's choice. Renaming only what users see keeps the port line for line and the backups compatible. |

## Features kept

Everything here behaves as in the web app, unless noted. The rules are in [04-domain-rules.md](04-domain-rules.md) and the
screens in [07-screens.md](07-screens.md).

| Area | Kept |
|---|---|
| Home | Quick-post composer, today's daily note (optional), pinned notes, the timeline with infinite scroll and "You're all caught up 🦅" |
| Notes | Markdown (GFM) with tickable task lists, `#tags` and nested tags, titles with optional date, edit inline (double-tap option, caret at the end), pin, labels, copy text, move between Home and Quick notes, archive and restore, trash with Undo, delete for good |
| Composer | Title field, formatting toolbar, Ctrl/⌘+B/I/K, Ctrl/⌘+Enter, Esc, tag suggestions, attach by picker, paste and drag-and-drop (desktop), upload progress, 100,000-character limit with counter |
| Attachments | Images (one keeps its shape, several form a grid), full-screen viewer with swipe, arrows and keys, video and audio players, file chips, shrink photos to 2,560 px JPEG (optional), lazy loading near the screen |
| Todo | Lists with items to tick, add, edit and remove, Edit as Markdown, rename, clear completed, pin, labels, archive, trash |
| Quick notes | Scratchpad tab, optional titles, move to Home |
| Daily notes | Today card titled with the date, saved on first words, one per day |
| Habits | Week view with ticks, earlier weeks, Progress chart (weeks and months, all habits or one, streaks), month Calendar, archived habits |
| Tags | Tags page with nested tree, filter, A–Z / Most used, tag filter on Home |
| Labels | Ten colours, create/rename/recolour/delete, picker with create, chips, side-menu list, label filter |
| Search | Text and attachment file names, active notes of enabled kinds |
| Calendar | Side-menu month calendar with dots, day filter |
| Archive, Trash | Archive page; Trash page from Settings with Restore, Delete forever, Empty trash, 30-day purge |
| Settings | Appearance (theme, accent, week start), Side menu (order, text size), Writing (titles, dates, tag suggestions, double-tap), Features (page and tool switches), Labels, Backup & data (export, restore, trash, delete all), storage use |
| Help | The user guide, adapted ([08-help-guide.md](08-help-guide.md)) |
| Layout | Phone layout with drawer; ≥1,024 px with fixed sidebar; light, dark and device themes; seven accents; keyboard shortcuts; accessibility (labels, focus, live regions) |

## Features dropped

| Web feature | Why | Replaced by |
|---|---|---|
| Sign-in, registration, setup, accounts, usernames | No server | A local profile with a display name, chosen on first run |
| Passwords, key-derived sign-in, sessions, "Sign out everywhere" | No server | Optional app lock |
| Encryption modes (Off, At rest, End-to-end), conversion, recovery keys, Unlock page | The server could read notes; here only the device can | Always-on device encryption (D4) |
| Administration: registration, accounts, roles, storage limits, app name and icon | No server, one user | Nothing. The app is always called Falcon Notes and shows the falcon mark (D12). |
| Link previews | They need a server to fetch pages, and the app is offline | Nothing; links stay plain links |
| Server export endpoint, browser export port, media service worker | Server and browser plumbing | One C# exporter in the app; a local media handler ([02](02-architecture.md#serving-attachments-to-the-webview)) |
| Storage quota, HTTP 507 handling | One user, one device | Storage usage is still shown, with no limit |
| "Needs a secure connection" page, CSP headers, rate limits, antiforgery, health checks, Docker | Web hosting concerns | A content security policy in the app's page ([02](02-architecture.md#webview-hardening)) |
| Version from the server | No server | `AppInfo.VersionString` |

## Features changed

| Area | Web 1.8.0 | This app |
|---|---|---|
| Name and mark | Maple Notes and the maple leaf, or the name and icon an administrator chose | **Falcon Notes** and the falcon mark; the default accent is called Falcon, with the same colours (D12) |
| First start | Sign-in / setup screen | **Welcome** screen: optional name, Start writing, or Restore from a backup |
| Side menu footer | Avatar, display name, `@username`, Sign out | Avatar, display name, "On this device"; a **Lock** button when the app lock is on |
| Settings → Account | Username, display name, role, member since, storage, Delete account | **Profile**: display name, storage, **Erase all data** |
| Settings → Privacy & security | Password, encryption, recovery key, sessions | **App lock** (PIN, biometrics, lock after), **Data protection** (what is encrypted, where), nothing else |
| Settings → Administration | Admin only | Removed |
| Settings → Features | Includes Link previews | Without Link previews |
| Delete all notes and files | Confirmed with the password | Confirmed in a dialog, and with the app lock when it is on |
| Export | Browser download | A save dialog (Android: the system's "save to" picker), with progress |
| Restore | Browser file input | The system file picker, several files at once |
| Attachments: download | Browser download | **Save a copy…** (save dialog) and **Open** (the system's default app, from a decrypted temporary copy deleted on next start) |
| Help | 22 sections | Adapted: server sections removed, App lock, Backups and Moving between devices added ([08](08-help-guide.md)) |

## Not in scope for 1.0 (proposals, need a decision)

| Proposal | Note |
|---|---|
| Labels in backups | **Done in 1.2.0** (2026-10-08, at the owner's request): manifest version 3, exactly as Maple Notes 1.9.0 defined it and 1.15.0 still writes it. See [05](05-backup-compatibility.md#labels-in-backups). |
| Settings in backups | Still open. The web app's format has no place for them, so this needs a new manifest version there first ([05](05-backup-compatibility.md#settings-in-backups)). |
| Backup reminder | A local app has no other copy of the notes. Proposal: Settings → Backup & data shows "Last export: …", and Home shows a dismissible reminder after 30 days without one. Off by default. |
| Share into Falcon Notes (Android share sheet, macOS Share menu) | Turns shared text, links and images into a new note. |
| iOS | D1 keeps it possible. |
| Full-text index (SQLite FTS5) | Only if search on 50,000+ notes proves too slow on real phones ([13](13-storage-benchmark.md)). |
| Sync | Out of scope: the app is offline by design. Moving between devices is by backup and restore. |
