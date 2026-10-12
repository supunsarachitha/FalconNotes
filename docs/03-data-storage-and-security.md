# 3. Data, storage and security

## Where things are

| What | Where | Encrypted |
|---|---|---|
| Database | `{FileSystem.AppDataDirectory}/falcon.db` (+ `-wal`, `-shm`) | Yes: SQLite3MC, AEGIS-256, whole file |
| Attachment files | `{AppDataDirectory}/attachments/{2 hex}/{2 hex}/{id N}-{8 hex}.bin` | Yes: MNAE chunked AES-256-GCM |
| Database copies before migrations | `{AppDataDirectory}/backups/falcon-{yyyyMMdd-HHmmss}-v{n}.db`, newest 3 kept | Yes (copies of the encrypted file) |
| Device key | OS secure storage (`ISecretStore`) | By the OS (Keystore, DPAPI, Keychain) |
| App lock settings and PIN hash | `Settings['appLock']` in the database | Yes (inside the database) |
| Whether a note is locked | `Notes.IsLocked` in the database | Yes (inside the database) |
| Which locked notes are open to be read just now | Memory only (`NoteLocks`) | Not stored: closing the app locks them all |
| Decrypted copies for **Open** | `{FileSystem.CacheDirectory}/open/{random}/{file name}` | **No**: deleted at the next start, and at most 24 h old |
| Export being written, restore archive being read | `{CacheDirectory}/export/`, `{CacheDirectory}/restore/` | **No** (exports are plain by design); deleted when done and at start |
| Automatic backups, when the user turns them on | A folder **outside the app** that the user chose, as `falcon-notes-auto-{yyyy-MM-dd_HHmm}.zip`, the newest 3, 5 or 10 kept ([05](05-backup-compatibility.md#automatic-backups)) | **No**: they are exports. Off by default, and Settings says so before the folder is chosen. |
| Automatic backups' settings and the folder's reference | `Settings['autoExport']` in the database | Yes (inside the database) |
| Appearance for the first paint | WebView `localStorage` key `falcon-notes:appearance` (`{theme, accent}`) | No (not sensitive) |

Exports are not encrypted, exactly as in the web app; the Help and Backup screens say so. Automatic backups are
exports written without asking each time, so they are the one place where plain copies of the notes are made without
a tap: only after the user has turned them on and chosen the folder, and only into that folder.

## Keys

```text
device key  32 random bytes, created on first run, stored in ISecretStore as "falcon-notes.device-key.v1" (base64)
  ├─ HKDF-SHA256(salt "FalconNotes.KeyDerivation", info "falcon-notes/v1/database")    → database key (raw SQLite3MC key)
  ├─ HKDF-SHA256(salt "FalconNotes.KeyDerivation", info "falcon-notes/v1/attachments") → attachment key ("user data key" for AttachmentCipher)
  └─ HKDF-SHA256(salt "FalconNotes.KeyDerivation", info "falcon-notes/v1/fingerprint", 4 bytes) → fingerprint (8 hex characters, shown in Settings)
```

The salt and labels are this app's own (the server uses `MapleNotes.KeyDerivation` and `maple-notes/v1/…`); no other
program ever derives these keys.

Port `KeyMaterial`, `AttachmentCipher` and `DecryptingAttachmentStream` from
the web app's `src/MapleNotes.Server/Infrastructure/Crypto/`. The attachment format stays byte for byte
the same, including the 42-byte `MNAE` header (it keeps its name: it is the format's magic, and the server's tests check
it), 64 KiB chunks, the STREAM nonce and the associated data. The "owner ID" bound into each chunk is the installation
ID: a random UUID stored in `Settings` on first run.

Zero key bytes after use (`CryptographicOperations.ZeroMemory`) where the reference does.

### Database

Build the connection string with `SqliteConnectionStringBuilder`; never by concatenation, because the key contains
quotes:

```csharp
new SqliteConnectionStringBuilder
{
    DataSource = $"file:{UriPath(dbPath)}?cipher=aegis",   // see SqlCipherConnectionString.ToUriPath in the reference
    Password = $"x'{Convert.ToHexString(databaseKey)}'",      // a raw 256-bit key: no KDF, opening takes ~1 ms
    Mode = SqliteOpenMode.ReadWriteCreate,
    Pooling = true,
}.ToString();
```

- `cipher=aegis` is what the benchmark measured ([13](13-storage-benchmark.md)). Pin the AEGIS variant to AEGIS-256
  explicitly with SQLite3MC's parameter for it (check the SQLite3MC documentation for the exact URI parameter),
  so a library update cannot change it silently.
- Tests must prove: the file's first 16 bytes are not `SQLite format 3\0`; opening with a wrong key fails with
  `SQLITE_NOTADB`; a database written by one platform opens on another with the same key.

### Lost key

If the database exists but the key is missing or wrong, the notes cannot be read. That happens after a reinstall
that kept the files, an OS keychain reset, or a device restore that did not carry the Keystore. The app then shows
**Key lost** ([07](07-screens.md#key-lost)) and never deletes anything by itself. The user can restore from a backup,
which first moves the unreadable data to `{AppDataDirectory}/unreadable-{timestamp}/`, or keep it and quit.

Debug builds have a developer action in Settings → Privacy & security, **Forget the device key (testing only)**, which
removes the key so this screen can be tested. Release builds must not include it.

Android Auto Backup must be **off** (`android:allowBackup="false"`). Otherwise a phone restored from a cloud backup
gets the encrypted files without the Keystore key: notes that can never be opened.

## Schema

Migration 1; migration 2, which added `Labels.HideNotes` with Maple Notes 1.16.0; and migration 3, which added
`Notes.IsLocked` and its index with Falcon Notes 1.4.0. A migration only ever adds: an update must leave every note,
label and file as it found them, and a test proves it for each one ([11](11-testing.md#updates-keep-every-note)). Times are UTC `DateTime.Ticks` (INTEGER), IDs are lower-case canonical UUID text (`Guid.ToString("D")`),
and kinds are integers (0 Note, 1 Todo, 2 Quick, 3 Habit). Note text lives in `NoteBodies`, so lists and counts never
decrypt pages of text ([13](13-storage-benchmark.md#reading-the-results)).

```sql
CREATE TABLE Settings (
  Key   TEXT NOT NULL PRIMARY KEY,   -- 'installationId', 'profile', 'preferences', 'appLock', 'lastExportAt', 'autoExport'
  Value TEXT NOT NULL                -- JSON
);

CREATE TABLE Notes (
  Id            TEXT    NOT NULL PRIMARY KEY,   -- UUID v7, lower case
  Kind          INTEGER NOT NULL,
  DailyDate     TEXT    NULL,                   -- yyyy-MM-dd, the device's local day; timeline notes only
  IsPinned      INTEGER NOT NULL DEFAULT 0,
  ArchivedAt    INTEGER NULL,
  TrashedAt     INTEGER NULL,
  CreatedAt     INTEGER NOT NULL,
  UpdatedAt     INTEGER NOT NULL,
  Revision      INTEGER NOT NULL DEFAULT 0,     -- +1 on every change; the media URL's ?v= and stale-save checks
  ContentBytes  INTEGER NOT NULL,               -- UTF-8 length of the text, for storage use
  IsLocked      INTEGER NOT NULL DEFAULT 0      -- 1: hidden until the PIN takes the lock off (migration 3)
);
CREATE TABLE NoteBodies (
  NoteId  TEXT NOT NULL PRIMARY KEY REFERENCES Notes(Id) ON DELETE CASCADE,
  Content TEXT NOT NULL
);
-- Feed, pinned, archive and kind lists, and tag/label counts, from the index alone.
CREATE INDEX IX_Notes_List     ON Notes (Kind, IsPinned, ArchivedAt, TrashedAt, CreatedAt, Id);
CREATE INDEX IX_Notes_Created  ON Notes (CreatedAt, Id);
CREATE INDEX IX_Notes_Trash    ON Notes (TrashedAt, Id) WHERE TrashedAt IS NOT NULL;
CREATE UNIQUE INDEX IX_Notes_Daily ON Notes (DailyDate) WHERE DailyDate IS NOT NULL;
-- Only the locked notes: "is any note locked?" is asked before every export, and must not read the table.
CREATE INDEX IX_Notes_Locked   ON Notes (Id) WHERE IsLocked = 1;

CREATE TABLE Tags (
  Id   INTEGER PRIMARY KEY,
  Name TEXT NOT NULL UNIQUE                     -- lower case, without '#'
);
CREATE TABLE NoteTags (
  NoteId TEXT    NOT NULL REFERENCES Notes(Id) ON DELETE CASCADE,
  TagId  INTEGER NOT NULL REFERENCES Tags(Id)  ON DELETE CASCADE,
  PRIMARY KEY (NoteId, TagId)
) WITHOUT ROWID;
CREATE INDEX IX_NoteTags_Tag ON NoteTags (TagId, NoteId);

CREATE TABLE Labels (
  Id        TEXT    NOT NULL PRIMARY KEY,       -- UUID v7
  Name      TEXT    NOT NULL,                   -- 1–40 characters, trimmed
  Color     TEXT    NOT NULL,                   -- Grey Red Orange Amber Green Teal Blue Indigo Purple Pink
  CreatedAt INTEGER NOT NULL,
  HideNotes INTEGER NOT NULL DEFAULT 0         -- 1: its notes stay out of Home and Quick notes (migration 2)
);
CREATE TABLE NoteLabels (
  NoteId  TEXT NOT NULL REFERENCES Notes(Id)  ON DELETE CASCADE,
  LabelId TEXT NOT NULL REFERENCES Labels(Id) ON DELETE CASCADE,
  PRIMARY KEY (NoteId, LabelId)
) WITHOUT ROWID;
CREATE INDEX IX_NoteLabels_Label ON NoteLabels (LabelId, NoteId);

CREATE TABLE Attachments (
  Id          TEXT    NOT NULL PRIMARY KEY,     -- UUID v7
  NoteId      TEXT    NULL REFERENCES Notes(Id) ON DELETE CASCADE,   -- null while the note is being written
  FileName    TEXT    NOT NULL,                 -- sanitized (UploadPolicy.SanitizeFileName)
  ContentType TEXT    NOT NULL,
  SizeBytes   INTEGER NOT NULL,                 -- plaintext size
  StorageKey  TEXT    NOT NULL UNIQUE,          -- ab/cd/{id N}-{suffix}.bin
  CreatedAt   INTEGER NOT NULL,
  Revision    INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IX_Attachments_Note ON Attachments (NoteId, CreatedAt);
```

Rules the schema relies on:

- **Deleting a note for good** deletes its rows by cascade. The service reads the attachments' storage keys first and
  deletes those files after the transaction commits. It then deletes tags no note uses any more:
  `DELETE FROM Tags WHERE NOT EXISTS (SELECT 1 FROM NoteTags WHERE TagId = Tags.Id)`.
- **The daily date is given up** (set to null) when a note leaves the timeline kind or goes to the trash. That is
  what lets the unique index allow a new daily note for that day ([04](04-domain-rules.md#daily-notes)).
- `ContentBytes` + `SUM(Attachments.SizeBytes)` make the storage use figure, counting the trash and archive as the web
  app does.
- **Profile** (`Settings['profile']`): `{"displayName": "…", "createdAtUtc": "…"}`. **Preferences**
  (`Settings['preferences']`): the web app's `Preferences` object as camelCase JSON, minus `linkPreviews`, with
  unknown keys ignored and missing keys taking their defaults ([04](04-domain-rules.md#preferences)). **App lock**
  (`Settings['appLock']`): `{"enabled", "biometrics", "lockAfterSeconds", "pinHash", "pinSalt", "pinIterations",
  "failedAttempts", "lockedUntilUtc"}`. A PIN can be there with `enabled` false: locked notes use it
  ([Locked notes](#locked-notes)).

## Attachment store

Port `AttachmentStore` from the web app's `src/MapleNotes.Server/Infrastructure/Storage/`:

- Storage keys follow `^[0-9a-f]{2}/[0-9a-f]{2}/[0-9a-f]{32}(-[0-9a-f]{8})?\.bin$`. They are checked before every
  file operation, so a damaged row can never point outside `attachments/`.
- Write to a temporary name in the same folder, flush, then rename. Every write of new content gets a new random
  suffix, as on the server.
- `AttachmentCleanup`, at start and hourly: delete rows with `NoteId IS NULL` older than 24 h, with their files.
  Delete files that no row references and that are older than 1 h, and leftover temporary files.

## App lock

An optional privacy screen. It keeps people who pick up an unlocked device out of the app. It does **not** encrypt
anything: the device key does not depend on the PIN, so a forgotten PIN can never lose notes.

- **Settings → Privacy & security → App lock** ([07](07-screens.md#settings--privacy--security)):
  - **Lock Falcon Notes**: turning it on asks for a new 4–8 digit PIN, twice.
  - **Unlock with {fingerprint or face | Windows Hello | Touch ID}**: shown only when `IAppLock.IsBiometricAvailable`.
  - **Lock after**: Immediately, 1 minute (default), 5 minutes, 15 minutes or 1 hour in the background.
  - **Change PIN**, and turning the lock off: both ask for the current PIN or biometrics first.
- **The PIN hash** is PBKDF2-HMAC-SHA256 with 210,000 iterations and a 16-byte random salt, compared in constant time.
  Five wrong PINs in a row lock the screen for 30 s; each further five doubles the wait, up to 15 minutes. The
  counter and `lockedUntilUtc` are stored, so restarting the app does not reset them.
- **When locked**, only the Lock screen renders: no app shell, no note data, no media handler answers.
- **While the app is in the background with the lock on**, cover the window with the brand colour and the falcon mark,
  so the app switcher shows no notes. On Android, set `FLAG_SECURE` while the lock is on, which also blocks screenshots;
  say so under the switch.
- **Forgot your PIN?** The lock cannot be bypassed, since that would defeat it. The way out is to erase the app's data
  and restore from a backup. The Lock screen explains this and offers **Erase and start over…**, after typing `ERASE`.

### Locked notes

New in 1.4.0 (2026-10-12, at the owner's request). A single note can be locked: the app then shows that it is there
and nothing of what is in it, until the PIN opens it to be read; then it locks again by itself. **Lock** is offered
only while **Note lock** is turned on in Settings → Privacy & security, which it is not by default. What a locked note is left out of is in
[04](04-domain-rules.md#locked-notes), and the screens are in [07](07-screens.md#locked-notes). Like the app lock, it
is a privacy screen and **not** a key: the note is encrypted exactly as every other note is, with the device key, and
a forgotten PIN cannot make it unreadable to the app, only to the person.

- **One PIN.** Locked notes use the app lock's PIN, hash and tries counter; there is no second PIN to remember. The
  PIN is **in use** while the app lock is on or any note is locked, the trash included
  (`AppLockService.PinInUseAsync`).
- **The first lock** on a device with no PIN in use asks for one (Set a PIN) and saves it with the app lock left off.
  With a PIN in use, locking asks for nothing. Nor does it when the PIN there is was typed or chosen since the app
  started (`AppLockState.PinReadyAsync`): a note unlocked to be read is locked again with one tap, although for that
  moment no note was locked. A stored PIN that nobody has typed in this run of the app is not trusted that far: it
  may be one left by an app lock turned off long ago and forgotten, and a note locked with it could not be opened
  again. That one is chosen anew.
- **A PIN in use is never chosen again**: whoever could choose it could open the locked notes. Turning the app lock on
  while a note is locked asks for the PIN there is and keeps it; **Change PIN…** is offered while the PIN is in use,
  with the app lock on or off, and asks for the current one first. The service refuses a new PIN otherwise
  (`SetPinAsync`, `EnableAsync`). A PIN left behind by an app lock since turned off, with no note locked, protects
  nothing and is replaced by the next one chosen, as before.
- **Unlocking** asks for the PIN, or the fingerprint or face where the app lock allows it, through the same check as
  Settings uses. Wrong PINs count towards the same five tries and the same waits, and the dialog says "Too many
  tries. Try again in {n} seconds." while one runs.
- **Unlocking does not take the lock off.** It opens the note to be read and changed, and the fact is kept in memory
  only (`NoteLocks`), never in the database. The note **locks again by itself** when another page is opened, when the
  app goes to the background (at once, whatever the app lock's "Lock after" says, and with the app lock off too),
  when the app lock locks, and when the app is closed. There is no timer: on a phone the screen turning off sends
  the app to the background, which locks it. Taking the lock off for good is a separate, deliberate choice (**Remove
  lock**), offered only on a note that is open, so it never needs the PIN a second time.
- **The switch** (**Note lock**, the preference `noteLock`, off by default) decides only whether **Lock** is in the
  menus. Turning it off reveals nothing and so asks for no PIN: notes already locked stay locked and keep **Unlock…**,
  or a switch could strand them.
- **Forgot your PIN?** A locked note can still be deleted. An export made before, or an automatic backup, holds it
  unlocked. There is no other way in.
- **What it does not do.** Exports and automatic backups hold locked notes as plain text, like every note; that is why
  the PIN is asked for before an export and before the automatic backups' folder is chosen, while a note is locked.
  The lock is not in a backup, so a restored note is not locked.

## Threat model

| Threat | Protected? | How |
|---|---|---|
| Someone copies the app's data files (another user account on the PC, a lost laptop without disk encryption, an extracted phone image) | **Yes** | The database and files are encrypted with a key kept in the OS secure store, not with the files |
| A cloud or device-transfer backup of the app folder | **Yes** | Auto Backup off on Android. Elsewhere, files copied without the key are unreadable. |
| Someone with the unlocked device opens the app | **Yes, with the app lock on**; otherwise no | PIN or biometrics, the background cover, `FLAG_SECURE` |
| Someone using the open app, or looking at its screen, reads a note that was locked | **Yes, inside the app** | The card shows no text, file or label; the note is in no search result, tag list or tag count; its files are not opened; an export asks for the PIN first. A screenshot shows only the locked card. |
| Someone picks up the device after its owner unlocked a note to read it | **Yes, once the page or the app was left** | The note locks again by itself when another page is opened and when the app goes to the background, which a screen turning off does. Not before: a note left open on the screen can be read. |
| The same person deletes a locked note, or finds it in an export or automatic backup made earlier | **No** | Deleting needs no PIN, so that a forgotten PIN can be cleared away; backups are plain by design |
| Malware running as the same OS user, or root/admin access while the user is logged in | **No** | It can ask the OS secure store for the key, as the app does. That is the limit of any app without a password-derived key. |
| Exported ZIP files, by hand or automatic | **No** | Exports are plain Markdown/text/JSON by design (compatibility); the app says so when exporting and where automatic backups are turned on. Whoever can open the chosen folder can read the automatic ones. |
| Decrypted temporary copies made by **Open** | Partly | Kept in the app's private cache, deleted at the next start and after 24 h |
| Tampering with the database or files | **Detected** | AEGIS and AES-GCM authenticate every page and chunk; damaged attachments are reported, not shown |
| Notes leaving the device over the network | **Yes** | The app makes no network requests; the WebView's content security policy forbids remote loads |
