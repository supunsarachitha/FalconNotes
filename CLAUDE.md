# CLAUDE.md

Falcon Notes is an **offline** .NET 10 MAUI Blazor Hybrid app for Android, Windows and macOS: a port of the Maple
Notes web app 1.8.0 under its own name and mark. It has the same features and look, no server, encrypted local
storage, and backups compatible with the web app.

## Read first

The specification is in `docs/`. Read **all of it, in order (01 → 13)**, before your first change in a session that
starts a new phase. Afterwards, re-read the documents your task touches. The [README](README.md) lists what each one
settles.

The web app is the **reference implementation**, in `reference/maple-notes-1.8.0/`. When the docs do not say how
something behaves, the reference does. [docs/09-port-map.md](docs/09-port-map.md) says where every reference file goes.

## Ground rules

1. **The reference is read-only.** Never edit, build, ship or reference it from a project. Read it and port from it.
2. **Port, do not redesign.** Keep the React components' markup, Tailwind classes, copy text, ARIA and keyboard
   behaviour. Change only what [07-screens.md](docs/07-screens.md) and [08-help-guide.md](docs/08-help-guide.md) say to change.
   Spelling is British, as in the reference ("colour", "organise"). Where ported text names the app, it says "Falcon
   Notes"; "Maple Notes" stays only where the text means the web app, its servers or its backup format (D12 in
   [01](docs/01-scope-and-decisions.md)).
3. **Backups are a contract** ([05](docs/05-backup-compatibility.md)). The exporter is copied from the server's C#. The
   conformance tests against `fixtures/export-vectors.json` must always pass. Never change the format here alone.
4. **Offline means offline.** No network calls, no `INTERNET` permission on Android, no remote content in the WebView,
   no telemetry, no update checks, no CDN.
5. **Encryption is always on**: SQLite3MC with AEGIS-256, the MNAE attachment format, and the device key in
   `ISecretStore` ([03](docs/03-data-storage-and-security.md)). Never add a way to turn it off. Never write note text,
   file names, label names or keys to logs or to unencrypted files, except exports and the documented
   temporary copies.
6. **No EF Core and no ORM.** Use `Microsoft.Data.Sqlite` with hand-written, parameterised SQL in repositories. Note text
   stays in `NoteBodies` ([13](docs/13-storage-benchmark.md)).
7. **Never block the UI thread.** Components render on the UI thread. Repositories run SQL inside `Task.Run`, and
   components only `await` services.
8. **Core and UI never reference MAUI.** Platform features go through the interfaces in `FalconNotes.Core/Platform/`.
9. **Port the tests with the code** ([11](docs/11-testing.md)). A rule is done when its reference tests pass here.
10. **Dependencies**: only those in [02 → Dependencies](docs/02-architecture.md#dependencies). Anything new needs an
    allowed licence (see `reference/maple-notes-1.8.0/docs/licensing.md`), an entry in `THIRD-PARTY-NOTICES.md`, and my
    approval first.
11. **Ask me before** you change the backup format, a decision in [01](docs/01-scope-and-decisions.md), the schema of a
    released version without a migration, or the copy of a screen beyond what the docs say. Also ask before adding a
    feature that is not in the docs, or dropping one that is. Record every agreed change in the relevant doc in the
    same commit.

## Working style

- Work phase by phase as in [10-implementation-plan.md](docs/10-implementation-plan.md). Tick its checklists as items are
  done, and append each spike's result under "Spike results".
- One feature per commit, with its tests. Keep `CHANGELOG.md` (Keep a Changelog) under `[Unreleased]`.
- Write code like the reference's C#: file-scoped namespaces, XML documentation comments on public members explaining
  *why*, small focused services, no clever abstractions. Comments say what the code cannot.
- The development machine is a Mac (Apple silicon) with the .NET 10 MAUI workload and Android emulators. Windows builds
  and tests run in CI. Say when something needs a test on a Windows machine or a real phone.

## Commands

```sh
dotnet build                                                                   # everything the OS can build
dotnet test tests/FalconNotes.Core.Tests                                        # rules, storage, crypto, backups
dotnet test tests/FalconNotes.UI.Tests                                          # components (bUnit)
dotnet build src/FalconNotes.App -t:Run -f net10.0-maccatalyst                   # run on this Mac
dotnet build src/FalconNotes.App -t:Run -f net10.0-android                       # run on the running emulator/device
dotnet build src/FalconNotes.App -t:Run -f net10.0-windows10.0.19041.0           # on Windows
~/Library/Android/sdk/emulator/emulator -list-avds                             # emulators: MAUI_Emulator_API_36, Medium_Phone_API_36.1
```

Update this section when the commands change.
