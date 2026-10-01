# 13. Storage benchmark and decision

**Decision (2026-10-01): keep SQLite, keep encryption.** Use SQLite3 Multiple Ciphers with the **AEGIS-256** cipher,
keep note text in its own table, and query with plain `Microsoft.Data.Sqlite`, without EF Core. A folder of `.md` files
was measured and rejected.

The question was whether SQLite is fast enough on mobile devices, and if not, whether to store notes as a folder of
Markdown files without encryption. This page records what was measured and why the decision went the way it did.
The benchmark source and raw results are in [`../benchmarks/storage/`](../benchmarks/storage/), so it can be run again
on real devices.

## What was measured

Three storage designs, holding the same generated notes:

| Variant | What it is |
|---|---|
| `sqlite` | SQLite (the SQLite3MC build, no key), WAL, `synchronous=NORMAL`, `secure_delete=ON` |
| `sqlite-sqlcipher` | The same, encrypted in the SQLCipher v4 format the server uses (AES-256-CBC + HMAC-SHA512) |
| `sqlite-chacha20` | The same, encrypted with ChaCha20-Poly1305 (SQLite3MC's default cipher) |
| `sqlite-aegis` | The same, encrypted with AEGIS-256 (an AES-based AEAD that uses the CPU's AES instructions) |
| `md-folder` | One `.md` file per note (front matter + text) in `yyyy/MM/` folders, not encrypted, with an in-memory index built by reading every file at start-up |
| `ef-core` | EF Core 10 on top of `sqlite-sqlcipher`, to measure its start-up cost |

Data, generated deterministically (`Random(42)`): 1,000, 10,000 and 50,000 notes over three years. 60% are 40–200
characters, 30% are 200–1,000 and 10% are 1,000–5,000. 8% are todo lists, 7% quick notes, 12 are habits with
about 180 days each, 10% are archived, 2% are in the trash and 5% are daily notes. 30% have one to three attachment
rows, 20% have a label, and tags come from a pool of 50, nested ones included. 50,000 notes is about 20 notes a day
for seven years: a heavy user.

The operations are the queries the app's screens make (see [04-domain-rules.md](04-domain-rules.md)): a feed page with
its tags, labels and attachments, the pinned list, tag and label counts, calendar counts, tag and label filters, the
Habits page, a search that matches nothing (the worst case, since it reads every note), and single writes in their
own transaction. Each operation runs once (first) and then ten times; the tables show the median.

### Schema used

The first run used the server's layout, with note text inside the `Notes` table. Encrypted, every query that
touched note metadata had to decrypt pages full of note text. On the Mac at 50,000 notes, SQLCipher tag counts took
286 ms ([results](../benchmarks/storage/results/mac-m1pro-schema-v1.txt)). The final schema keeps the text in a
separate `NoteBodies` table, so lists, counts and filters read only small metadata rows. All results below use the
final schema ([03-data-storage-and-security.md](03-data-storage-and-security.md#schema)).

## Machines

| Where | Details |
|---|---|
| Android emulator | Android 16 (API 36.1), arm64-v8a, **1 CPU core, 2 GB RAM**, .NET 10.0.8, Release build with profiled AOT, on an Apple M1 Pro host |
| Mac | Apple M1 Pro, macOS, .NET 10, Release |

The emulator runs at roughly the speed of one M1 Pro core: a flagship phone. A budget phone's single core is about
**3–5× slower** (Geekbench 6 single-core of roughly 450–750, against about 2,400). The "budget phone" column below is
the emulator × 5, which is deliberately pessimistic. Phone flash storage is also slower than the host's SSD, which
hurts the `.md` folder most: it reads tens of thousands of small files at every start.

## Results: Android emulator

All times in milliseconds, medians. Raw output:
[android-emulator-api36-1core.txt](../benchmarks/storage/results/android-emulator-api36-1core.txt).

### 10,000 notes

| Operation | sqlite | sqlcipher | chacha20 | **aegis** | md-folder |
|---|---|---|---|---|---|
| Open + first feed page (cold) | 1 | 1 | 1 | **1** | **305** (reads every file) |
| Feed: first page | 0.19 | 0.23 | 0.20 | **0.22** | 0.00 |
| Feed: page after scrolling 1,000 notes | 0.21 | 0.25 | 0.22 | **0.25** | 0.10 |
| Pinned list | 0.19 | 0.21 | 0.18 | **0.22** | |
| Tag counts | 8.2 | 14.7 | 7.5 | **9.0** | 4.7 |
| Label counts | 2.7 | 6.7 | 2.4 | **2.7** | |
| Calendar month counts | 0.54 | 0.61 | 0.54 | **0.58** | |
| Tag filter `#work` (nested): first page | 9.4 | 29.8 | 22.9 | **10.4** | |
| Label filter: first page | 3.7 | 11.6 | 9.6 | **4.4** | |
| Habits page | 0.16 | 0.18 | 0.16 | **0.28** | |
| Search with no match (reads every note) | 27 | 55 | 47 | **31** | 2.5 |
| Post a note (insert + tags) | 0.09 | 0.34 | 0.28 | **0.12** | 0.16 |
| Toggle pin | 0.03 | 0.13 | 0.08 | **0.04** | 0.14 |
| Edit a note (update + re-tag) | 0.06 | 0.21 | 0.14 | **0.08** | |
| Bulk load (restore), all notes | 504 | 883 | 576 | **504** | 1,487 |
| Size on disk | 14.9 MB | 15.2 MB | 15.0 MB | **15.2 MB** | 7.5 MB, 10,000 files |

### 50,000 notes

| Operation | sqlite | sqlcipher | chacha20 | **aegis** | **aegis, budget phone (×5)** | md-folder | md-folder, budget phone (×5) |
|---|---|---|---|---|---|---|---|
| Open + first feed page (cold) | 1 | 1 | 1 | **1** | **5** | **8,001** | **≥ 40,000** |
| Memory held by the index | — | — | — | — | — | 90 MB | 90 MB |
| Feed: first page | 0.23 | 0.26 | 0.24 | **0.24** | **1.2** | 0.00 | 0.0 |
| Pinned list | 1.2 | 10.0 | 9.9 | **2.2** | **11** | | |
| Tag counts | 41 | 80 | 67 | **45** | **225** | 26 | 130 |
| Label counts | 16 | 38 | 36 | **16** | **80** | | |
| Calendar month counts | 3.0 | 3.0 | 2.9 | **3.2** | **16** | | |
| Tag filter: first page | 44 | 133 | 116 | **52** | **260** | | |
| Label filter: first page | 22 | 59 | 50 | **24** | **120** | | |
| Search with no match | 134 | 278 | 240 | **148** | **740** | 14 | 70 |
| Post a note | 0.10 | 0.39 | 0.29 | **0.12** | **0.6** | 0.19 | 1.0 |
| Toggle pin | 0.03 | 0.13 | 0.10 | **0.04** | **0.2** | 0.21 | 1.1 |
| Bulk load (restore), all notes | 2,720 | 3,621 | 3,267 | **2,693** | **13,500** | 8,286 | 41,000 |
| Size on disk | 74 MB | 75 MB | 75 MB | **75 MB** | | 37 MB, 50,000 files | |

### EF Core

| | Mac | Android emulator | Budget phone (×5) |
|---|---|---|---|
| First query in the process (model building, query compilation, JIT) | 557 ms | **808 ms** | **~4 s** |
| Each feed page after that (new context and connection) | 2.4 ms | 2.7 ms | ~14 ms |
| Raw `Microsoft.Data.Sqlite`: first feed page in the process | 2 ms | **4 ms** | ~20 ms |

## Reading the results

1. **SQLite is fast enough, with a wide margin.** Everything a screen shows while scrolling, such as feed pages,
   pinned notes, the calendar and habits, takes well under a frame (16 ms), even at 50,000 notes on a budget phone.
   Writes take under a millisecond. The slowest operations are tag counts, filters by tag or label, and search, which
   all touch many notes. At 50,000 notes on a budget phone they take a quarter to three quarters of a second. That
   needs a spinner and a background thread, not a different database. Search returns its first matches early anyway
   (see [04-domain-rules.md](04-domain-rules.md#search)).
2. **Encryption costs almost nothing with AEGIS-256.** It is within 10–20% of unencrypted SQLite on every operation,
   because it uses the CPU's AES instructions. The server's SQLCipher format is 2–3× slower on queries that read
   many pages, and ChaCha20 is in between. The app's database never leaves the device and is never opened with the
   `sqlcipher` tool, so it does not need the server's format. Backups are compatible through the ZIP export, not
   the database file.
3. **The `.md` folder fails on start-up time and memory.** It has no index on disk, so every launch reads every
   file. That took 0.3 s at 10,000 notes and **8 s at 50,000 on the emulator**, roughly 40 s on a budget phone,
   while holding 90 MB in memory, which makes Android more likely to kill the app in the background. Once loaded it
   answers from memory quickly, but that only moves the cost to every launch. Making it start fast would need an
   index file kept in sync with the notes, which is a database built by hand. It would also give up encryption, and
   changing a pin, label or archive state would mean rewriting the note's whole file.
4. **EF Core's first query costs 0.8 s on the emulator, about 4 s on a budget phone.** That cost lands on every
   launch, while the app's whole data layer is about 25 hand-written queries. Use `Microsoft.Data.Sqlite` directly
   ([02-architecture.md](02-architecture.md#data-access)).
5. **Keep note text out of the metadata table.** With the server's layout, encrypted counts and filters were 3–7×
   slower, because they decrypted pages full of note text.

## The decision, as rules for the app

- The database is SQLite through `Microsoft.Data.Sqlite` + `SQLite3MC.PCLRaw.bundle` (MIT), opened with
  `cipher=aegis` and a raw 256-bit key (`Password=x'<64 hex>'`). The key lives in the OS secure storage. Encryption is
  always on and there is no setting to turn it off ([03](03-data-storage-and-security.md)).
- Note text lives in `NoteBodies`; `Notes` holds only metadata. Lists, counts and filters must not read
  `NoteBodies` except for the notes they return.
- No EF Core and no other ORM. Write SQL in repository classes, migrate with `PRAGMA user_version`.
- Run every query off the UI thread. Show a spinner for counts, filters and search when they take longer than 150 ms.
- Batch restores into transactions of about 200 notes. One transaction per note also works (0.12 ms each, about
  6 s for 50,000), but batching is faster.

## Caveats and how to confirm on real hardware

- The emulator had one core, but that core was an M1 Pro core. Real budget phones will be slower; the ×5 column
  estimates that. Run the benchmark on the slowest phone you intend to support before Phase 1 ends
  ([10-implementation-plan.md](10-implementation-plan.md)):
  ```sh
  cd benchmarks/storage/Droid && dotnet build -c Release
  adb install -r bin/Release/net10.0-android/android-arm64/dev.maplenotes.bench-Signed.apk
  adb logcat -c && adb shell am start -n dev.maplenotes.bench/$(adb shell cmd package resolve-activity --brief dev.maplenotes.bench | tail -1 | cut -d/ -f2)
  adb logcat -s 'MAPLEBENCH:*' | grep -o 'MAPLEBENCH|.*' > results/<device>.txt   # until MAPLEBENCH|finished
  python3 table.py results/<device>.txt
  ```
  On the desktop: `cd benchmarks/storage/Desktop && dotnet run -c Release -- <data dir> 1000,10000,50000`.
- AEGIS is fast because of hardware AES. All 64-bit Android phones from recent years have the ARMv8 crypto
  extensions, as do all x64 PCs and all Macs. On a CPU without them, AEGIS falls back to software and runs at about
  ChaCha20 speed: still acceptable.
- The `.md` folder was measured right after its files were written, so they were probably still in the OS file
  cache. A real cold start after a reboot would be slower, so its numbers flatter it.
