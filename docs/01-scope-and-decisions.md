# 1. Scope and decisions

## The product

**Falcon Notes** for Android, Windows and macOS: a fully offline app for quick notes, with the same features and look as
the Maple Notes web app version 1.16.0 (1.8.0 until Falcon Notes 1.1.0, 1.15.0 in 1.2.0), under its own name and mark, minus everything that exists only because there is a
server. Notes stay on the device, encrypted. Backups are ZIP files in exactly the web app's export format, so notes move
freely between this app, another device running it, and any Maple Notes server.

The web app is the **reference implementation**: the [Maple Notes repository](https://github.com/supunsarachitha/MapleNotes) at release 1.16.0 (tag
`v1.16.0`). When this documentation does not say how something behaves, the reference does: port its behaviour, its
copy text and its Tailwind classes. Until 2026-10-08 a copy of its 1.8.0 source, which this plan was written against,
sat in `reference/maple-notes-1.8.0/`; the source and tests were removed once the app had moved on to 1.15.0, and
what is left there is that release's documents and screenshots (see the [README](../README.md)).

## Decisions

| # | Decision | Why |
|---|---|---|
| D1 | **Platforms: Android (phones and tablets), Windows 10/11 (WinUI 3, MSIX), macOS (Mac Catalyst).** iOS is not planned, but nothing may rule it out. | The user's choice. Mac Catalyst and iOS share most code, so iOS stays cheap to add later. |
| D2 | **.NET 10 MAUI Blazor Hybrid.** The interface is Razor components inside a `BlazorWebView`. Every other part is C#. | The React components port almost line for line, with the same Tailwind classes, so the app looks the same. The server's C# (export, tag parsing, naming) is reused directly. |
| D3 | **Offline only.** No network access at all: no accounts, sign-in, sync, telemetry, link previews or remote content. One local profile per installation. | The user's requirement: never connect to a server. |
| D4 | **Encrypted storage, always on.** SQLite via SQLite3 Multiple Ciphers with the AEGIS-256 cipher. Attachment files use Maple Notes' chunked AES-256-GCM format. A random 256-bit device key is kept in the OS secure storage. There is no setting to turn encryption off. | The user's choice: "Encrypted + optional lock". The benchmark showed it costs almost nothing ([13](13-storage-benchmark.md)). |
| D5 | **Optional app lock**: a PIN, plus biometrics where available (Windows Hello, Android BiometricPrompt, Touch ID). It is a privacy screen, not a key: the data key does not depend on it. | Losing a PIN must never lose notes. See the [threat model](03-data-storage-and-security.md#threat-model). |
| D6 | **Backups are the web app's export ZIPs, byte for byte.** Manifest version 3 (version 2 until Falcon Notes 1.1.0; version 3 adds labels, as Maple Notes has written them since 1.9.0), Markdown/text/JSON, every folder layout. Restore accepts everything the web app's restore accepts. A note's mood (Falcon Notes 1.7.0) is one more line in its file, written only for a note that has one, which Maple Notes and earlier versions of this app pass over; the owner is adding moods to Maple Notes next ([05](05-backup-compatibility.md#moods-in-backups)). A sticker (1.7.0) is an ordinary picture among the note's files, with one more list saying which files are stickers, which Maple Notes passes over and shows as pictures ([05](05-backup-compatibility.md#stickers-in-backups)). | The user's requirement: backups compatible with the web app. Proven by the shared export vectors ([05](05-backup-compatibility.md)). |
| D7 | **Same features as web 1.15.0, minus server-only ones** (tables below). Written against 1.8.0; brought up to 1.15.0 in Falcon Notes 1.2.0 (2026-10-08, the owner's decision: "we are supporting latest"). | The user's requirement: same functions and design. |
| D8 | **Same copy text**, in English, with British spelling ("colour", "organise"), except where a server concept changes. New and changed text is spelled out in [07-screens.md](07-screens.md) and [08-help-guide.md](08-help-guide.md). | Consistency with the web app. |
| D9 | **SQL through `Microsoft.Data.Sqlite`, no EF Core.** Note text is kept in its own table. | EF Core's first query costs about 0.8 s on Android (about 4 s on a budget phone) at every launch ([13](13-storage-benchmark.md)). |
| D10 | **A separate repository**, starting at version **1.0.0**. The About line reads "Falcon Notes {version}" and, under it, "Based on Maple Notes 1.15.0" (1.8.0 until Falcon Notes 1.1.0). | The user's choice. |
| D11 | **The same licence and dependency policy** as the web app: PolyForm Noncommercial 1.0.0 for our code, only permissive third-party licences, a `THIRD-PARTY-NOTICES.md` (see [02-architecture.md](02-architecture.md#dependencies)). | Continuity with the web app's policy, kept here as [licensing.md](licensing.md). |
| D12 | **The name is Falcon Notes, with its own mark** (decided 2026-10-01). Code uses `FalconNotes.*`, and the app ID is `lk.stechbuzz.falconnotes` ([12](12-platforms.md); changed from `dev.falconnotes.app` on 2026-10-07, before the first release, to sit under the owner's `lk.stechbuzz` name). The mark is an original falcon drawn for this app ([06](06-design-system.md#app-icon-and-splash)). The default accent is called **Falcon**: the web app's Maple colours, unchanged. Names users never see keep the reference's: the `maple-*` colour classes and the `MNAE` attachment header. Backups keep `"application": "Maple Notes"`, the format's name, which the web app checks ([05](05-backup-compatibility.md#manifest)). | The user's choice. Renaming only what users see keeps the port line for line and the backups compatible. |

## Features kept

Everything here behaves as in the web app, unless noted. The rules are in [04-domain-rules.md](04-domain-rules.md) and the
screens in [07-screens.md](07-screens.md).

| Area | Kept |
|---|---|
| Home | Quick-post composer, today's daily note (optional), pinned notes, the timeline with infinite scroll and "You're all caught up 🦅" |
| Notes | Markdown (GFM) with tickable task lists, `#tags` and nested tags, titles with optional date, edit inline (double-tap option, caret at the end), pin, labels, copy text, move between Home and Quick notes, archive and restore, trash with Undo, delete for good |
| Composer | Title field, formatting toolbar, Ctrl/⌘+B/I/K, Ctrl/⌘+Enter, Esc, tag suggestions, attach by picker, paste and drag-and-drop (desktop), upload progress, 100,000-character limit with counter |
| Attachments | Images (one keeps its shape, several form a grid), full-screen viewer with swipe, arrows and keys, video and audio players, file chips, shrink photos to JPEG in one of three sizes (optional), lazy loading near the screen |
| Todo | Lists with items to tick, add, edit and remove, Edit as Markdown, rename, clear completed, pin, labels, archive, trash |
| Quick notes | Scratchpad tab, optional titles, move to Home |
| Daily notes | Today card titled with the date, saved on first words, one per day; any note can be the template each new day starts with |
| Habits | Week view with ticks, earlier weeks, Progress chart (weeks and months, all habits or one, streaks), month Calendar, archived habits |
| Tags | Tags page with nested tree, filter, A–Z / Most used, tag filter on Home |
| Labels | Ten colours, create/rename/recolour/delete, picker with create, chips, side-menu list, label filter |
| Search | Text and attachment file names, active notes of enabled kinds |
| Calendar | Side-menu month calendar with dots, day filter |
| Archive, Trash | Archive page; Trash page from Settings with Restore, Delete forever, Empty trash, 30-day purge |
| Settings | Appearance (theme, accent, background, week start), Side menu (order, text size), Writing (titles, dates, tag suggestions, double-tap), Features (page and tool switches), Labels, Backup & data (export, restore, automatic backups, trash, delete all), storage use |
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
| Two-factor sign-in, session length (web 1.15) | No sign-in | The app lock |
| Offline reading and writing, the Online/Offline badge (web 1.12, 1.15) | The app is always offline, and its notes are always on the device | Nothing needed |
| Install as an app (web 1.11) | It is an app | Nothing needed |
| Server export endpoint, browser export port, media service worker | Server and browser plumbing | One C# exporter in the app; a local media handler ([02](02-architecture.md#serving-attachments-to-the-webview)) |
| Storage quota, HTTP 507 handling | One user, one device | Storage usage is still shown, with no limit |
| "Needs a secure connection" page, CSP headers, rate limits, antiforgery, health checks, Docker | Web hosting concerns | A content security policy in the app's page ([02](02-architecture.md#webview-hardening)) |
| Version from the server | No server | `AppInfo.VersionString` |

## Features changed

| Area | Web 1.15.0 | This app |
|---|---|---|
| Name and mark | Maple Notes and the maple leaf, or the name and icon an administrator chose | **Falcon Notes** and the falcon mark; the default accent is called Falcon, with the same colours (D12) |
| First start | Sign-in / setup screen | **Welcome** screen: optional name, Start writing, or Restore from a backup |
| Side menu footer | Avatar, display name, `@username`, Sign out | Avatar, display name, "On this device"; a **Lock** button when the app lock is on |
| Settings → Account | Username, display name, role, member since, storage, Delete account | **Profile**: display name, storage, **Erase all data** |
| Settings → Privacy & security | Password, encryption, recovery key, sessions | **App lock** (PIN, biometrics, lock after), **Data protection** (what is encrypted, where), nothing else |
| Settings → Administration | Admin only | Removed |
| Settings → Features | Includes Link previews | Without Link previews |
| Help in the menu, turned off | The guide stays at its address, `/help` | The guide is opened from a **Help** link at the foot of Settings |
| Delete all notes and files | Confirmed with the password | Confirmed in a dialog, and with the app lock when it is on |
| Export | Browser download | A save dialog (Android: the system's "save to" picker), with progress |
| Appearance | Theme and accent colour | The same, plus a **Background**: None, or one of nine festival patterns drawn for this app, off by default (2026-10-08, at the owner's request; [06](06-design-system.md#backgrounds)) |
| Automatic backups | None: the server keeps the notes, and its administrator backs it up | **Automatic backups** to a folder the user chooses, off by default ([05](05-backup-compatibility.md#automatic-backups)) |
| Locked notes | None: a note is as private as the account | With **Note lock** turned on in Settings → Privacy & security (off by default), **Lock** in a note's ⋯ menu hides that note; the app lock's PIN opens it to be read, and it locks again by itself (2026-10-12, at the owner's request; [04](04-domain-rules.md#locked-notes)) |
| Export to PDF | None | With **Export to PDF** turned on in Settings → Features (off by default), **Export to PDF…** in the ⋯ menu of a note, quick note or todo list saves that one note as a PDF, with its pictures, where the user chooses on the device (2026-10-13, at the owner's request; [04](04-domain-rules.md#export-to-pdf)). Android first, like every platform feature. |
| Line breaks in a note | A single line break joins its lines into one paragraph, as Markdown reads it | The same as installed. With **Keep line breaks** turned on in Settings → Writing (off by default), every line is shown on its own line, on the screen and in a note's PDF (1.6.0, 2026-10-13, at the owner's request; [04](04-domain-rules.md#markdown-rendering)). The note's text is the same either way, and so are backups. |
| Moods | None | With **Mood tracker** turned on in Settings → Features (off by default), a smiley button where a note or quick note is written offers five smilies, from awful to great, and the one chosen shows on the note after its time (1.7.0, 2026-10-13, at the owner's request; [04](04-domain-rules.md#moods)). Backups keep it ([05](05-backup-compatibility.md#moods-in-backups)). |
| Stickers | None | With **Stickers** turned on in Settings → Features (off by default), sticker packs, ZIP files of pictures, are added and removed there, and a sticker button where a note or quick note is written puts any number of their stickers on it, shown small above its other files (1.7.0, 2026-10-14, at the owner's request; [04](04-domain-rules.md#stickers)). Backups keep them ([05](05-backup-compatibility.md#stickers-in-backups)). |
| Restore | Browser file input | The system file picker, several files at once |
| Attachments: download | Browser download | **Save a copy…** (save dialog) and **Open** (the system's default app, from a decrypted temporary copy deleted on next start) |
| Help | 22 sections | Adapted: server sections removed, App lock, Locked notes, Backups and Moving between devices added ([08](08-help-guide.md)) |

## Not in scope for 1.0 (proposals, need a decision)

| Proposal | Note |
|---|---|
| Labels in backups | **Done in 1.2.0** (2026-10-08, at the owner's request): manifest version 3, exactly as Maple Notes 1.9.0 defined it and 1.15.0 still writes it. See [05](05-backup-compatibility.md#labels-in-backups). |
| Settings in backups | Still open. The web app's format has no place for them, so this needs a new manifest version there first ([05](05-backup-compatibility.md#settings-in-backups)). |
| Automatic backups to a chosen folder | **Done** (2026-10-08, at the owner's request): Settings → Backup & data → Automatic backups, off by default because the backups are not encrypted. The archive is the ordinary export, so the format is untouched. See [05](05-backup-compatibility.md#automatic-backups). |
| Festival backgrounds | **Done** (2026-10-08, at the owner's request, after trying a Halloween sample on the emulator): Settings → Appearance → Background. This app's own; Maple Notes has none. See [06](06-design-system.md#backgrounds). Themes that also change the colours and icons, and themes from a file, were discussed and not built. |
| Locked notes | **Done in 1.4.0** (2026-10-12, at the owner's request): **Lock** and **Unlock…** in the ⋯ menu of a note, quick note or todo list. This app's own; Maple Notes has none. The owner chose a lock that hides the note behind the app lock's PIN, over a read-only lock and over one with a temporary "Show"; and that the first lock asks for a PIN, without turning the app lock on. Later the same day the owner asked for two more things: a switch for it in Privacy & security, **off by default**; and that a note unlocked to be read **locks again by itself**, so unlocking no longer takes the lock off (leaving the page or the app locks it; taking the lock off for good is **Remove lock**). Like the app lock it is a privacy screen and not a key (D5), and it is not in backups, whose format has no place for it (D6). See [04](04-domain-rules.md#locked-notes) and [07](07-screens.md#locked-notes). |
| Export a note to PDF | **Done in 1.5.0** (2026-10-13, at the owner's request: a switch in Settings, off by default; with it on, each note can be exported to a PDF saved on the device). This app's own; Maple Notes has none. Decided while building: the switch is with the other features, in Settings → Features; the PDF is drawn by the platform's own PDF and text drawing, so the app gains **no library** and every script the device shows comes out right; it holds the note's text as the screen formats it, when it was written and its pictures, and names the files a page cannot show; it is saved through the system's "save to" dialog, like an export, so no storage permission is needed; a locked note can be exported only while it is open to be read; it is not a backup and cannot be restored, and it is not encrypted, which the switch says. See [04](04-domain-rules.md#export-to-pdf) and [07](07-screens.md#export-to-pdf). |
| Lines typed one under another | **Done in 1.6.0** (2026-10-13, at the owner's request, who found that lines typed one under another came out as a single line once the note was saved). That is Markdown's rule and the web app's, not a fault of the port, and Obsidian, Joplin and Bear show such lines apart. The owner chose a switch over changing the rule outright: **Keep line breaks** in Settings → Writing, **off by default**, so a note looks as it does in Maple Notes until the user chooses otherwise. It changes only how a note is shown and exported to PDF: no note is rewritten, so turning it off puts everything back, and a backup restored into the web app shows the lines joined. Help's own text is never affected. See [04](04-domain-rules.md#markdown-rendering). |
| Mood tracker | **Done in 1.7.0** (2026-10-13, at the owner's request: "a new feature for mood tracker using smilies"). This app's own for now; Maple Notes has none, and the owner is adding the same option there next. Asked how it should work, the owner chose that the mood is **shown in the note**, not kept as a log of days with a page and a chart of its own; that the smilies are **emoji**, over faces drawn in the app's icon style; and that **backups carry it**, since Maple Notes will read it. Decided while building: five moods, from awful to great; a mood belongs to a note or a quick note (a daily note too), never to a todo list or a habit; the switch is with the other features, in Settings → Features, **off by default**; the mood is chosen where the note is written and changed by editing it; a change of mood alone does not make a note "edited"; a locked note hides it. It is kept in a new column (migration 4) and written to backups as one optional line, so a note without a mood is exported byte for byte as before and the shared vectors still pass. See [04](04-domain-rules.md#moods), [05](05-backup-compatibility.md#moods-in-backups) and [07](07-screens.md#moods). |
| Stickers | **Done in 1.7.0** (2026-10-14, at the owner's request: "add stickers to notes. should be able to download/ remove new sticker packs"). This app's own; Maple Notes has none. Scoped first, then the owner chose: packs come as **ZIP files added from the system file picker**, over an in-app store, which would have ended D3 (network access, a hosted catalogue, a new privacy story); a note can have **several** stickers; **backups carry them**, as ordinary pictures plus one list naming which are stickers; and a **switch in Settings → Features, off by default**. Decided while building: a sticker is copied into the note's encrypted files, so removing a pack never changes a note and a backup needs no pack; only PNG, JPEG, GIF and WebP, checked by their bytes, at most 2 MB each and 300 a pack; an optional `sticker-pack.json` names the pack and orders it, and a pack with the same ID updates it; packs are kept unencrypted and are not in backups; with the switch off, stickers on notes still show. No starter pack ships: artwork needs a licence the owner has chosen (D11). See [04](04-domain-rules.md#stickers), [05](05-backup-compatibility.md#stickers-in-backups) and [07](07-screens.md#stickers). |
| Backup reminder | A local app has no other copy of the notes. Proposal: Settings → Backup & data shows "Last export: …", and Home shows a dismissible reminder after 30 days without one. Off by default. |
| Share into Falcon Notes (Android share sheet, macOS Share menu) | Turns shared text, links and images into a new note. |
| iOS | D1 keeps it possible. |
| Full-text index (SQLite FTS5) | Only if search on 50,000+ notes proves too slow on real phones ([13](13-storage-benchmark.md)). |
| Sync | Out of scope: the app is offline by design. Moving between devices is by backup and restore. |
