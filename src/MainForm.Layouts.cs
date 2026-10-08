// OrclFX: saved layouts (named sets of panes and tabs, shared through OneDrive), tab colours and
// opening a terminal in a folder.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    partial class MainForm
    {
        // The panes and tabs before the last layout switch, so the switch can be undone (this session only).
        // It holds all four panes (shown or not), with how many were shown.
        Layout beforeSwitch;
        int beforeShown, beforeTouched;   // panes shown before, panes the switch changed
        string beforeSwitchName;

        // Every pane (also the hidden ones, which keep their tabs) for undoing a switch.
        Layout CaptureAll()
        {
            Layout l = new Layout();
            l.PaneCount = Panes.Length;
            l.ActivePane = Math.Max(0, Array.IndexOf(Panes, ActivePane));
            for (int i = 0; i < Panes.Length; i++)
            {
                l.Weights[i] = row.Weights[i];
                l.ActiveTab[i] = Panes[i].ActiveIndex;
                foreach (BrowserTab t in Panes[i].Tabs) l.Tabs[i].Add(new TabSpec(t.SavedFolder, t.Locked, t.Color));
            }
            return l;
        }

        // The panes on screen, their widths and their tabs, as a layout.
        Layout CaptureLayout(string name)
        {
            Layout l = new Layout();
            l.Name = name;
            List<Pane> v = VisiblePanes();
            l.PaneCount = v.Count;
            l.ActivePane = Math.Max(0, v.IndexOf(ActivePane));
            for (int i = 0; i < v.Count; i++)
            {
                l.Weights[i] = row.Weights[Array.IndexOf(Panes, v[i])];
                l.ActiveTab[i] = v[i].ActiveIndex;
                foreach (BrowserTab t in v[i].Tabs) l.Tabs[i].Add(new TabSpec(t.SavedFolder, t.Locked, t.Color));
            }
            return l;
        }

        // Replaces the panes on screen and their tabs with the layout's (panes not in it keep their tabs).
        void ApplyLayout(Layout l, string undoLabel)
        {
            beforeSwitch = CaptureAll();
            beforeShown = PaneCount;
            beforeTouched = l.PaneCount;
            beforeSwitchName = undoLabel;
            Put(l, l.PaneCount, l.PaneCount);
        }

        // Undoes the last switch (and makes that undoable in turn).
        void SwitchBack()
        {
            if (beforeSwitch == null) return;
            Layout back = beforeSwitch;
            int shown = beforeShown, touched = Math.Max(beforeTouched, beforeShown);
            beforeSwitch = CaptureAll();
            beforeShown = PaneCount;
            beforeTouched = touched;
            Put(back, touched, shown); // only the panes the switch changed: the others keep their history
        }

        // The layout's first count panes go into panes 0..count-1; shown: how many panes are shown afterwards.
        void Put(Layout l, int count, int shown)
        {
            for (int i = 0; i < count && i < l.PaneCount; i++)
            {
                row.Weights[i] = l.Weights[i];
                Panes[i].ReplaceTabs(l.Tabs[i], l.ActiveTab[i]);
            }
            ActivePane = Panes[Math.Max(0, Math.Min(l.ActivePane, l.PaneCount - 1))];
            SetPaneCount(shown);
            row.PerformLayout();
            foreach (Pane p in Panes) p.ApplyActiveLook();
        }

        // The Layouts submenu of the main menu.
        public ToolStripMenuItem LayoutsMenu(ToolStripRenderer renderer)
        {
            ToolStripMenuItem menu = new ToolStripMenuItem("Layouts");
            menu.DropDown.Renderer = renderer;
            List<Layout> all;
            string problem = null;
            try { all = LayoutFile.Read(AppPaths.LayoutsFile); }
            catch (Exception ex) { all = new List<Layout>(); problem = ex.Message; }
            all.Sort(delegate(Layout a, Layout b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            foreach (Layout l in all)
            {
                Layout layout = l;
                ToolStripMenuItem it = new ToolStripMenuItem(l.Name, null, delegate
                {
                    ApplyLayout(layout, layout.Name);
                    Notice("Layout “" + layout.Name + "”: " + Describe(layout) + ".");
                });
                it.ShortcutKeyDisplayString = Describe(l);
                menu.DropDownItems.Add(it);
            }
            if (problem != null) menu.DropDownItems.Add(new ToolStripMenuItem("The layouts can't be read now: " + problem) { Enabled = false });
            else if (all.Count == 0) menu.DropDownItems.Add(new ToolStripMenuItem("No layouts saved yet") { Enabled = false });
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(new ToolStripMenuItem("Save these panes and tabs as a layout…", null, delegate { SaveLayoutAs(); }) { Enabled = problem == null });
            if (all.Count > 0)
            {
                ToolStripMenuItem del = new ToolStripMenuItem("Delete a layout");
                del.DropDown.Renderer = renderer;
                foreach (Layout l in all)
                {
                    string name = l.Name;
                    del.DropDownItems.Add(name, null, delegate { DeleteLayout(name); });
                }
                menu.DropDownItems.Add(del);
            }
            if (beforeSwitch != null)
                menu.DropDownItems.Add("Back to the tabs before “" + beforeSwitchName + "”", null, delegate
                {
                    SwitchBack();
                    Notice("Back to the tabs you had before.");
                });
            return menu;
        }

        static string Describe(Layout l)
        {
            int n = l.TabCount;
            return (l.PaneCount == 1 ? "1 pane" : l.PaneCount + " panes") + ", " + (n == 1 ? "1 tab" : n + " tabs");
        }

        void SaveLayoutAs()
        {
            string name = InputBox.Ask(this, "Save layout", "Name for these " + Describe(CaptureLayout("")) + " (for example Work or Photos):", "");
            if (name == null) return;
            name = LayoutFile.CleanName(name);
            if (name.Length == 0) { SystemSounds.Beep.Play(); return; }
            try
            {
                Layout existing = LayoutFile.Find(LayoutFile.Read(AppPaths.LayoutsFile), name);
                if (existing != null && MessageBox.Show(this, "Replace the layout “" + existing.Name + "” (" + Describe(existing) + ") with these panes and tabs?",
                    "Save layout", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                Layout l = CaptureLayout(existing != null ? existing.Name : name);
                LayoutFile.Save(AppPaths.LayoutsFile, l);
                Notice("Saved the layout “" + l.Name + "” (" + Describe(l) + "). Switch to it from the menu › Layouts.");
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
                MessageBox.Show(this, "The layout couldn't be saved: " + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void DeleteLayout(string name)
        {
            if (MessageBox.Show(this, "Delete the layout “" + name + "”? Your tabs stay as they are.", "Delete layout",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                LayoutFile.Delete(AppPaths.LayoutsFile, name);
                Notice("Deleted the layout “" + name + "”.");
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
                MessageBox.Show(this, "The layout couldn't be deleted: " + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---- tab colours

        public void SetTabColor(BrowserTab t, int color)
        {
            t.Color = LayoutFile.ClampColor(color);
            t.Pane.RefreshTabs();
            StateChanged();
        }

        // ---- new folder / text file

        public void CreateNew(Pane p, bool folder)
        {
            BrowserTab t = p.ActiveTab;
            string dir = t == null ? null : t.Address;
            bool usable = t != null && t.Created && !t.IsFindResults && PathParts.Split(dir).Count > 0 &&
                (Util.IsNetworkPath(dir) || Directory.Exists(dir));
            if (!usable)
            {
                SystemSounds.Beep.Play();
                Notice("New " + (folder ? "folders" : "files") + " can be made in a folder on a disk or network share" + (t != null ? " (" + t.Title + " isn't one)." : "."));
                return;
            }
            t.CreateNew(folder, delegate(string error) { Notice("\u26A0 Couldn't create a new " + (folder ? "folder" : "text file") + " here: " + error); });
        }

        // ---- terminal

        // Opens Windows Terminal (or PowerShell, without it) in the tab's folder.
        public void OpenTerminal(BrowserTab t)
        {
            string dir = t == null ? null : t.Address;
            bool usable = dir != null && Path.IsPathRooted(dir) && !t.IsFindResults &&
                (Util.IsNetworkPath(dir) || Directory.Exists(dir)); // a network folder isn't looked up on this thread
            if (!usable)
            {
                SystemSounds.Beep.Play();
                Notice("A terminal opens in a folder on a disk or network share" + (t != null ? " (" + t.Title + " isn't one)." : "."));
                return;
            }
            try
            {
                string wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe");
                ProcessStartInfo psi;
                if (File.Exists(wt))
                    // "C:\" would end the quoted argument with \" : "C:\." is the same folder. Windows Terminal
                    // splits its command line at ";" even inside quotes, unless written "\;".
                    psi = new ProcessStartInfo(wt, "-d \"" + (dir.EndsWith("\\") ? dir + "." : dir).Replace(";", "\\;") + "\"");
                else
                {
                    psi = new ProcessStartInfo("powershell.exe");
                    psi.WorkingDirectory = dir;
                }
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Notice("\u26A0 The terminal couldn't be opened: " + ex.Message);
            }
        }
    }
}
