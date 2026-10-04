# Changelog

Versions are `major.minor.build` with a three-digit build number. Every release bumps the build;
the minor number changes only for larger milestones.

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
