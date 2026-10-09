# OrclFX

(Formerly Orcl File Explorer.)

A light multi-pane file manager for Windows, in the spirit of xplorer2, made for wide and ultrawide
screens. Every pane hosts the **real Windows Explorer view**, so thumbnails, right-click menus,
drag and drop, renaming, columns and shell extensions behave exactly as in File Explorer.

![OrclFX with three panes](docs/screenshot.png)

**[Download the latest release](https://github.com/Oracooll/OrclFX/releases/latest)**:
a single `orclfx.exe`, no installer package and no admin rights needed.

## Features

**Panes and tabs**
- One to four panes side by side (Alt+1 … Alt+4). Each pane has its own tabs, remembered between
  sessions; panes you hide keep their tabs for when you show them again.
- Locked tabs (right-click a tab › Lock) never leave their folder: opening a folder from one opens
  it in a new tab, placed after the group of locked tabs (tabs the app opens never land between two
  locked tabs; you can still drag tabs anywhere).
- The active pane, the one you used last, is framed in your accent colour. The first click on an
  inactive pane only activates it.
- Tab colours (right-click a tab › Colour) tell similar tabs apart.
- **Layouts** (☰ › Layouts): save the panes, their widths and their tabs under a name, such as Work or
  Photos, and switch between them. They're shared between your computers through OneDrive, and a switch can
  be undone from the same menu.
- **Open terminal here** (Ctrl+Alt+T): Windows Terminal (or PowerShell) in the tab's folder.
- **Address bar**: click a folder of the path to go there, or the arrow after it to pick a subfolder.
  Click empty space in it to copy the folder's path; double-click it (or Ctrl+L) to type an address.
- **New folder** and **New text file** buttons next to Up create the item and let you type its name; the
  **Refresh** button next to them reads the folder again (Ctrl+R, F5).
- **Space Viewer**: press Space on a file for a large view with thumbnails on its left of the folder's pictures (each pane
  can be turned off in its title bar; Ctrl+wheel resizes the thumbnails up to 512 px). Wheel to zoom, drag to
  move, 1 for 100 %, F for full screen, I for the picture's details, [ and ] to turn it without losing quality,
  Del to move it to the Recycle Bin; the arrows step through the folder, Space or Esc closes it.
- The status bar shows how big the selection is, folders included (counted in the background).
- Double-click empty space in a file list to go up one level.
- A tab on an unplugged drive or an offline share keeps its folder: it shows This PC until the folder is back,
  and opens it when you return to the tab.
- Drag a divider to resize: only that divider moves, and the panes to its right share the change
  equally. Double-click a divider to make all panes the same width.

**Side panes** (title bar or Alt+T / Alt+P / Alt+S)
- **Tree**: one folder tree that follows the active pane.
- **Preview**: previews the active pane's selected file with the Windows preview handlers
  (PDF, Office, images …). Text and code files are shown as text with a Search box. Handlers run outside the app, as in File Explorer; files without an out-of-process handler show a thumbnail.
- **Shortcuts**: a strip of favourite folders. Drop folders onto it, double-click to open
  (Ctrl+double-click or middle-click for a new tab), F2 renames the real folder, sort icons on its
  header. The list is stored in your OneDrive (Documents\OrclFX), so all your computers share it; shortcuts to folders
  that don't exist on the current computer are dimmed and listed in a warning line.

**Find** (magnifier in the title bar, Ctrl+F or F3)
- Searches the folder open in the active pane and all its subfolders, and shows the results in a new
  tab of that pane, in the real Explorer list: open, right-click, preview and sort them as usual, with
  a Folder path column showing where each one is. Double-clicking a folder in the results opens it in
  that tab; Back or Up returns to the searched folder.
- Type part of a name (`report`), or patterns such as `*.pdf;*.docx`; folders that match are found
  too. The app's own search runs in the background, works everywhere (also in folders Windows doesn't
  index and on network drives), shows results as they come and can be stopped; it doesn't follow
  folder links, so nothing is found twice.
- **Also search inside files** hands the search to Windows Search, like File Explorer's search box:
  it finds words inside documents (Office, PDF, text …) in folders Windows indexes, and understands
  filters such as `kind:music`, `size:>10MB` or `date:this week`.
- Recent searches are remembered.

**View**
- All eight view modes have a button in the title bar (Details, List, Tiles, Content, Small, Medium, Large and
  Extra large icons), in the order Ctrl+mouse wheel goes through them; also in ☰ › View mode. Right-click a view button to make it the default for every folder you open (a green
  tick marks it); right-click it again to go back to each folder's own remembered view.
- Light, dark or match-Windows theme from the title bar.
- ☰ › View options: show hidden files (Ctrl+H), file name extensions (Ctrl+E), auto-fit the Name column, natural number sorting,
  and folder sizes. Folder sizes are calculated in the background at low priority, skip network
  drives, never download OneDrive files, and turn themselves off if a scan gets out of hand.

## Keyboard shortcuts

| Keys | Action |
|---|---|
| Double-click empty space, Backspace, Alt+Up | Up one level |
| Alt+Left / Alt+Right | Back / Forward |
| Tab | Next pane |
| Ctrl+F, F3 | Find in this folder and its subfolders |
| Ctrl+T / Ctrl+W | New tab / close tab (middle-click a tab also closes it) |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+L, Alt+D, F4 | Edit the address |
| Ctrl+Alt+T | Open a terminal in this folder |
| Alt+1 … Alt+4 | One to four panes |
| Alt+T / Alt+P / Alt+S | Tree / Preview / Shortcuts pane |
| Ctrl+H | Show or hide hidden files |
| Ctrl+E | Show or hide file name extensions |
| Ctrl+R, F5 | Refresh the folder |
| Ctrl+mouse wheel | Step through all views, smallest to largest (Details, List, Tiles, Content, Small, Medium, Large, Extra large icons) |
| Space | Space Viewer (a large view of the selected file, with thumbnails) |

## Install

1. Download `orclfx.exe` from the [releases page](https://github.com/Oracooll/OrclFX/releases/latest).
2. Run it and choose **Yes** to install for your Windows account. It goes to
   `%LOCALAPPDATA%\Programs\Orcl File Explorer`, appears in the Start menu, and can be removed from
   Settings › Apps.
3. Optional: right-click its taskbar icon › Pin to taskbar.

Once the [winget listing](https://github.com/microsoft/winget-pkgs/pull/446536) is approved, it can also be
installed with `winget install Oracooll.OrclFileExplorer` (and updated with `winget upgrade`).

**Updates:** the main menu (click the app icon at the top left, or the menu button in a pane) has
**Check for updates…**. It finds the newest release here on GitHub, checks the download (size,
SHA-256 checksum and version), saves your tabs, replaces the program and restarts it. Once a day the
app also checks by itself and shows a note in the status bar when a new version is out (it never
installs without asking; turn this off with **Check for updates automatically** in the menu).
Running a newer `orclfx.exe` by hand (or `orclfx.exe --install`) updates the installed copy too.
`orclfx.exe --portable` runs it without installing. `orclfx.exe --install --quiet` and
`orclfx.exe --uninstall --quiet` install or remove it without any windows (the exit code says whether it
worked); a quiet uninstall keeps your settings.

Windows SmartScreen may warn about a downloaded unsigned program: choose **More info › Run anyway**.
That's only needed once: installing (and updating) removes the browser's "downloaded from the internet"
mark from the installed program, so Windows doesn't ask "The publisher could not be verified" on every start.

**Requirements:** Windows 10 or 11 with .NET Framework 4.8, which Windows includes.

## Build from source

No SDK or IDE needed: the build uses the C# compiler that ships with Windows (.NET Framework 4.x).

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

This produces `dist\orclfx.exe`. The icon is generated from `Icon.png` by `src\make-icon.ps1`.

The code is C# 5 with Windows Forms and the Shell COM interfaces, in [`src/`](src):

| Folder | What's in it |
| --- | --- |
| `src/` | `Program.cs` (startup), `MainForm.cs` (main window), `Installer.cs`, `AssemblyInfo.cs` (version) |
| `src/Core/` | Logic with no UI: shortcut-list merging, pane widths, the settings file, folder sizes, path and file helpers |
| `src/Panes/` | Explorer tabs, file panes, tree, preview and shortcuts panes |
| `src/UI/` | Title bar, tab strip, theme colours, splitters and other controls |
| `src/Interop/` | Windows API and Shell COM declarations |

### Tests

```
powershell -ExecutionPolicy Bypass -File test.ps1
```

Builds the app and the tests in `tests/` into `dist\tests\` and runs them. The tests cover the
`src/Core` logic and the preview worker. They need no extra packages and open no windows. Add `-Smoke` to also start the app
with throw-away settings and check that it opens, saves its settings and closes cleanly. GitHub runs
the tests on every push.

## Where things are stored

| What | Where |
|---|---|
| Tabs, layout and settings (this computer only) | `%APPDATA%\OrclFX\state.txt` (with a `.bak` copy) |
| Shortcuts list (shared by your computers) | `Documents\OrclFX\shortcuts.txt` in your OneDrive (see below) |
| Saved layouts (shared by your computers) | `layouts.txt` in the same folder |
| Error log | `%APPDATA%\OrclFX\errors.log` |

The shared folder is `Documents\OrclFX` when Documents is backed up to OneDrive, or
`OneDrive\Documents\OrclFX` when your OneDrive has a Documents folder (so computers with and without the
backup share it). Without either, the list stays on that computer in `%APPDATA%\OrclFX`.
Versions up to 1.1.014 used `DualPane` folders. Newer versions move the settings and merge the old shortcuts
list into the new one, again whenever a computer still on an old version has changed it, so nothing is lost
while your computers update one by one. Once all are updated, the old `OneDrive\DualPane` folder can be
deleted. When OneDrive keeps two versions of the list (two computers changed it before syncing, leaving a
`shortcuts-<computer>.txt` copy), the copy is merged in and removed.

## Notes

- Switching between light and dark restarts the window, keeping your tabs and layout, because
  Windows applies some light/dark choices only when an app starts.
- Only one window runs at a time; launching the app again brings the open window to the front.
- See [CHANGELOG.md](CHANGELOG.md) for the version history, and [IDEAS.md](IDEAS.md) for suggestions kept for later. Versions are `major.minor.build` with a
  three-digit build (1.1.042).

## License

[PolyForm Noncommercial 1.0.0](LICENSE.md): free to use, modify and share for any noncommercial
purpose (personal use, study, hobby projects, charities, schools, public bodies). Selling it or
using it to make money is not permitted. Because it restricts commercial use, this is
source-available software rather than "open source" in the OSI sense.
