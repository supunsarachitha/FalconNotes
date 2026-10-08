# Reference (read-only)

The reference implementation is the Maple Notes web app and server: <https://github.com/supunsarachitha/MapleNotes>, at release 1.15.0 (tag `v1.15.0`),
which Falcon Notes follows since its version 1.2.0. Port from that repository; never copy its source in here.

`maple-notes-1.8.0/` is what is left of the copy this project started from, commit
`a28db714a66a4021449abb98ad3bb1a5614ff1b0` (release 1.8.0), the version the specification in `docs/` was written
against:

- `docs/`: its architecture, threat model, end-to-end encryption specification and licensing policy, and the
  screenshots the design was matched against (`docs/06` of this project points at them).
- Its README, changelog, licence, third-party notices and build files.

Removed on 2026-10-08, at the owner's request, once the app had moved on to 1.15.0:

- `src/` and `tests/`, the source and its tests. Code comments and documents here that name a file under `web/`
  (`src/maple-web/src/`) or `server/` (`src/MapleNotes.Server/`) mean that path in the repository above.
- `scripts/`, the licence check, which this project ported to its own `scripts/`.

The licensing policy this project follows is kept in `docs/licensing.md` of this project. The web app's demo backups
are in `fixtures/demo-backups/`.
