# 4. Domain rules

Every rule the app enforces, with the reference file that defines it. Port the rule, not just its effect: tests in the
reference show the edge cases (see [11-testing.md](11-testing.md)). Paths are relative to
`reference/maple-notes-1.8.0/`; `web/` means `src/maple-web/src/` and `server/` means `src/MapleNotes.Server/`.

## Notes

| Rule | Detail | Reference |
|---|---|---|
| IDs | `Guid.CreateVersion7()` for notes, attachments and labels; stored and exported in lower-case canonical form | `server/Domain/Note.cs` |
| Kinds | `Note` (Home timeline), `Todo`, `Quick`, `Habit` | `server/Domain/NoteKind.cs` |
| Text length | At most **100,000 characters** (UTF-16 length, as `string.Length`). Over it: "A note can be at most 100,000 characters long." | `server/Features/Notes/NoteService.cs` `ValidateContent` |
| Not empty | Text that is blank (`string.IsNullOrWhiteSpace`) is refused unless the note has a file: "Write something or attach a file." | same |
| Times | `CreatedAt` is set once. `UpdatedAt` changes only when the **text or files** change (`UpdateAsync`). Pinning, archiving, trashing, labels and moves leave it. A note shows "· edited" when `UpdatedAt − CreatedAt > 60 s`. | `NoteService.UpdateAsync`/`PatchAsync`, `web/components/NoteCard.tsx` |
| Revision | +1 on every change to the row; used for stale-save checks and media URLs | `server/Domain/IRevisioned.cs` |
| Tags | Re-extracted from the text on every create, edit and restore ([Tags](#tags)) | `NoteService.SetTagsAsync` |

### Lists

Every list is newest first by `(CreatedAt DESC, Id DESC)`, except the trash, which is `(TrashedAt DESC, Id DESC)`.
Pages hold 20 notes. The next page starts after the last `(time, id)` shown (keyset paging), so new notes never shift
later pages.

| State | Which notes | Used by |
|---|---|---|
| `feed` | not pinned, not archived, not in the trash | Home, Todo, Quick notes (each with its kind) |
| `pinned` | pinned, not archived, not in the trash | "Pinned" above the feed |
| `active` | not archived, not in the trash | Search, tag, label and day filters |
| `archived` | archived, not in the trash (pinned or not) | Archive page, Archived habits |
| `trash` | in the trash | Trash page |

| Screen | State | Kinds |
|---|---|---|
| Home feed / pinned | `feed` / `pinned` | `Note` |
| Todo | `feed` / `pinned` | `Todo` |
| Quick notes | `feed` / `pinned` | `Quick` |
| Home with `?tag`, `?q`, `?day`, `?label` | `active` | enabled kinds |
| Archive | `archived` | enabled kinds |
| Trash | `trash` | all four kinds |
| Habits | `active` and `archived`, **every page loaded**, then sorted oldest first by `(CreatedAt, Id)` | `Habit` |

**Enabled kinds** are always `Note`, plus `Todo` when *Todo lists* is on, plus `Quick` when *Quick notes* is on. Habits are
never included: only the Habits page lists them (`web/lib/kinds.ts`).

The infinite list loads the next page when its end comes within 600 px of the screen, or with a **Load more** button. It
shows three pulsing placeholders while loading, and "Your notes could not be loaded." with **Try again** on error. After
more than 5 notes, when nothing more is left, it shows "You're all caught up 🦅" (`web/components/NoteList.tsx`, where it
is 🍁; [07](07-screens.md#wording)).

### Search

- The query is the box's text, trimmed. A note matches when its text contains it, **or any attachment's file name
  does**, compared with `StringComparison.OrdinalIgnoreCase`.
- It searches active notes of the enabled kinds, newest first, and returns pages of 20 matches. It reads the notes
  in one pass in time order, each once, and stops as soon as a page is full. Cancel the scan when the query changes or
  the page closes. (The server read batches of 200; here each batch re-sorted the notes, and a search that matched
  nothing took 4.4 s at 50,000 notes instead of 0.14 s.)
- Empty result: "No matching notes" / "Archived notes are not included in searches."
- Reference: `NoteService.ListAsync` (search branch) and `Matches`.

### Day filter and calendar

- `?day=yyyy-MM-dd` lists active notes of enabled kinds created between that day's start and the next day's start in
  the **device's time zone** (`web/lib/dates.ts` `dayRange`). An invalid date shows Home as usual.
- The side-menu calendar counts active notes of enabled kinds per **local** creation date, one month at a time, and
  marks days with notes with a dot. Reference: `NoteService.CalendarAsync`, `web/components/Calendar.tsx`. Days are
  local calendar days; a daylight-saving change must not shift them.

## Tags

- **Parsing** (`server/Features/Notes/TagParser.cs`), ported as is:
  1. Replace fenced code blocks (closed, or running to the end) and inline code spans with a space:
     `` ```[\s\S]*?(?:```|\z)|`[^`\n]*` ``.
  2. Match `(?<![\p{L}\p{N}_/#&])#(?<tag>[\p{L}\p{N}_][\p{L}\p{N}_/-]{0,63})`, with `RegexOptions.CultureInvariant`
     and a 1 s timeout. If the timeout fires, the note gets no tags.
  3. Trim trailing `/` and `-`, `ToLowerInvariant()`. Drop empty tags and those made only of ASCII digits (`#123`).
  4. Keep the distinct tags, ordinal, in order of appearance.
- **Stored** in `Tags` and `NoteTags`. Tags no note uses any more are deleted after every edit and delete.
- **Counts** (side menu number, Tags page, suggestions): active notes of enabled kinds. Tags with no such note are not
  listed. Sorted by name, ordinal.
- **Filter** `?tag=work` (trimmed, leading `#` removed, lower case) matches notes with tag `work` or any tag starting
  with `work/` (`NoteService.LoadBatchAsync`).
- **In rendered Markdown**, every match of the same pattern outside code and links becomes a link to
  `/?tag={tag, lower case, trailing / and - trimmed}`, showing the original text. Purely numeric tags stay text
  (`web/components/Markdown.tsx` `remarkTags`).
- **The Tags page** (`web/pages/TagsPage.tsx`) builds a tree on `/`. A parent that exists only through its children has no
  count of its own. Order is A–Z (`localeCompare` on the label) or Most used (by total, which includes children).
  The filter matches anywhere in the full name, with a leading `#` ignored.
- **Suggestions** (`web/lib/tagSuggest.ts`), when *Suggest tags while typing* is on:
  - `tagQueryAt`: the caret must be right after `#` plus a partial tag. The `#` follows the same "not preceded by"
    rule, and the next character must not be a tag character.
  - `suggestTags`: at most 6. First the tags that start with the typed text, then those with a `/` part that starts
    with it; within each group by note count (descending), then name. A single exact match is no suggestion.
  - `insertTag` replaces the partial tag with `#tag` plus a space, unless a space or newline follows already, and
    puts the caret after it.
  - Keys: ↓ ↑ move, Enter (without modifiers) or Tab inserts, Esc closes for that `#` until the caret leaves it.
    Clicking or tapping an option inserts it without taking focus from the text box.

## Titles

A title is the text's first line written as a Markdown heading, so it survives exports and searches
(`web/lib/titles.ts`):

- `splitTitle`: the first line (without a trailing `\r`) matching `^# +(.*?)(?: +#+)? *$` with non-blank text is the
  title. The rest is the body, with one leading blank line removed.
- `joinTitle(title, body)`: collapse whitespace in the title to single spaces and trim. Empty → the body unchanged.
  Otherwise `# {title}\n\n{body}`, or `# {title}` when the body is blank.
- The title field shows only when *Note titles* is on, and only for timeline notes, and for quick notes when *Titles on
  quick notes* is also on. Todo lists and habits have their own names. Turning titles off never changes a note: the
  heading then simply renders as part of the Markdown.
- *Start titles with today's date*: a **new** note's title starts as today's date in the chosen format. A title equal to
  that suggestion does not count as something written, so it alone cannot be posted. After posting, the next
  suggestion fills the field again.

## Composer

Port `web/components/Composer.tsx`:

- Content = the daily note's title + text, or title + text (titles on), or the text alone.
- **Post / Save** is enabled when nothing is saving or still being added, the content is at most 100,000 characters,
  and there is text, a file, or a typed title.
- The counter `n / 100,000` appears above 90,000 characters and turns red above 100,000.
- Files are stored as soon as they are chosen (rows with `NoteId` null) and attached on Post. Removing one before
  posting deletes it at once. **Cancel** on an edit deletes the files added during that edit, and keeps those the note
  already had (they go only if the edit is saved without them).
- Keys: Ctrl/⌘+Enter posts or saves; Esc cancels an edit; Ctrl/⌘+B, I, K format (`⌘` on macOS, Ctrl elsewhere). In the
  title field, Enter moves to the text.
- Editing opens with the caret after the last character, the box grown to fit (at most 480 px, then it scrolls).
- On save: new notes clear the box (and reset the title suggestion); edits close the editor.
- Errors: show the field error or message in a toast; default "Could not save the note."

## Markdown rendering

Port `web/components/Markdown.tsx` with **Markdig**:

- Pipeline: CommonMark + `UsePipeTables` + `UseTaskLists` + `UseEmphasisExtras(Strikethrough)` + `UseAutoLinks` (GFM
  literal autolinks) + `UseFootnotes` + `UsePreciseSourceLocation`, with **`DisableHtml()`**. Raw HTML in a note shows as
  text and never becomes markup.
- **Links**: keep `href` only for `http:`, `https:`, `mailto:`, `xmpp:`, `irc:`, `ircs:` and relative URLs; anything else
  (`javascript:`, `data:`, `file:`, …) becomes an empty `href`. This is react-markdown's default `urlTransform`.
  Relative links starting with `/` navigate inside the app. Others open in the system browser.
- **Tags** become links, as in [Tags](#tags), but never inside code or existing links. Add `class="tag"`.
- **Task lists**: render each checkbox enabled, with `data-task="{offset}"`, where offset is where the list item's
  marker starts in the rendered text, and `aria-label` set to the item's text without nested lists. Clicking calls
  `ToggleTask(offset)`. Archived notes render the boxes read-only.
- **`ToggleTask(text, offset)`** (`web/lib/markdownEdit.ts` `toggleTask`): at `offset` the text must match
  `^(?:[-*+]|\d{1,9}[.)])[ \t]+\[([ xX])\]`. Flip `x`↔space in the box, and do nothing when it does not match. The note
  card adds the length of the title part before the body to the offset.
- Wrap the output in `<div class="markdown">`. Cache rendered HTML by `(note id, revision)`, so long lists do not
  re-render Markdown.

## Markdown editing

Port `web/lib/markdownEdit.ts` `formatEdit` to C# as a pure function `(format, text, selectionStart, selectionEnd) →
Edit(start, end, insert, selectStart, selectEnd)`. `editor.js` applies it as one undoable step:

| Format | Effect |
|---|---|
| Bold, italic, code | Wrap in `**`, `*`, `` ` `` with placeholders "bold text", "italic text", "code". Unwrap if already wrapped, with the markers just outside or just inside the selection. Code over several lines becomes a fenced block. |
| Heading, bullets, checklist, quote | Prefix every selected line with `## `, `- `, `- [ ] `, `> `. If every line already has the prefix, remove it. Otherwise replace another list or heading marker. |
| Link | `[selection or "link text"](https://)`, with `https://` selected to type over |

Toolbar order and labels: Bold (Ctrl/⌘+B), Italic (Ctrl/⌘+I), Heading, Bulleted list, Checklist, Quote, Code, Link
(Ctrl/⌘+K). Buttons must not take focus from the text box.

## Todo lists

Port `web/lib/todo.ts` and `web/components/TodoCard.tsx`:

- Text: `# {title}` then one `- [ ] item` or `- [x] item` line per item.
- `parseItems`: lines matching `^\s*[-*+]\s+\[([ xX])\]\s?(.*)$` become items (done unless the box holds a space). Other
  non-blank lines become open items with any bullet removed. Item text is collapsed to one line, and empty items are
  dropped.
- `serializeTodo`: the title collapsed to one line, or "Untitled list".
- Limits: list name 300 characters, new item 500.
- Behaviour: tick; tap an item's text to edit it, where an emptied item is removed; ✕ removes; "Add an item" box at the
  bottom; the menu has Pin/Unpin, Rename, Labels…, Edit as Markdown, Clear completed (only when some are done), Archive
  or Restore, and Move to trash / Delete…. The header shows `done/total`. Archived lists are read-only.
- **Edit as Markdown**: one text box with every item, with the hint "One item per line: `- [ ]` to do, `- [x]` done. Other
  lines become items to do." Ctrl/⌘+Enter saves, Esc cancels. It saves only if the items actually changed.

## Habits

Port `web/lib/habits.ts` exactly. Day arithmetic uses day numbers (days since 1970-01-01 UTC), never local midnights:

- Text: `# {name}`, any other text (kept as it is), then one `- yyyy-MM-dd` line per day done, sorted and unique.
  "Untitled habit" when the name is blank. Day lines match `^\s*[-*+]\s+(\d{4}-\d{2}-\d{2})\s*$` and must be real dates.
- `habitStart`: the local date the habit was created, or its first day done if that is earlier.
- `streaks(days, today)`: days after today do not count. The current streak runs up to today, or up to yesterday
  while today is not ticked yet. Also the best streak and the total.
- `recentPeriods`: the last 12 weeks (starting on the chosen first day of the week) or 12 calendar months. The last one
  holds today.
- `scorePeriod`: for each habit, count days from `max(period start, habit start)` to `min(period end, today)`. `done`
  counts its days in that range; `possible` is the range's length.
- The Habits page shows 7 days ending today (`daysEnding`), with arrows to go back a week at a time, never past today.
  Tap a day's circle to tick or untick it. Today's circle is outlined.
- Habits never become another kind, and other notes never become habits. They never appear in search, tags, labels
  counts or the calendar. Exports and backups include them (folder `habits/`).
- Limits: name 300 characters.
- Archive toast: "Archived. It keeps its history under Archived habits." Restore: `Restored "{name}".`

## Daily notes

`web/lib/daily.ts`, `NoteService.CreateAsync`, `NoteService.PatchAsync`:

- Today is the device's local date, checked every minute while the app is open, so the Today card rolls over at
  midnight.
- With *Daily notes* on, Home shows **Today**: today's note if it exists, otherwise a composer titled with the date in
  the chosen format and the placeholder "Write about your day…". Nothing is saved until the first words are posted.
- Only timeline notes (`Note`) can be daily notes; there is at most one per date (unique index).
- If today's note appeared meanwhile (it should not happen with one device, but restores can add one), post into it:
  `existing.TrimEnd() + "\n\n" + body`, plus the new files.
- The daily date is cleared when the note moves to another kind or goes to the trash. Restored from the trash, it is an
  ordinary note.
- Home's pinned and feed lists hide today's note while the Today card shows it.

## Pin, archive, trash, delete

| Action | Effect | Toast |
|---|---|---|
| Pin / Unpin | `IsPinned` flips | "Pinned to the top." / "Unpinned." |
| Archive | `ArchivedAt` = now, if not set yet; pinned state kept | "Archived." (todo: "Archived."; habit: see above) |
| Restore from the archive | `ArchivedAt` = null | Note: "Restored to your feed."; todo list: "Restored." |
| Move to trash (trash **on**) | `TrashedAt` = now if not set; daily date cleared | "{Note \| List \| Habit} moved to the trash." with **Undo** (restores) |
| Undo | `TrashedAt` = null | On error: "Could not restore it. Find it in Settings → Backup & data → Trash." |
| Delete (trash **off**) | Confirm first, then delete for good | "{Note \| List \| Habit} deleted." |
| Restore from the trash | `TrashedAt` = null | "Restored to the archive." if archived, else "Restored." |
| Delete forever | Confirm, delete for good | "Deleted for good." |
| Empty trash | Confirm, delete every trashed note for good | "Deleted {n} note(s) and {m} file(s) for good." or "Deleted {n} note(s) for good." |
| Purge | At start and hourly: notes trashed more than 30 days ago are deleted for good | none |
| Move to Home / to quick notes | `Kind` = Note / Quick (Quick only offered when *Quick notes* is on) | "Moved to Home." / "Moved to quick notes." |
| Any failure | | "That didn't work. Please try again." |

Deleting for good removes the note, its tags (when unused), its label links and its files. The trash's "days left" is
`max(0, ceil((TrashedAt + 30 days − now) / 1 day))`, shown as "deleted for good today" or "deleted for good in {n}
day(s)". Confirmation texts are in [07-screens.md](07-screens.md).

## Labels

`server/Domain/Label.cs`, `server/Features/Labels/LabelService.cs`, `web/lib/labels.ts`, `web/components/Labels.tsx`,
`web/components/LabelSettings.tsx`:

- Colours: Grey, Red, Orange, Amber, Green, Teal, Blue, Indigo, Purple, Pink (Tailwind shades in [06](06-design-system.md#label-colours)).
- Names are 1–40 characters, trimmed. Names must be unique ignoring case and surrounding spaces: "You already have a
  label called “{name}”."
- At most 100 labels: "You can have at most 100 labels." At most 20 labels per note: "A note can have at most 20 labels."
- A new label's colour defaults to `Colors[(count % 9) + 1]`, which cycles through every colour except Grey.
- Sorted by name with a culture-aware, case- and accent-insensitive comparison (`CompareOptions.IgnoreCase |
  IgnoreNonSpace`), then by ID.
- A label's count covers active notes of the enabled kinds that carry it.
- Deleting a label takes it off its notes; the notes stay.
- The picker shows every label to tick, a find-or-create box, and "Create label “{typed}”" when the typed name is new
  and valid. Save applies the ticked set in the list's order, dropping labels deleted meanwhile. An unchanged set
  closes without saving.
- Labels are not part of backups, as in the web app ([05](05-backup-compatibility.md#future-labels-in-backups)).

## Preferences

The web app's `Preferences` (`web/lib/types.ts`, `web/lib/preferences.ts`) without `linkPreviews`. Defaults:

| Preference | Default | Preference | Default |
|---|---|---|---|
| `noteTitles` | false | `archive` | true |
| `dateInTitles` | false | `tags` | true |
| `quickNoteTitles` | false | `shrinkPhotos` | false |
| `dateFormat` | `yyyy-MM-dd` | `doubleTapToEdit` | false |
| `todoLists` | true | `tagSuggestions` | false |
| `quickNotes` | true | `labels` | false |
| `dailyNotes` | false | `trash` | true |
| `calendar` | true | `theme` | `System` (System, Light, Dark) |
| `habitTracker` | false | `accent` | `Falcon` (Falcon, Ocean, Forest, Teal, Plum, Amber, Slate) |
| `menuTextSize` | `Medium` (Small, Medium, Large) | `weekStart` | `Auto` (Auto, Sunday, Monday, Saturday) |
| `menuOrder` | `""` | | |

- `Falcon` is the web app's `Maple` accent under this app's name, with the same colours (D12). Preferences are not
  part of backups, so nothing needs mapping.
- A change shows at once and is saved in the background. If saving fails, it is rolled back with "Your setting could
  not be saved. Please try again."
- Turning a feature off hides its page or tool and deletes nothing. A page whose feature is off shows "{Feature} is
  turned off" with a link to Settings (`FeatureOff` in `web/pages/TodoPage.tsx`).
- `weekStart: Auto` uses `CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek` (the browser's `Intl.Locale.weekInfo`
  in the web app).

## Side menu order

`web/lib/menu.ts`: items `home, todo, quick, habits, tags, archive, settings, help`, with their labels, routes and
icons, and when each shows (Todo, Quick notes, Habits, Tags and Archive follow their feature switches).
`menuOrder(saved)` reads the comma-separated value, ignoring unknown and repeated names, and appends the missing items
in default order. `saveMenuOrder` stores `""` for the default order. The active item follows `isActive` in
`web/components/AppShell.tsx`: Home is not active while a filter is shown; Tags is active on `?tag=`; Settings is active
on `/settings/*` and `/trash`.

## Dates and numbers

| What | Rule | Reference |
|---|---|---|
| Date formats | `yyyy-MM-dd`, `dd/MM/yyyy`, `MM/dd/yyyy`, `dd.MM.yyyy`, `d MMM yyyy`, `MMM d, yyyy`, `dddd, d MMMM yyyy`, `dddd, MMMM d, yyyy`. Tokens `yyyy MMMM MMM MM dddd dd d`, with **English** names whatever the device language. | `web/lib/dates.ts` `formatDate` |
| Relative time (note header) | Under 45 s "just now"; under 1 h "{n} minute(s) ago"; under 1 day "{n} hour(s) ago"; under 7 days "yesterday" or "{n} days ago"; otherwise "Sep 12", or "Sep 12, 2024" in another year. Future times read "in {n} …", "tomorrow". English, matching `Intl.RelativeTimeFormat("en", {numeric: "auto"})`. | `web/lib/format.ts` |
| Absolute time (tooltip, settings) | Medium date and short time, e.g. "Sep 28, 2026, 2:30 PM", with the device's 12/24-hour preference | `formatAbsolute` |
| Sizes | "512 B", "1.4 KB", "23 MB", "1.2 GB": one decimal under 10, binary units | `formatBytes` |
| Counts | Thousands separators (`N0`) | `toLocaleString()` |
| Local day key | `yyyy-MM-dd` of the device's local date | `localDateKey` |

## Attachments

- **File names**: port `UploadPolicy.SanitizeFileName` (`server/Features/Attachments/UploadPolicy.cs`). Take the last path
  segment, drop control characters and `"<>|:*?/`, trim spaces and dots, fall back to "file", and cap at 200
  characters, keeping an extension of up to 20.
- **Content type**: the type the picker or the file reports, lower case, if valid and not `application/octet-stream`.
  Otherwise guess from the extension, using the table in `web/import/parse.ts` (`TYPES`) plus common office and
  archive types. Unknown: `application/octet-stream`.
- **Shown in the app** only for the passive types: `image/png, jpeg, gif, webp, avif, bmp`, `video/mp4, webm`,
  `audio/mpeg, mp4, ogg, wav, webm, flac`, `text/plain` (`UploadPolicy.CanDisplayInline`). An attachment is an **image**
  when it is `image/*` and shown inline. SVG is never shown.
- **Gallery** (`web/components/AttachmentGallery.tsx`): images first. One image keeps its shape (at most 70% of the
  viewport height); two or more form a grid of squares, two columns, three from 640 px. Then videos, audio and files.
  Videos are `video/*` and not images, audio is `audio/*`, everything else is a file chip with the name and size.
  Ordered by `CreatedAt`.
- **Lazy**: images use `loading="lazy"`. Players and media URLs start only when their note comes within 800 px of the
  screen.
- **Viewer** (`web/components/ImageViewer.tsx`): full screen; previous and next with buttons, ← →, or a horizontal swipe
  over 50 px; the title shows the name and "i / n"; **Save a copy…** replaces Download; Esc or ✕ closes.
- **Size**: no limit except 2 GiB per file and free space. Show "Not enough space on this device." when a write fails
  for lack of space.
- **Shrink photos** (`web/lib/shrinkPhoto.ts`), when on: only `image/jpeg, png, webp, avif, bmp, heic, heif`. Decode
  upright (EXIF orientation), scale so the longest side is at most 2,560 px (never enlarge), and re-encode as JPEG at
  85%, named `{name}.jpg`. Keep the original when it cannot be decoded, has transparent pixels (and is not a JPEG), or
  the result is not at least 10% smaller. The shrunk photo has no location or camera details.

## Saving structured notes

Port `web/lib/noteEditor.ts` for todo lists, habits and checkboxes ticked in notes. Each change updates the value at
once and queues a save of the whole text. Saves run one after another. After a failure: a toast (e.g. "A change to
this list could not be saved. Please try again.") and the value reloads from the database. A reload from the
database does not overwrite the value while saves are pending.

## Storage use

`Settings → Profile → Storage` shows the total, a bar split between notes (`maple-700`) and files (`maple-400`), and
"Notes {size} ({count}) · Files {size} ({count})". It counts the text of every note, the archive and trash included,
and every attachment. There is no limit.

## Starting over

- **Delete all notes and files** (Backup & data): deletes every note, tag, label and attachment in one transaction,
  then the files. Profile, preferences and app lock stay. Toast: "Deleted {n} notes and {m} files." Port of
  `DELETE /api/v1/account/content`.
- **Erase all data** (Profile): deletes the database, attachments, database copies, cache, `localStorage` and the
  device key, then shows Welcome. The confirmation requires typing `ERASE`.
