# Maple Notes for Android, Windows and macOS

An offline app for quick notes, todo lists and habits, with the same features and look as the
[Maple Notes](https://github.com/supunsarachitha/MapleNotes) web app 1.8.0. It runs without a server: notes stay on the
device, encrypted. Backups are the web app's export ZIPs, so notes move freely between this app, other devices and any
Maple Notes server.

Built with .NET 10 MAUI Blazor Hybrid. **Status: specification. No code yet.**

## The specification

Read in this order. AI agents: start with [CLAUDE.md](CLAUDE.md).

| Document | What it settles |
|---|---|
| [01 Scope and decisions](docs/01-scope-and-decisions.md) | What the app is, the decisions taken, and the features kept, dropped and changed |
| [02 Architecture](docs/02-architecture.md) | Projects, runtime model, routes, data access, media, WebView hardening, JS interop, platform services, dependencies |
| [03 Data, storage and security](docs/03-data-storage-and-security.md) | Files, keys, schema, attachment store, app lock, threat model |
| [04 Domain rules](docs/04-domain-rules.md) | Every behaviour rule, with the reference file that defines it |
| [05 Backup compatibility](docs/05-backup-compatibility.md) | The export and restore contract with the web app |
| [06 Design system](docs/06-design-system.md) | Tokens, colours, layout, components, icons, theming |
| [07 Screens](docs/07-screens.md) | Each screen: the reference to port, and the exact text of new and changed screens |
| [08 Help guide](docs/08-help-guide.md) | The in-app user guide, adapted |
| [09 Port map](docs/09-port-map.md) | Every reference file and where it goes |
| [10 Implementation plan](docs/10-implementation-plan.md) | Phases, spikes, checklists and acceptance criteria |
| [11 Testing](docs/11-testing.md) | Test projects, tests to port, backup conformance, performance budgets, manual QA |
| [12 Platforms](docs/12-platforms.md) | Android, Windows and macOS specifics |
| [13 Storage benchmark](docs/13-storage-benchmark.md) | Why SQLite with AEGIS encryption, measured on Android |

Also here:

- `fixtures/`: `export-vectors.json` (the shared export vectors: 13 reference exports of an awkward dataset), two demo
  backups (35 notes, 8 files), and the maple leaf artwork.
- `benchmarks/storage/`: the storage benchmark source (desktop and Android) and its raw results.
- `reference/maple-notes-1.8.0/`: the web app at the commit this plan was written against. It is the reference
  implementation: read-only, never built, never shipped.

## Where this came from

This folder was written on the `maui-offline-plan` branch of the Maple Notes repository (`maui-handoff/`), against
commit `a28db714a66a4021449abb98ad3bb1a5614ff1b0` (release 1.8.0). To set up the new repository from it:

```sh
# in the Maple Notes repository, on the maui-offline-plan branch
maui-handoff/scripts/export-handoff.sh /path/to/new-repo
```

The script copies `CLAUDE.md`, `README.md`, `docs/`, `fixtures/` and `benchmarks/` into the new repository. It writes
the reference source into `reference/maple-notes-1.8.0/` from that exact commit (with `git archive`, so uncommitted
changes are never included) and adds a starter `.gitignore` and `.editorconfig`. It refuses to overwrite existing
files.

Then open the new repository with Claude Code and paste the prompt from [KICKOFF-PROMPT.md](KICKOFF-PROMPT.md).
