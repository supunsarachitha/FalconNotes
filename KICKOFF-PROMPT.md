# Prompts for starting and continuing the work

Paste these into Claude Code, opened at the root of this repository (set up as the [README](README.md) describes).

## 1. Kickoff: Phase 0

```text
We are building Falcon Notes for Android, Windows and macOS in this repository: an offline .NET 10 MAUI Blazor Hybrid
port of the Maple Notes web app 1.8.0 under its own name, with the same features and design, encrypted local storage and
backups compatible with the web app. Nothing is built yet; the specification is complete.

1. Read CLAUDE.md, then every document in docs/ in order (01 to 13). Skim reference/maple-notes-1.8.0/ enough to know
   where things are: the React app in src/maple-web/src, the server in src/MapleNotes.Server. Look at
   fixtures/ and benchmarks/storage/ too.
2. Check the environment: dotnet --info, the MAUI workload, the Android SDK and emulators, Xcode for Mac Catalyst.
   Tell me what is missing before relying on it.
3. Give me a short plan for Phase 0 of docs/10-implementation-plan.md: the order of work, what each spike (S1–S6)
   will build to answer its question, and anything in the docs that is unclear, contradictory or that you think is
   wrong. Ask your questions now, then wait for my answers before writing code.
4. After I answer, do Phase 0 on a branch named phase-0:
   - the solution, projects, central package versions, Tailwind build and CI as specified;
   - the six spikes, each with its result appended to "Spike results" in docs/10 (date, platform versions, what
     worked, the decision), updating any doc whose assumption turned out wrong;
   - run on the Mac (Catalyst) and the Android emulator yourself; Windows runs in CI.
   Commit as you go with clear messages. Do not push unless I ask.
5. Stop at the end of Phase 0. Report: what was built, each spike's result and decision, any change to the docs,
   anything that needs me (a Windows machine, a real phone, signing accounts), and the plan for Phase 1.
```

## 2. Continue with the next phase

```text
Continue the Falcon Notes app. Re-read CLAUDE.md and docs/10-implementation-plan.md, check which phase is next
from its checklists and spike results, and re-read the docs that phase touches (and the reference files the port
map names for it).

Before coding, give me a short plan for the phase and any questions; wait for my answers if there are any.
Then work on a branch named phase-<n>. Port the reference tests with each feature, keep CHANGELOG.md and the phase
checklist up to date, and commit as you go. Do not push unless I ask.

Stop when the phase's acceptance criteria are met. Report what was built, test results (including the backup
conformance tests from Phase 2 on), anything I need to check by hand, and any doc you changed and why.
```

## 3. When something seems off

```text
Something in the app does not match the web app: <describe it, with a screenshot if you can>.
Find the reference implementation of this behaviour in reference/maple-notes-1.8.0/, compare it with ours, and fix
ours to match, with a test. If the docs deliberately differ from the reference here, show me where and ask before
changing anything.
```
