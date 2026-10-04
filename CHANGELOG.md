# Changelog

Versions are `major.minor.build` with a three-digit build number. Every release bumps the build;
the minor number changes only for larger milestones.

## 1.1.010
- Check for updates: the menu finds the newest release on GitHub and, if you agree, downloads it,
  checks it (size, SHA-256 checksum, version), saves your tabs, replaces the program and restarts.
  A daily background check notes new versions in the status bar (can be turned off in the menu).
- The app icon at the top left of the title bar opens the main menu.

## 1.1.009
- New app icon: a yellow folder with documents (transparent background). The icon generator
  now crops a transparent image to its visible part so the small taskbar sizes stay legible.

## 1.1.008
Fixes from a second code audit:
- Shortcuts: changes that couldn't be saved yet are no longer lost when the shared list is reloaded;
  they are merged with the new contents. Saving reads, merges and writes in one locked step, so two
  windows sharing the list can't overwrite each other's changes.
- Previews (preview handlers, thumbnails and the file checks behind them) run on one background
  worker that only handles the newest selection, so a slow or hung preview can't freeze the window
  or closing. A stuck preview is abandoned and a fresh worker takes over.
- A damaged settings file no longer wins over a good backup, and a pane whose saved tabs are all
  unusable opens with a default tab instead of none.
- Closing (and the restart after a theme change) checks that settings and shortcut changes were
  saved; if not, it asks before anything is lost.
- Folder sizes: a local link that leads to a network share is detected and not scanned.
- Free disk space: never shows the previous drive's numbers; a stuck query no longer blocks others.
- Resource cleanup in the tree, preview and shortcut icons; 11 new tests (49 in all).

## 1.1.007
- Shortcuts pane: folder icons show again. Since 1.1.005 they were loaded on a background thread
  that couldn't use the shell properly, and icons ready before the window opened were thrown away.

## 1.1.006
- The code is split into smaller files by area (`src/Core`, `src/Panes`, `src/UI`, `src/Interop`)
  instead of one 4,700-line file. The logic with no UI (shortcut-list merging, pane widths, the
  settings file, folder sizes, path and file helpers) is separated from the windows that use it.
- Automated tests (`test.ps1`, 38 tests plus an optional smoke test that starts the app), run by
  GitHub on every push.
- No change in how the app looks or behaves.

## 1.1.005
Fixes from a code audit:
- Shortcuts shared through OneDrive now merge properly: a shortcut deleted or renamed on another
  computer is no longer brought back or overwritten by this one.
- A failed save of the shortcuts list or the settings is shown and retried instead of being lost
  silently; a damaged or empty settings file is restored from its `.bak` copy.
- Only one window per settings file, portable copies included; file writes use unique temporary
  files and never overlap.
- Preview handlers run only outside the app (no in-process fallback); thumbnails are made in the
  background; free disk space is read in the background, so a slow network drive can't freeze the window.
- Folder sizes: limits are enforced inside large folders, unreadable folders are counted and the
  total is shown as "at least", and results older than two minutes are recalculated.
- Navigation and preview failures are detected and reported; shortcut icons load in the background.
- Uninstall removes only `orclfx.exe` and an empty install folder; tabs show the new name right
  after a folder is renamed; smaller resource-cleanup fixes.

## 1.1.004
- Shortcuts pane: a single click only selects; double-click or Enter opens (Ctrl: in a new tab).
- Shortcuts pane: sort icons at the right end of its header (as arranged, name A to Z, name Z to A,
  folder path A to Z). Sorting only changes the display; the shared list keeps its arranged order.

## 1.1.003
- Title bar: the Tree toggle now sits with the Preview and Shortcuts toggles.

## 1.1.002
- Pane dividers: dragging moves only that divider; the pane to its left changes by exactly the drag
  distance and the panes to its right share the difference equally. No more jump when grabbing a divider.

## 1.1.001
- Renamed from "DualPane" to **Orcl File Explorer**; the program is now `orclfx.exe` and installs to
  `%LOCALAPPDATA%\Programs\Orcl File Explorer`. Installing replaces the old DualPane install and
  re-points a taskbar pin made for it. Settings and the shared shortcuts list keep their locations.
- Version number shown in the title bar.
- View modes: Details, List, Tiles, Content, Medium and Large icons in the title bar; all eight
  Explorer modes in ☰ › View mode.

## 1.0.0 (as DualPane)
- One to four panes side by side, each with its own tabs, remembered between sessions; hidden panes
  keep their tabs. Locked tabs that never leave their folder.
- Real Windows Explorer view in every pane (thumbnails, context menus, drag and drop, columns).
- Double-click empty space to go up a level; the active pane is framed in the accent colour.
- Tree pane that follows the active pane; Preview pane using Windows preview handlers.
- Shortcuts pane shared between computers through OneDrive, with missing-folder warnings,
  column width setting, and renaming the real folder (F2).
- Light, dark or match-Windows theme (switching restarts the window so every part follows).
- Show hidden files, auto-fit Name column, natural sorting, background folder sizes with safety limits.
- Custom title bar with pane, view and theme controls; live-resizing separators.
- Per-user installer (no admin rights), Start menu entry, uninstall from Settings › Apps,
  single window per user, crash-safe settings with backup, error log.
