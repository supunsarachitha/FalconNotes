# 12. Platforms

Targets: `net10.0-android`, `net10.0-windows10.0.19041.0`, `net10.0-maccatalyst`. Record every deviation found in the
Phase 0 spikes here.

## Android

| Topic | Decision |
|---|---|
| Versions | Minimum API 26 (Android 8.0), target the latest. The WebView updates through the Play Store on API 24+, so the Chromium version is current. |
| Package | `dev.maplenotes.app` (or the owner's choice: decide once; changing it later loses users' data) |
| Permissions | **None.** No `INTERNET` permission: the app is offline, and the WebView loads its local content without it. Remove the permission the MAUI template adds. The media option C (loopback HTTP) would need `INTERNET`, so prefer A or B on Android ([02](02-architecture.md#serving-attachments-to-the-webview)). File access goes through the system pickers, so no storage permission is needed. |
| Backup | `android:allowBackup="false"` and `android:fullBackupContent="false"` (or `dataExtractionRules` excluding everything): the encrypted files are useless without the Keystore key ([03](03-data-storage-and-security.md#lost-key)) |
| Secure storage | `SecureStorage` (Keystore-backed). Uninstall deletes the key; reinstalling starts fresh. |
| Biometrics | AndroidX `BiometricPrompt` with `BIOMETRIC_STRONG or BIOMETRIC_WEAK`, falling back to the PIN; reports "fingerprint or face" |
| Lock privacy | `Window.SetFlags(WindowManagerFlags.Secure)` while the app lock is on |
| Edge to edge | Draw behind the system bars; the page pads with `env(safe-area-inset-*)`. Status bar colour and icon brightness follow the theme ([06](06-design-system.md#light-and-dark)). |
| Keyboard | `WindowSoftInputMode = AdjustResize`, so the composer and dialogs stay above the keyboard. Check the drawer and dialogs with the keyboard open. |
| Back | Goes back in the app's history. On Home (or Welcome, Lock, Key lost) it leaves the app. Closes an open dialog, menu or the image viewer first. |
| Files | `FilePicker.PickMultipleAsync` (Storage Access Framework); `FileSaver` uses `ACTION_CREATE_DOCUMENT`; Open uses a `FileProvider` URI for the cached copy, with read permission granted |
| Share (viewer) | `Share.RequestAsync(new ShareFileRequest)` with the cached decrypted copy |
| Release | Signed AAB for the Play Store, signed APK for direct installs. R8/trimming: `TrimMode=partial` (raw ADO.NET is trim-friendly; no EF Core). Test the release build, not just debug. |
| Tablets | Landscape tablets at 1,024 dp or more get the sidebar layout automatically |

## Windows

| Topic | Decision |
|---|---|
| Versions | Windows 10 1809 (17763) or later, x64 and arm64 |
| Packaging | **MSIX (packaged).** `SecureStorage` and the app data folders need package identity. Sideload or the Microsoft Store. |
| WebView | WebView2, evergreen runtime (present on Windows 11; on Windows 10 the installer bootstraps it) |
| Secure storage | `SecureStorage` (DPAPI-protected, per user) |
| Biometrics | `Windows.Security.Credentials.UI.UserConsentVerifier` (Windows Hello: face, fingerprint or Hello PIN) |
| Single instance | Allow one instance only: a second launch activates the first window (`AppInstance.FindOrRegisterForKey`). Two processes on one database are not supported. |
| Window | Starts at 1,200 × 800, minimum 400 × 600, size and position remembered; title bar follows the theme |
| Keyboard | Ctrl shortcuts as in the web app; F5 and Ctrl+R must not reload the WebView (disable the browser accelerator keys in WebView2) |
| Context menu | Disable WebView2's default context menu, except in text fields (cut, copy, paste) |
| Time zone | `TimeZoneInfo.Local.Id` is a Windows ID: convert it to IANA for exports ([05](05-backup-compatibility.md#manifest)) |
| Lock privacy | When locked or in the background with the lock on, show the cover; Windows has no app switcher snapshot to block |

## macOS (Mac Catalyst)

| Topic | Decision |
|---|---|
| Versions | macOS 14 (Sonoma) or later, Apple silicon and Intel. The WebKit on 14+ has `<dialog>`, `content-visibility` and modern CSS. Check `SupportedOSPlatformVersion` for Catalyst accordingly. |
| Entitlements | App Sandbox; `com.apple.security.files.user-selected.read-write` (pickers and save dialogs); **keychain access group** (`keychain-access-groups`), without which `SecureStorage` fails. No network entitlement. |
| Secure storage | `SecureStorage` (Keychain) |
| Biometrics | `LocalAuthentication` `LAContext.EvaluatePolicy(.DeviceOwnerAuthenticationWithBiometrics)` (Touch ID), falling back to the PIN |
| Window | 1,200 × 800, minimum 400 × 600, remembered. Closing the last window quits the app. |
| Menu bar | Keep App, Edit (Undo, Redo, Cut, Copy, Paste, Select All) and Window. Remove default items whose shortcuts would take ⌘B, ⌘I or ⌘K from the page (S5). |
| Keyboard | ⌘ shortcuts as in the web app (the reference picks ⌘ on Mac via `navigator.platform`; in the app, pass the platform from C#) |
| Release | Signed with a Developer ID and notarized for direct download, or the Mac App Store. Without an Apple developer account, an unsigned build runs locally only. |

## Shared

- **Data folder**: `FileSystem.AppDataDirectory` (Android `files/`, Windows `LocalState`, macOS the sandbox container's
  `Library`). **Cache**: `FileSystem.CacheDirectory`.
- **Version**: `AppInfo.VersionString`, shown at the foot of Settings and Help.
- **Platform names in copy**: Settings → Data protection and Key lost name the platform's key store:
  "Android Keystore", "Windows' protected storage", "the macOS Keychain".
- **iOS later**: keep platform code behind the Core interfaces and use no Catalyst-only APIs in shared code, so an
  `net10.0-ios` head is mostly icons, entitlements and testing.
