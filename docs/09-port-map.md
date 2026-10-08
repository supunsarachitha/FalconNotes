# 9. Port map

Every file of the reference and where it goes. Work file by file: open the source, port it, then port its tests.
`web/` = `reference/maple-notes-1.8.0/src/maple-web/src/`, `server/` = `reference/maple-notes-1.8.0/src/MapleNotes.Server/`.

Legend: **Port** = same behaviour, new language or framework · **Copy** = take the code almost verbatim · **Adapt** =
port with the changes in the docs · **Drop** = not in this app.

## Web app: logic (`web/lib`, `web/export`, `web/import`, `web/help`)

| Source | Target | How |
|---|---|---|
| `lib/types.ts` | `Core/Domain/*` records and enums | Port (no wire types; no encryption types) |
| `lib/kinds.ts` | `Core/Domain/NoteKinds.Enabled(Preferences)` | Port |
| `lib/titles.ts` | `Core/Text/Titles.cs` | Port |
| `lib/todo.ts` | `Core/Text/Todo.cs` | Port |
| `lib/habits.ts` | `Core/Text/Habits.cs` | Port (day numbers via `DateOnly.DayNumber`) |
| `lib/dates.ts` | `Core/Text/DateFormats.cs` (`FormatDate`, `ParseDateKey`, `LocalDateKey`, `WeekStartDay`) | Port |
| `lib/format.ts` | `Core/Text/RelativeTime.cs`, `Core/Text/Bytes.cs` | Port ([04](04-domain-rules.md#dates-and-numbers)) |
| `lib/markdownEdit.ts` | `Core/Text/MarkdownEdit.cs` (`FormatEdit`, `ApplyEdit`, `ToggleTask`) + `UI/wwwroot/js/editor.js` | Port |
| `lib/tagSuggest.ts` | `Core/Text/TagSuggest.cs` | Port |
| `lib/caret.ts`, `lib/focus.ts` | `UI/wwwroot/js/editor.js` | Port (JavaScript stays JavaScript) |
| `lib/menu.ts` | `Core/Text/MenuOrder.cs` + `UI/Layout/MenuItems.cs` (labels, routes, icons) | Port |
| `lib/labels.ts` | `UI/Components/LabelStyles.cs` + `Core/Labels/LabelRules.cs` (`HasLabelNamed`, `NextColor`) | Port |
| `lib/appearance.ts` | `UI/State/AppearanceService.cs` + `UI/wwwroot/js/appearance.js` | Adapt ([06](06-design-system.md#light-and-dark)) |
| `lib/preferences.ts` | `Core/Preferences/PreferencesService.cs` (defaults, optimistic save) + `UI/State/AppState.cs` | Port |
| `lib/daily.ts` | `Core/Notes/DailyNotes.cs` + `UI` today timer | Port |
| `lib/noteEditor.ts` | `UI/State/NoteEditor<T>.cs` | Port |
| `lib/doubleTap.ts` | `UI/wwwroot/js/gestures.js` | Port |
| `lib/viewport.ts` | `UI/wwwroot/js/observe.js` + `NearViewport` component | Port |
| `lib/links.ts` | — | Drop (link previews) |
| `lib/media.ts` | `Core/Attachments/UploadPolicy.cs` (`CanDisplayInline`, `IsImage`) | Port |
| `lib/shrinkPhoto.ts` | `Core/Attachments/PhotoShrinker.cs` (the platform's `IImageCodec`) | Port (same rules) |
| `lib/queries.ts` | Services + `Core/Events/ChangeFeed.cs` | Adapt (events instead of query cache) |
| `lib/router.tsx` | Blazor `Router` / `NavigationManager` | Adapt |
| `lib/api.ts`, `lib/apiError.ts` | — | Drop (services are called directly; errors are exceptions with user-facing messages) |
| `lib/auth.ts`, `credentials.ts`, `e2ee.ts`, `noteCrypto.ts`, `conversion.ts`, `secureContext.ts`, `branding.ts`, `icon.ts`, `mediaWorker.ts` | — | Drop |
| `crypto/*` | — | Drop (browser end-to-end crypto) |
| `export/*` | — | Drop: port the **server** exporter instead (below), which these files only imitate |
| `import/parse.ts` | `Core/Backup/Restore/RestoreReader.cs` | Port ([05](05-backup-compatibility.md#restore)) |
| `import/importer.ts` | `Core/Backup/Restore/RestoreRunner.cs` | Adapt (local service instead of API calls; batches of 200) |
| `import/zip.ts` | — | Drop (`System.IO.Compression.ZipArchive`) |
| `help/guide.ts` | `UI/Help/Guide.cs` | Adapt ([08](08-help-guide.md)) |
| `sw/*` | `App/Services/MediaHandler.cs` | Adapt ([02](02-architecture.md#serving-attachments-to-the-webview)) |
| `index.css` | `UI/Styles/app.css` | Copy |
| `main.tsx`, `App.tsx` | `App/MauiProgram.cs`, `UI/Routes.razor`, `UI/State/AppBootstrapper.cs` | Adapt |

## Web app: components and pages

All are **Port** to Razor with the same markup and classes, unless noted.

| Source | Target |
|---|---|
| `components/ui.tsx` | `UI/Components/Ui/{Button,IconButton,TextField,Switch,Spinner,Card,Section,EmptyState,ErrorMessage}.razor` |
| `components/AppShell.tsx` | `UI/Layout/AppShell.razor`, `Sidebar.razor`, `Drawer.razor`, `SearchBox.razor`, `LabelLinks.razor` (Adapt: footer, [07](07-screens.md#app-shell)) |
| `components/AuthLayout.tsx` | `UI/Layout/AuthLayout.razor` (for Welcome, Lock, Key lost) |
| `components/BrandMark.tsx`, `Logo.tsx` | `UI/Components/Logo.razor` (Adapt: the falcon mark, no custom icon) |
| `components/Toaster.tsx` | `UI/State/Toasts.cs` + `UI/Components/Toaster.razor` |
| `components/ConfirmDialog.tsx` | `UI/Components/ConfirmDialog.razor` (`<dialog>`) |
| `components/NoteCard.tsx` | `UI/Components/NoteCard.razor` + `MenuItem.razor`, `DropdownMenu.razor` |
| `components/NoteList.tsx` | `UI/Components/NoteList.razor` |
| `components/NoteRemoval.tsx` | `UI/Components/NoteRemoval.cs` (helper) |
| `components/Composer.tsx` | `UI/Components/Composer.razor` (Adapt: native picker) |
| `components/FormatToolbar.tsx` | `UI/Components/FormatToolbar.razor` |
| `components/TagSuggestions.tsx` | `UI/Components/TagSuggestions.razor` |
| `components/Markdown.tsx` | `Core/Markdown/MarkdownRenderer.cs` + `UI/Components/Markdown.razor` + `markdown.js` |
| `components/AttachmentGallery.tsx` | `UI/Components/AttachmentGallery.razor` (Adapt: Open / Save a copy) |
| `components/ImageViewer.tsx` | `UI/Components/ImageViewer.razor` (Adapt: Save a copy, Share) |
| `components/Labels.tsx` | `UI/Components/LabelDot.razor`, `LabelChips.razor`, `LabelPicker.razor` |
| `components/LabelSettings.tsx` | `UI/Pages/Settings/LabelSettings.razor` |
| `components/TodoCard.tsx` | `UI/Components/TodoCard.razor`, `ItemsEditor.razor` |
| `components/HabitRow.tsx`, `HabitChart.tsx`, `HabitCalendar.tsx` | `UI/Components/Habits/*.razor` |
| `components/Calendar.tsx` | `UI/Components/Calendar.razor` |
| `components/PreferenceSections.tsx` | `UI/Pages/Settings/{Appearance,Menu,Writing,Editing,Features}Section.razor` (Adapt: no link previews) |
| `components/MenuOrderEditor.tsx` | `UI/Pages/Settings/MenuOrderEditor.razor` + `gestures.js` |
| `components/ExportSection.tsx`, `RestorePanel.tsx` | `UI/Pages/Settings/BackupSection.razor`, `RestorePanel.razor` (Adapt) |
| `components/VersionNote.tsx` | `UI/Components/VersionNote.razor` (Adapt: app version, "based on 1.8.0") |
| `components/EncryptionSection.tsx`, `EndToEndSetupDialog.tsx`, `PasswordDialog.tsx`, `RecoveryKit.tsx`, `RecoveryKitDialog.tsx`, `LinkPreviews.tsx` | Drop |
| `pages/HomePage.tsx` | `UI/Pages/Home.razor`, `Archive.razor`, `FilterHeader.razor`, `TodayCard.razor` |
| `pages/TodoPage.tsx` | `UI/Pages/Todo.razor`, `UI/Components/FeatureOff.razor` |
| `pages/QuickNotesPage.tsx` | `UI/Pages/Quick.razor` |
| `pages/HabitsPage.tsx` | `UI/Pages/Habits.razor` |
| `pages/TagsPage.tsx` | `UI/Pages/Tags.razor` + `Core/Text/TagTree.cs` |
| `pages/TrashPage.tsx` | `UI/Pages/Trash.razor` |
| `pages/SettingsPage.tsx` | `UI/Pages/Settings/Settings.razor`, `ProfileSection.razor`, `StorageRow.razor`, `TrashSection.razor`, `DeleteContentSection.razor` (Adapt) + new `AppLockSection.razor`, `DataProtectionSection.razor`, `EraseSection.razor` |
| `pages/HelpPage.tsx` | `UI/Pages/Help.razor` |
| `pages/AuthPage.tsx`, `UnlockPage.tsx`, `RecoverPage.tsx` | Drop (new `Welcome.razor`, `Lock.razor`, `KeyLost.razor`) |

## Server (C#)

| Source | Target | How |
|---|---|---|
| `Domain/Note.cs`, `NoteKind.cs`, `Attachment.cs`, `Label.cs`, `UserPreferences.cs` | `Core/Domain/*` | Adapt (no user, no scheme, no encrypted fields; preferences without link previews) |
| `Features/Notes/NoteService.cs` | `Core/Notes/NoteService.cs`, `SearchService.cs`, `CalendarService.cs`, `TagService.cs` | Adapt: the rules are in [04](04-domain-rules.md); drop the encryption, quota and E2EE branches |
| `Features/Notes/TagParser.cs` | `Core/Text/TagParser.cs` | Copy |
| `Features/Notes/NoteCursor.cs` | `Core/Notes/NoteCursor.cs` | Adapt (an in-memory record, no string encoding needed) |
| `Features/Notes/TrashCleanup.cs` | `Core/Maintenance/TrashPurge.cs` | Adapt |
| `Features/Labels/LabelService.cs` | `Core/Labels/LabelService.cs` | Adapt |
| `Features/Attachments/UploadPolicy.cs` | `Core/Attachments/UploadPolicy.cs` | Copy (replace `FileExtensionContentTypeProvider` with a table) |
| `Features/Attachments/AttachmentService.cs`, `AttachmentCleanup.cs` | `Core/Attachments/AttachmentService.cs`, `Core/Maintenance/AttachmentCleanup.cs` | Adapt |
| `Features/Export/*.cs` | `Core/Backup/Export/*.cs` | **Copy** ([05](05-backup-compatibility.md#export)) |
| `Features/Storage/StorageService.cs` | `Core/Notes/StorageUsage.cs` | Adapt (no quota) |
| `Infrastructure/Crypto/KeyMaterial.cs`, `AttachmentCipher.cs`, `DecryptingAttachmentStream.cs` | `Core/Crypto/*` | Copy (labels and owner per [03](03-data-storage-and-security.md#keys)) |
| `Infrastructure/Storage/AttachmentStore.cs`, `LengthLimitedStream.cs` | `Core/Attachments/AttachmentStore.cs` | Copy |
| `Infrastructure/Persistence/SqlCipherConnectionString.cs` (`ToUriPath`) | `Core/Storage/Database.cs` | Adapt (AEGIS) |
| `Infrastructure/Persistence/MapleDbContext.cs`, migrations | `Core/Storage/Migrations.cs` | Adapt (hand-written SQL, [03](03-data-storage-and-security.md#schema)) |
| `Infrastructure/Hosting/StartupInitializer.cs` | `Core/Maintenance/StartupTasks.cs` | Adapt (database copy before migrations) |
| Everything else (`Auth`, `Admin`, `Branding`, `Encryption`, `EndToEnd`, `LinkPreviews`, `Preferences` controller, `Web`, `Configuration`, Data Protection, `MasterKey`) | — | Drop |

## Tests to port

| Source | Target | Notes |
|---|---|---|
| `web/lib/*.test.ts` for titles, todo, habits, dates/format, markdownEdit, tagSuggest, menu, shrinkPhoto | `tests/FalconNotes.Core.Tests/Text/*` | Every case |
| `web/import/import.test.ts`, `web/export/export.test.ts` | `tests/FalconNotes.Core.Tests/Backup/*` | Rebuilt around `fixtures/export-vectors.json` ([11](11-testing.md#backup-conformance)) |
| `web/components/*.test.tsx`, `web/pages/*.test.tsx` (except encryption, link previews, auth, end-to-end) | `tests/FalconNotes.UI.Tests/*` (bUnit) | The behaviours, not the React specifics |
| `server/../tests/MapleNotes.Server.Tests/Notes/*`, `Labels/*`, `Export/*`, `Crypto/AttachmentCipherTests.cs`, `Infrastructure/AttachmentStoreTests.cs` | `tests/FalconNotes.Core.Tests/*` | The rules: lists, cursors, tags, trash, daily notes, kinds, labels, import, the cipher's tamper cases |
