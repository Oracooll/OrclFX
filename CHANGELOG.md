# Changelog

Versions are `major.minor.build` with a three-digit build number. Every release bumps the build;
the minor number changes only for larger milestones.

## 1.1.024
- Default view: right-click a view button in the title bar to make that view the default for every folder
  you open, in all panes and tabs (a small green tick marks it). It always wins over the view a folder had
  before; left-click still changes the current folder's view until you open another. Right-click the
  ticked button again to go back to each folder's own view.

## 1.1.023
Final fixes from the audit (round 7 found no major problems):
- After a copy finishes, a window closed during it no longer flashes back before closing.
- A closing app no longer takes a "show your window" request meant for a newly started one.

## 1.1.022
Fixes from audit round 6:
- Starting the app while its window waits hidden for a copy to finish shows the window again (before, the
  start did nothing until the copy had finished).
- If saving fails when the window closes after a copy, the window comes back to ask, instead of staying
  invisible.
- Renaming a shortcut's folder on a network share no longer checks the share on the UI thread.
- The window's minimum width leaves room for all title bar buttons and the icon.

## 1.1.021
Fixes from audit round 5:
- Copies, moves and deletes started in a pane keep running when you close the window (it hides until
  they finish, like File Explorer); a theme switch waits for them, and an update is put off until they're
  done. Before, closing or restarting cut them off halfway.
- Tabs on an offline network share or mapped drive no longer delay startup: they're looked up only when
  first shown.
- Opening a folder from Find results in a locked tab opens it in a new tab (the locked tab stays put).
- A window that was completely off-screen reopens on the main monitor.

## 1.1.020
Fixes from audit round 4:
- After a monitor is unplugged or rearranged, the window no longer reopens with its title bar off-screen
  (where it couldn't be moved, maximized or closed): it's moved back onto a screen.
- A OneDrive conflict copy is only removed if it's unchanged since it was merged.
- Hand-edited, absurdly large pane sizes in the settings can no longer break startup.

## 1.1.019
Fixes from audit round 3:
- Closing the app with a very narrow window made the next start show "Something went wrong" with every pane
  empty. The window now has a minimum size and always starts properly.
- An update or install whose final swap fails can no longer leave the program file missing.
- Uninstall isn't refused because another Windows user has the app open.
- A OneDrive conflict copy whose removal had to wait is removed after the next successful save (it could
  otherwise bring removed shortcuts back after a restart).

## 1.1.018
Fixes from audit round 2:
- Updates and installs replace the program safely: the new copy is checked before it's swapped in, so a
  failed update (full disk, antivirus) leaves the previous version working.
- Uninstalling while the app runs is refused (a quiet uninstall reports failure) instead of half working.
- A quiet install can't show a crash window if registering it fails.
- Shortcuts: a conflict copy that can't be deleted is merged only once (no endless saving); the old
  `DualPane` list's merge note is the same on every computer, and is ignored when the list itself hasn't
  synced yet (it could have emptied the list); a save retry during a rename no longer saves the folder name
  as the label; menu actions on a shortcut replaced by a reload work, and can't block later reloads.
- "Open in other pane" onto a locked tab that hasn't been shown yet opens the folder in a new tab.
- The tab menu no longer touches a network share on the UI thread.

## 1.1.017
Fixes from an audit:
- Shortcuts are no longer lost between computers: OneDrive's conflict copies (`shortcuts-<computer>.txt`,
  made when two computers changed the list before syncing) are merged in and removed; changes a computer
  still on an old version makes to the old `DualPane` list are merged in; a list kept locally before
  `OneDrive\Documents` existed joins the shared one; shortcuts from very old versions are kept.
- No freezes on network folders: the folder-size check and the Shortcuts menu no longer touch the network on
  the UI thread; a preview handler that hangs can't freeze the window or closing.
- An error inside a keyboard shortcut no longer closes the app (it's logged and shown in the status bar).
- Dragging many files over the Shortcuts pane no longer re-checks every file on each mouse move.
- Find: searching again in a results tab no longer loses early results; a tab can't get stuck in Find mode,
  and returns to its folder if the results can't be shown; the searched folder can be reopened from the
  tree or Shortcuts.
- Quick clicks on tabs while one is closing no longer cause an error.

## 1.1.016
- Tabs the app opens (a folder opened from a locked tab, Ctrl+T, Find results, Duplicate) never land
  between locked tabs: from a locked tab, the new tab goes after that group of locked tabs. Dragging
  tabs still puts them anywhere.

## 1.1.015
- Settings move out of the old `DualPane` folders. The shortcuts list, shared by your computers, is now
  in `Documents\OrclFX` in OneDrive (when Documents is backed up to OneDrive, or OneDrive has a Documents
  folder), otherwise in `%APPDATA%\OrclFX`. Each computer's own tabs and settings are in
  `%APPDATA%\OrclFX`. Existing settings are moved and the shortcuts list copied on first start.

## 1.1.014
- Shortcuts pane: folders added while the app runs get their icon (since 1.1.005 they stayed without
  one), removing a shortcut no longer shifts the other shortcuts' icons, and the icons of shortcuts
  loaded at startup no longer go missing now and then.

## 1.1.013
- Find: the magnifier in the title bar (or Ctrl+F / F3) searches the folder in the active pane and all
  its subfolders by name (part of a name, or patterns like `*.pdf;*.docx`). Results appear as they are
  found in a new tab, in the real Explorer list with a Folder path column; double-clicking a folder
  opens it there and Back/Up returns to the searched folder. Optionally, Windows Search also looks
  inside files, like File Explorer's search box. Recent searches are remembered.

## 1.1.012
- `--install --quiet` and `--uninstall --quiet` work without any windows (exit code 1 on failure), and the
  Settings › Apps entry has a quiet uninstall command. Needed for installing through winget.

## 1.1.011
- No more "The publisher could not be verified" warning on every start. A copy installed from a browser
  download kept the browser's "downloaded from the internet" mark; installing, updating and starting
  the installed program now remove it.

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
