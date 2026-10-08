# 6. Design system

The app must look like the web app. The fastest and safest way is not to redesign anything: **port each React
component's markup with its Tailwind classes unchanged**, and build the CSS from the same tokens. The screenshots in
`reference/maple-notes-1.8.0/docs/screenshots/` (`home-feed.png`, `home-feed-dark.png`, `mobile-view.png`, `todo.png`,
`habits.png`, `settings.png`) show the target. Every screen should match them side by side.

## Stylesheet

`FalconNotes.UI/Styles/app.css` = the web app's `src/maple-web/src/index.css`, copied whole. It defines:

- `@import "tailwindcss"` (Tailwind 4).
- The `dark` variant: the `.dark` class on `<html>`, or the device's dark mode unless `.light` is set. The app always
  sets one of the two classes ([below](#light-and-dark)).
- The `short` variant: windows under 900 px tall (`max-height: 56.25rem`), which tightens the side menu.
- The side-menu sizes `[data-menu="small|medium|large"]`: `--menu-text`, `--menu-row` and `--menu-icon`, scaled 0.87,
  1 and 1.14.
- The brand tokens `--color-maple-{50,100,400,500,600,700}` and their seven accent overrides on `:root[data-accent]`.
- The font stacks: system UI and system monospace. Ship no font files.
- `body { bg-stone-100 dark:bg-stone-950 antialiased }`, and the reduced-motion rule.
- `.markdown` typography for rendered notes, and `.note-card { content-visibility: auto; contain-intrinsic-size: auto 10rem }`.

Add new rules only at the end, under a comment naming the screen they serve.

## Colour

### Brand and accents

The interface uses `maple-*` classes everywhere. They keep the reference's name so that ported markup stays unchanged;
only the default accent's displayed name is Falcon (D12). The accent changes what they mean (from `index.css`; the 600
shade is also `lib/appearance.ts` `ACCENT_COLORS`):

| Accent | 50 | 100 | 400 | 500 | **600** | 700 |
|---|---|---|---|---|---|---|
| Falcon (default; the web app's Maple) | `#fdf3f3` | `#fbe4e4` | `#e57373` | `#b0282d` | **`#8f1d21`** | `#731619` |
| Ocean | `#eff6ff` | `#dbeafe` | `#60a5fa` | `#2563eb` | **`#1d4ed8`** | `#1e40af` |
| Forest | `#f0fdf4` | `#dcfce7` | `#4ade80` | `#15803d` | **`#166534`** | `#14532d` |
| Teal | `#f0fdfa` | `#ccfbf1` | `#2dd4bf` | `#0f766e` | **`#115e59`** | `#134e4a` |
| Plum | `#faf5ff` | `#f3e8ff` | `#c084fc` | `#7e22ce` | **`#6b21a8`** | `#581c87` |
| Amber | `#fff7ed` | `#ffedd5` | `#fb923c` | `#c2410c` | **`#9a3412`** | `#7c2d12` |
| Slate | `#f8fafc` | `#f1f5f9` | `#94a3b8` | `#475569` | **`#334155`** | `#1e293b` |

Usage, as in the reference: primary buttons `bg-maple-600 hover:bg-maple-700 text-white`; active navigation
`bg-maple-50 text-maple-700 dark:bg-maple-600/15 dark:text-maple-400`; links `text-maple-700 dark:text-maple-400`; focus rings
`outline-maple-500`; tag chips `bg-maple-50 dark:bg-maple-600/15`.

### Neutrals

Tailwind's **stone** scale. Pages are `bg-stone-100` / `dark:bg-stone-950`; cards and panels `bg-white` /
`dark:bg-stone-900`; borders `border-stone-200` / `dark:border-stone-800`; secondary text `text-stone-500`,
`text-stone-600 dark:text-stone-300`. For native chrome (status bar, splash, window background) use these values:

| Token | Hex | Native use |
|---|---|---|
| stone-100 | `#f5f5f4` | Window and splash background, light |
| white | `#ffffff` | Android status bar, light (matches the phone header `bg-white/90`) |
| stone-900 | `#1c1917` | Android status bar, dark |
| stone-950 | `#0c0a09` | Window background, dark |

Danger: `bg-red-700 hover:bg-red-800` (buttons), `text-red-700 dark:text-red-400` (menu items),
`bg-red-50 text-red-800 dark:bg-red-950 dark:text-red-200` (error boxes). Warning: `bg-amber-50 text-amber-900
dark:bg-amber-950 dark:text-amber-200`.

### Label colours

From `lib/labels.ts`. Write them out in full so Tailwind finds them:

| Colour | Dot | Chip |
|---|---|---|
| Grey | `bg-stone-500` | `bg-stone-200 text-stone-800 dark:bg-stone-700/60 dark:text-stone-200` |
| Red | `bg-red-500` | `bg-red-100 text-red-800 dark:bg-red-500/20 dark:text-red-300` |
| Orange | `bg-orange-500` | `bg-orange-100 text-orange-800 dark:bg-orange-500/20 dark:text-orange-300` |
| Amber | `bg-amber-500` | `bg-amber-100 text-amber-900 dark:bg-amber-500/20 dark:text-amber-300` |
| Green | `bg-green-600` | `bg-green-100 text-green-800 dark:bg-green-500/20 dark:text-green-300` |
| Teal | `bg-teal-600` | `bg-teal-100 text-teal-800 dark:bg-teal-500/20 dark:text-teal-300` |
| Blue | `bg-blue-600` | `bg-blue-100 text-blue-800 dark:bg-blue-500/20 dark:text-blue-300` |
| Indigo | `bg-indigo-600` | `bg-indigo-100 text-indigo-800 dark:bg-indigo-500/20 dark:text-indigo-300` |
| Purple | `bg-purple-600` | `bg-purple-100 text-purple-800 dark:bg-purple-500/20 dark:text-purple-300` |
| Pink | `bg-pink-500` | `bg-pink-100 text-pink-800 dark:bg-pink-500/20 dark:text-pink-300` |

## Light and dark

`theme` is System, Light or Dark. The web app could leave System to CSS (`prefers-color-scheme`). WebViews do not
report the device setting reliably, so the app resolves it in C#:

1. `IThemeSource` reports the device theme (`Application.Current.RequestedTheme`) and raises an event when it changes.
2. `AppearanceService` computes the effective theme. `appearance.js` then sets **exactly one** of `.dark`/`.light` on
   `<html>`, `style.colorScheme`, and `data-accent` (absent for Falcon), and stores `{theme, accent}` in
   `localStorage['falcon-notes:appearance']`.
3. `index.html` runs `appearance.js`'s `applySaved()` before Blazor starts, so the first paint uses the last appearance.
4. Native chrome follows: Android status and navigation bar colours (CommunityToolkit `StatusBarBehavior`), the
   `MainPage` background, and the Windows and macOS title bar theme.

## Typography

System fonts only (`--font-sans`, `--font-mono`). Sizes in use: page titles `text-xl font-semibold`; card titles
`text-lg font-semibold leading-snug`; section headings `text-base font-semibold`; body and inputs `text-base` or
`text-[15px]`; secondary text `text-sm`; captions `text-xs`; section labels
`text-xs font-semibold uppercase tracking-wide text-stone-500`. Use `tabular-nums` for counts, dates in grids and
`done/total`. Rendered notes use the `.markdown` rules (15 px, relaxed leading, headings 20/18/16 px).

## Shape, spacing and elevation

- **Radii**: cards and dialogs `rounded-2xl`; inputs, menus and chips-in-panels `rounded-xl`; menu items `rounded-lg`;
  buttons, switches, search boxes and pills `rounded-full`.
- **Cards**: `rounded-2xl border border-stone-200 bg-white p-4 shadow-sm dark:border-stone-800 dark:bg-stone-900`;
  settings sections use `p-5`.
- **Lists of cards**: `flex flex-col gap-3`; groups of sections `gap-6`.
- **Elevation**: `shadow-sm` for cards, `shadow-lg` for menus and toasts, `shadow-xl` for dialogs and the drawer.
  Overlays use `bg-black/50 backdrop-blur-sm` for dialogs, `bg-black/40` behind the drawer, `bg-black/95` in the
  image viewer.
- **Touch targets**: icon buttons `size-10` (40 px); rows `min-h-11` or `min-h-12`; habit circles `size-10`
  (`sm:size-9`).

## Layout

| Width | Layout |
|---|---|
| < 1,024 px (`lg`) | Sticky top bar `h-14` (`bg-white/90 backdrop-blur`, below the safe area) with the menu button, mark and name. The side menu opens as a drawer from the left, `w-[min(20rem,85vw)]`. |
| ≥ 1,024 px | Fixed sidebar `w-72`, full height, `border-r`, next to the content; the whole shell centred in `max-w-6xl` |
| Content column | `px-4 pb-24 pt-4 lg:px-10 lg:pt-8`, `max-w-2xl` (Settings: `max-w-4xl`) |
| Settings | Phone: the section list, then each section on its own page with "‹ Settings". ≥ 768 px (`md`): the list (13.5 rem) beside the open section. |
| Habit rows | Phone: name and menu on one line, the 7 days under them. ≥ 640 px (`sm`): one line. |

Respect the safe areas (`env(safe-area-inset-*)`), as the reference does in the header, toasts and image viewer. On
Android, draw edge to edge and let the WebView pad with these insets. Desktop windows open at 1,200 × 800 or larger,
with a minimum of 400 × 600.

## Components

Port these from the web app's `src/maple-web/src/components/ui.tsx` and the files named, keeping the
class strings:

| Component | Source | Notes |
|---|---|---|
| `Button` (primary, secondary, ghost, danger; `busy`) | `ui.tsx` | `h-10 rounded-full px-4 text-sm font-medium`; busy shows a spinner and disables |
| `IconButton` | `ui.tsx` | `label` is required: it is the accessible name and the tooltip |
| `TextField` | `ui.tsx` | Label, hint or error, `aria-describedby` |
| `Switch` | `ui.tsx` | `role="switch"`, 48 × 28 px |
| `Spinner`, `Card`, `Section`, `EmptyState`, `ErrorMessage` | `ui.tsx` | |
| `Toaster` | `Toaster.tsx` | Bottom-centre, at most 3, 4 s (8 s for errors and toasts with a button), polite live region |
| `ConfirmDialog` | `ConfirmDialog.tsx` | Use `<dialog>` + `showModal()` instead of Radix: traps focus, Esc cancels, focus returns to the opener |
| `DropdownMenu` + `MenuItem` | `NoteCard.tsx` | `min-w-48 rounded-xl border bg-white p-1 shadow-lg`, items `px-3 py-2.5 text-sm`, danger items red, separator `h-px bg-stone-200`. Opens aligned to the trigger's end, 4 px below; flips up near the bottom of the screen. Arrow keys, Enter, Esc, outside click. |
| `PreferenceSwitch` | `PreferenceSections.tsx` | Label, description, switch |
| Segmented choices | Theme, menu size, export format, Tags order | Radio inputs hidden with `sr-only`, styled labels |
| `LabelDot`, `LabelChips`, `LabelPicker` | `Labels.tsx` | |
| `Logo` | `Logo.tsx`, `BrandMark.tsx` | The falcon mark as an `<img>` onto `fixtures/falcon-mark.png` ([below](#app-icon-and-splash)); `Class` sizes it (`size-7`/`size-9`/`size-14` across the shell) |

## Icons

Lucide icons (ISC licence; record it in `THIRD-PARTY-NOTICES.md`), as inline SVG through one `Icon.razor`
component: `viewBox="0 0 24 24"`, `fill="none"`, `stroke="currentColor"`, `stroke-width="2"` (habit check: 3),
round caps and joins, `aria-hidden="true"`. Copy the path data of exactly these icons from the Lucide version the
reference uses (`lucide-react` in `package.json`):

`AlertCircle Archive ArchiveRestore ArrowDown ArrowUp Bold CalendarCheck CalendarDays CalendarX Check ChevronLeft ChevronRight
CircleHelp CircleUserRound Code Copy DatabaseBackup Download FileText GripVertical Hash Heading2 Home Italic Link2 List
ListChecks ListTodo Lock LogOut Menu Monitor Moon MoreHorizontal Palette PanelLeft Paperclip Pencil PenLine Pin PinOff
Play Plus Quote RotateCcw Search Settings ShieldCheck Sun Tag ToggleRight Trash2 TriangleAlert Upload X Zap`

New screens may also use `Fingerprint`, `KeyRound` and `Share`. Add any other icon to this list first.

## App icon and splash

- **The mark**: an original feather, created for Falcon Notes as a transparent square PNG (not an icon-set glyph or
  clip art, and not resembling another product's mark). It replaced the original hand-drawn falcon-head SVG on
  2026-10-07, owner-approved; the source lives in `fixtures/falcon-mark.png`. Because it is a detailed illustration
  rather than flat shapes, it reads clearly from about 32 px up (the in-app `Logo` component's smallest use, `size-7`)
  but softens below that, same as any raster mark would. `fixtures/maple-leaf.svg` is the web app's mark, kept for
  reference: never ship it.
- **App icon**: the mark as the foreground on a `#fdf3f3` (maple-50) background, through
  MAUI's `MauiIcon` (`ForegroundScale` about 0.65, PNG source/foreground). Generate the Android adaptive icon, Windows
  and macOS sizes from it.
- **Splash**: the mark centred on `#f5f5f4` (PNG source). Android 12+ uses the icon.
- Never ship the server's branding feature: the name is always "Falcon Notes".

## Motion and accessibility

- Transitions only on colour, transform and width (`transition-colors`, `transition-transform`). The reduced-motion
  rule in `index.css` turns them off.
- Keep every `aria-*`, `role`, `sr-only` text and live region from the reference. They are part of the design, and the
  reference's tests check many of them.
- Focus is always visible: `focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-maple-500`.
- "Skip to content" link first in the shell.
- Text must stay readable at the OS font size settings: the WebView follows them. Check Android at 130%.
