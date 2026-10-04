# Orcl File Explorer

A light multi-pane file manager for Windows, in the spirit of xplorer2. Each pane hosts the real
Windows Explorer view, so thumbnails, right-click menus, drag-and-drop, rename and columns all
behave exactly like File Explorer.

## Features

- One to four panes side by side (Alt+1 … Alt+4, title bar, or ☰ › Panes side by side), for wide and
  ultrawide screens. Each pane has its own tabs, remembered between sessions; hidden panes keep theirs.
  Dragging a divider moves only that divider: the pane to its left changes by exactly the drag distance,
  panes further left stay put, and the panes to its right share the difference equally; double-click a
  divider to make all panes equal
- The active pane (the last one you used) has an accent frame
- Locked tabs (right-click a tab › Lock): they never leave their folder, opening a folder from one opens a new tab
- Double-click empty space in a file list to go up one level
- View modes in the title bar (Details, List, Tiles, Content, Medium and Large icons) and all eight in
  ☰ › View mode (also Explorer's Ctrl+Shift+1 … 8)
- Tree pane on the left that follows the active pane (Alt+T)
- Preview pane on the right for the active pane's selected file, using Windows preview handlers (Alt+P)
- Shortcuts pane along the bottom (Alt+S): drop folders onto it, double-click to open (Ctrl+double-click / middle-click for a new tab).
  The list is stored in `OneDrive\DualPane\shortcuts.txt`, so every computer signed in to the same OneDrive shares it;
  paths under OneDrive or the user profile are stored as `%OneDrive%` / `%USERPROFILE%`. Shortcuts whose folder
  doesn't exist on the current computer are dimmed and listed in a warning line. Dropping only adds shortcuts;
  nothing on disk is ever moved from this pane. F2 (or right-click › Rename folder) renames the real folder, and
  shortcuts and open tabs inside it follow; right-click › Rename shortcut label only changes just the label.
  Right-click › Column width sets a maximum width in characters or pixels
  Sort icons at the right end of its header: as arranged, name A to Z, name Z to A, folder path A to Z.
  Sorting only changes the display; the shared file keeps the arranged order
- Title-bar buttons for the panes and view modes, and the theme switch (match Windows, light, dark)
- All separators resize live while dragging
- ☰ › View options: show hidden files (Ctrl+H), auto-fit Name column, natural number sorting, folder sizes
- Folder sizes (asks twice before turning on): the Preview pane lists the selected or current folder's subfolders
  by size and the status bar shows the total. Scans run in the background at low priority, skip network and
  optical drives, never download OneDrive files, and switch the feature off if a scan passes 90 seconds or
  2 million items

Versions are `major.minor.build` with a three-digit build (1.1.001, 1.1.002, …).

## Build

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Produces `dist\orclfx.exe` using the C# compiler built into Windows (.NET Framework 4.8); nothing to install.

## Install on a computer

Copy `orclfx.exe` to the computer (OneDrive works) and run it. Choose **Yes** to install for your
Windows account: it goes to `%LOCALAPPDATA%\Programs\Orcl File Explorer`, appears in the Start menu, and can be
uninstalled from Settings › Apps. Running a newer exe the same way (or `orclfx.exe --install`) updates the
installed copy. Run with `--portable` to skip the install prompt. Installing replaces a pre-1.1 "DualPane"
install and re-points a taskbar pin made for it.

Settings and tabs are stored in `%APPDATA%\DualPane\state.txt` (written safely, with a `.bak` copy).
Unexpected errors are shown in a message and logged to `%APPDATA%\DualPane\errors.log`.
Switching between light and dark restarts the window (tabs and layout are kept), because Windows only
applies some light/dark choices when an app starts. Only one window runs at a time; launching the app
again brings the open window to the front.
