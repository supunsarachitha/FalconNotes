# Reference implementation (read-only)

`maple-notes-1.8.0/` is the Maple Notes web app and server at commit `a28db714a66a4021449abb98ad3bb1a5614ff1b0` (release 1.8.0), the version the
specification in `docs/` was written against. Port from it; never edit, build or ship it, and never reference it from a
project. Its demo backups are in `fixtures/demo-backups/`.

Its `scripts/` folder (the licence check and its notice texts) was removed on 2026-10-08, at the owner's request: this
project has its own port in `scripts/`. Everything else is as in that commit.

The app has since moved on to Maple Notes 1.15.0 (Falcon Notes 1.2.0): what it took from the releases after 1.8.0
was ported from the Maple Notes repository itself, <https://github.com/supunsarachitha/MapleNotes>, and is not in this
folder. Where the two differ, the newer release is what the app follows (`docs/01`, D7; `docs/05` for backups).
