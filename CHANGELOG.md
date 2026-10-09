# Changelog

Versions are `major.minor.build` with a three-digit build number. Every release bumps the build;
the minor number changes only for larger milestones.

## 1.1.046
- Changes made by other programs (new, deleted or renamed files) appear at once: the app watches the folders on
  screen itself, as xplorer2 does, and passes each change on to the file list. Before, they showed up about a
  second later, through Windows' own notifications.
- F5 and Ctrl+R refresh the active pane wherever the keyboard is (the address box, the tree, the Shortcuts pane
  ...); before, F5 only worked while the file list had the focus.
- Refresh (F5, Ctrl+R or the Refresh button) fits the Name column at once, also after a width set by hand, and
  again as soon as the folder has been read again. The column no longer shrinks for a moment while the list
  reloads, even when refreshing many times in a row.

## 1.1.045
- Fixed: 1.1.044 kept making the first pane the active one. Its auto-fit sent the Name column a simulated
  double-click, which the app took for your click, and in some folders it did so every few seconds. Auto-fit now
  asks the view to auto-size the column directly (what that double-click does), with no clicks at all, and only
  fits again when the names change.

## 1.1.044
- Auto-fit now does what a double-click on the Name column's divider does: Windows sizes the column to the
  names itself, exactly as in File Explorer (no 70 % limit any more). It happens when a folder opens, on refresh
  (Ctrl+R / F5), when items are added or removed, and when names change; never while a mouse button is held, and
  the keyboard stays where it was.

## 1.1.043
- Auto-fit really fits the Name column to the longest name now (at most 70 % of the pane). It used the width the
  Explorer view reports as "ideal", which turned out not to depend on the names (about the same in every folder),
  so long names were cut off; the names are now measured. Refresh (Ctrl+R / F5) fits it again too.

## 1.1.042
- All eight views now have a button in the title bar: Small icons and Extra large icons were added, and the
  buttons are in the order Ctrl+mouse wheel goes through them, so the lit button shows which view the wheel
  chose. A default view (green tick) set with an earlier version stays on the same view.

## 1.1.041
- Ctrl+mouse wheel goes through the views in this order: Details, List, Tiles, Content, Small, Medium, Large and
  Extra large icons.

## 1.1.040
- Ctrl+mouse wheel over a file list steps through all eight views, smallest to largest: Details, List, Small
  icons, Content, Tiles, Medium, Large and Extra large icons (wheel up: larger), one view per notch, stopping at
  either end. The status bar names the view.

## 1.1.039
- A Refresh button next to New text file in each pane (the same as Ctrl+R / F5).

## 1.1.038
- Auto-fit of the Name column works again after a refresh: it no longer waits for the folder or its number of
  items to change, but re-fits whenever the column was changed by something else (a refresh, the view) and
  when names change. A width you set yourself (dragging or double-clicking a divider) is kept until you open
  another folder or refresh.
- Double-clicking a column divider (or a column header) resizes the column as in File Explorer instead of going
  up a level: only a double-click on empty space in the list goes up.
- Ctrl+R refreshes the folder (also in the menu); F5 still works too.

## 1.1.037
Fixes from an audit loop (3 rounds; the last found no problems):
- Space Viewer: stepping back to a PDF, text or other non-picture file showed an empty pane.
- Space Viewer: Del on a PDF, Office or other file with a Windows preview usually failed ("file in use"), because
  the preview (in the viewer, and in the main window's preview pane) still held it open. Both now let go of it
  first and don't reopen it while Windows asks about it; if it still can't be deleted, it is shown again.
- Turning a PNG that holds more than the picture (an animation, a colour profile, text) is refused: saving it
  again would have dropped that.
- Shortcuts: a damaged list left by a version before 1.1.015 (the old DualPane folder) could remove shortcuts
  from the shared list when it was merged; it is now ignored.

## 1.1.036
- The app is now called **OrclFX** (it was Orcl File Explorer): in its window, dialogs, the Start menu and
  Settings > Apps. The GitHub repository is now github.com/Oracooll/OrclFX (it was orcl-file-explorer; old links
  and the update check of earlier versions are redirected there).
- Nothing moves: the program stays in `%LOCALAPPDATA%\Programs\Orcl File Explorer`, so taskbar pins keep working,
  and settings, shortcuts and layouts stay where they are. Earlier versions update to this one as usual.

## 1.1.035
- New folder / New text file: the new item's name is always left open for typing, with "New folder" (or "New Text
  Document") selected. For a few seconds after it appears, until you type or click, the app keeps the rename box
  open and the keyboard in it: Windows' file list could close it when it redrew the new item (OneDrive changing
  its sync icon, for example). Auto-fit of the Name column also waits while a name is being typed.

## 1.1.034
- Space Viewer: the thumbnail pane is on the left. The viewer keeps the keyboard: clicking a thumbnail or stepping
  through pictures no longer lets the file list behind it take the keys. Page Up / Page Down move a screenful of
  thumbnails in thumbnails-only mode (with the viewer: previous / next picture).

## 1.1.033
Fixes from an audit loop (2 rounds; the last found no major problems):
- Space Viewer: zooming to 100 % after stepping back through several pictures could stay blurry (the full
  picture was loaded at screen size); a thumbnail pane made very wide on a big monitor could push the picture
  pane off a smaller screen; closing the main window while it waited for a copy could leave a viewer that no
  longer loaded anything; if the tab moved to another folder meanwhile, stepping could select a same-named file
  there; a thumbnail made while the picture was being turned could keep the old orientation. The viewer also
  frees its memory when closed.
- The size of a big selection is counted once the selection stops changing (holding Shift+arrow in a huge folder
  made the window sluggish).

## 1.1.032
Space Viewer (Space on a file; formerly Quick Look), with ideas from FastStone Image Viewer:
- Two panes: the viewer, and a thumbnail pane with every picture of the folder. Their buttons in the viewer's
  title bar turn each on or off, for viewer only or thumbnails only (also T and V). Ctrl+mouse wheel over the
  thumbnails makes them bigger or smaller, from 64 up to 512 pixels; click one to show it. The panes, the
  thumbnail size and the pane width are remembered.
- Zoom: mouse wheel at the pointer, 1 for 100 % (again: fit), 0 to fit, + and -; drag to move around. The full
  picture is loaded only when zooming in past the screen-sized copy.
- Full screen: F or Enter (Esc leaves it).
- The next and previous pictures are prepared in the background, so stepping with the arrows is instant.
- Picture details (I): size, megapixels, file size, date taken, camera, lens, focal length, aperture, shutter,
  ISO, and the zoom.
- Del moves the picture on screen to the Recycle Bin and shows the next one (Windows warns if it would be deleted
  for good; holding Del down doesn't delete more, and folders are never deleted from here).
- While the viewer is open, keys typed in the main window (renaming a file, the address bar) are left alone.
- Turn left / right ([ and ]) without losing quality: JPEG photos only get a new orientation tag (their picture
  data isn't touched); ordinary PNG and BMP pictures are turned pixel for pixel. Pictures that re-saving would
  change (GIF, TIFF, 16-bit or palette PNG, unusual JPEGs) aren't turned; you get a note instead. Holding the key
  down doesn't repeat it.
- In thumbnails-only mode the arrows move around the grid.

## 1.1.031
- Quick Look keeps the arrow keys (and Space / Esc) to itself while it's open, wherever the keyboard focus is:
  the file list behind it no longer moves on its own, and in a text preview the arrows also step through the
  files (Page Up / Page Down and the mouse wheel scroll the text). It also takes the keyboard back from
  Windows' preview handlers (PDF, Office …), which could keep it after loading a file.

## 1.1.030
- Picture previews show on the first click. Pictures (JPEG, PNG, BMP, GIF, TIFF) are now decoded by the app
  itself instead of asking Windows for a thumbnail, which often gave the file's icon until you came back to
  the file. They appear at once, sharp, and turned the right way up (camera rotation).
- Other files whose thumbnail Windows hasn't made yet (HEIC photos, videos …) show their icon first and are
  refreshed automatically as soon as the thumbnail is ready.

## 1.1.029
- Size of the selection: the status bar shows "3 selected (1.2 GB)". Selected folders are counted with
  everything in them, in the background at low priority ("…" while counting; "at least" when something
  couldn't be counted, such as a folder on a network share or a whole drive).
- Quick Look: press Space on a file for a large preview window (photos, PDFs, documents, text). The arrow keys
  step to the next or previous item in the list; Space or Esc closes it. Space still works for typing a name to
  jump to a file ("my notes").
- Previews of pictures are sharper on big screens (thumbnails up to 2560 pixels).

## 1.1.028
Fixes from an audit of the new features (2 rounds; the last found no major problems):
- Layouts: switching from the one-pane view could replace a hidden pane's tabs for good, and "Back to the tabs
  before …" brought back the wrong ones. Going back now restores every pane exactly, and leaves panes the
  switch didn't touch alone.
- New folder / New text file: the item is made in the background (an offline share no longer freezes the
  window); renaming no longer starts on a same-named item in another folder you moved to meanwhile, in a tab
  you've left, or after the tab was closed (which showed an error); two quick clicks make two items.
- Open terminal here works for folders with ";" in their name (Windows Terminal split the path there).
- Text preview: files with very long lines (minified .js / .json) are wrapped, so they don't make the preview slow.
- Address bar: a half-typed address survives switching to another window; double-clicking a folder in the path
  opens it without also copying the address.

## 1.1.027
- Clickable address bar: each folder of the path is a button (Ctrl+click or middle-click opens it in a new
  tab), and the arrow after it lists its subfolders to jump to. Folders that don't fit are under «.
  Click empty space in the address bar to copy the folder's full path (a pop-up confirms it); double-click
  it, or press Ctrl+L / F4, to type an address.
- New folder and New text file buttons next to Up: the item is created in the current folder and its name is
  ready to type.
- Show file name extensions: ☰ › View options, or Ctrl+E (the same setting as File Explorer's).
- Text and code preview: text files (logs, .json, .ps1, .cs, .csv …, and any other file that turns out to be
  text) are shown as text in the preview pane, with a Search box (Enter / F3 for the next match, Shift for the
  previous). Files only stored in OneDrive's cloud aren't downloaded for this; big files show their first 2 MB.

## 1.1.026
- Layouts: save the panes side by side, their widths and their tabs under a name (☰ › Layouts › Save these
  panes and tabs as a layout…) and switch between them from the same menu. Layouts are shared between your
  computers through OneDrive, like the shortcuts. After a switch, "Back to the tabs before …" undoes it.
- Open terminal here (Ctrl+Alt+T, or the menu / right-click a tab): Windows Terminal, or PowerShell where
  Windows Terminal isn't installed, opens in the tab's folder.
- Tab colours: right-click a tab › Colour to tint it red, orange, yellow, green, blue or purple. Colours are
  remembered with the tabs and saved in layouts.

## 1.1.025
Fixes from a second audit loop (5 rounds; the last found no major problems):
- A tab on a drive that isn't plugged in or a network share that is offline keeps its folder: it shows This PC
  meanwhile (with a note) and opens the folder again when you come back to the tab once it's available.
  Before, the tab was switched to This PC for good. Offline shares are checked in the background, so they
  no longer freeze the window at startup; shares that need a sign-in still open and let Windows ask.
- A damaged shortcuts list (an empty or garbled file after a crash or a sync glitch) is read from its backup
  instead of showing an empty list, and the next save no longer overwrites that good backup.
- If saving a settings or shortcuts file fails halfway (OneDrive or antivirus holding it), the file is put back
  instead of being left missing.
- A right-click on a title-bar button no longer presses it (a right-click on the close button closed the
  window; on a view button it also changed the current view).
- "Update to version…" clicked again during a download no longer starts a second update.
- A theme restart waits for the old window however long it takes to close (it could end with no window open).
- Left open for days, the app still checks for updates once a day; folder sizes aren't rescanned while you're
  away; a repeated save error is logged once instead of every few seconds.

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
