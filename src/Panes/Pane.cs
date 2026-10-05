// Orcl File Explorer: A file pane: tab strip, address bar and the tabs' Explorer views.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OrclFileExplorer
{
    class Pane : Panel
    {
        public readonly MainForm Main;
        public readonly List<BrowserTab> Tabs = new List<BrowserTab>();
        int active = -1;
        readonly TabStrip strip;
        readonly Panel addrBar = new Panel(), addrBorder = new Panel(), addrInner = new Panel(), content = new Panel();
        readonly Panel spacerL = new Panel(), spacerR = new Panel();
        readonly TextBox addr = new TextBox();
        readonly GlyphButton back, fwd, up, menuBtn;

        public Pane(MainForm main)
        {
            Main = main;
            Dock = DockStyle.Fill;
            Padding = new Padding(Native.Px(2));
            strip = new TabStrip(this);
            content.Dock = DockStyle.Fill;

            back = new GlyphButton("", "Back (Alt+Left)", DockStyle.Left);
            fwd = new GlyphButton("", "Forward (Alt+Right)", DockStyle.Left);
            up = new GlyphButton("", "Up one level (Backspace, or double-click empty space)", DockStyle.Left);
            menuBtn = new GlyphButton("", "Menu", DockStyle.Right);
            back.Click += delegate { Nav(Native.SBSP_NAVIGATEBACK); };
            fwd.Click += delegate { Nav(Native.SBSP_NAVIGATEFORWARD); };
            up.Click += delegate { Nav(Native.SBSP_PARENT); };
            menuBtn.Click += delegate { ShowMainMenu(); };

            addrBar.Dock = DockStyle.Top;
            addrBar.Height = Native.Px(38);
            addrBar.Padding = new Padding(Native.Px(4), Native.Px(5), Native.Px(4), Native.Px(5));
            spacerL.Dock = DockStyle.Left; spacerL.Width = Native.Px(6);
            spacerR.Dock = DockStyle.Right; spacerR.Width = Native.Px(4);
            addrBorder.Dock = DockStyle.Fill;
            addrBorder.Padding = new Padding(1);
            addrInner.Dock = DockStyle.Fill;
            addr.BorderStyle = BorderStyle.None;
            addr.KeyDown += AddrKeyDown;
            addrInner.Controls.Add(addr);
            addrInner.Resize += delegate { addr.SetBounds(Native.Px(8), (addrInner.Height - addr.Height) / 2, Math.Max(10, addrInner.Width - Native.Px(16)), addr.Height); };
            addrBorder.Controls.Add(addrInner);
            // Dock order: the last control added docks first.
            addrBar.Controls.Add(addrBorder);
            addrBar.Controls.Add(spacerR);
            addrBar.Controls.Add(menuBtn);
            addrBar.Controls.Add(spacerL);
            addrBar.Controls.Add(up);
            addrBar.Controls.Add(fwd);
            addrBar.Controls.Add(back);

            Controls.Add(content);
            Controls.Add(addrBar);
            Controls.Add(strip);
        }

        public int ActiveIndex { get { return active; } }
        public BrowserTab ActiveTab { get { return active >= 0 && active < Tabs.Count ? Tabs[active] : null; } }
        public bool IsActivePane { get { return Main.ActivePane == this; } }

        public void ApplyTheme()
        {
            addrBar.BackColor = spacerL.BackColor = spacerR.BackColor = content.BackColor = Theme.Window;
            addrInner.BackColor = addr.BackColor = Theme.Input;
            foreach (BrowserTab t in Tabs) t.Host.BackColor = Theme.Window;
            ApplyActiveLook();
            back.Invalidate(); fwd.Invalidate(); up.Invalidate(); menuBtn.Invalidate();
        }

        public void ApplyActiveLook()
        {
            // The active pane gets an accent frame, an accent address box and full-strength text;
            // the inactive one is unframed with dimmed address text.
            bool on = IsActivePane;
            BackColor = on ? Theme.Accent : Theme.Window;
            addrBorder.BackColor = on ? Theme.Accent : Theme.Border;
            addr.ForeColor = on ? Theme.Text : Theme.TextDim;
            strip.Invalidate();
        }

        public BrowserTab AddTab(string folder, bool locked, bool activate, bool atEnd = false)
        {
            BrowserTab t = new BrowserTab(this, folder, locked);
            int at = atEnd || active < 0 ? Tabs.Count : TabOrder.InsertIndex(Tabs.ConvertAll(delegate(BrowserTab x) { return x.Locked; }), active);
            Tabs.Insert(at, t);
            if (active >= at) active++;
            content.Controls.Add(t.Host);
            if (activate || active < 0) Select(at);
            else strip.Invalidate();
            Main.StateChanged();
            return t;
        }

        public void NewTab()
        {
            BrowserTab t = ActiveTab;
            AddTab(t != null ? t.Folder : Native.ThisPC, false, true);
        }

        public void Select(int i)
        {
            if (i < 0 || i >= Tabs.Count) return;
            BrowserTab old = ActiveTab;
            active = i;
            Main.SetActivePane(this);
            ShowActive();
            if (old != null && old != ActiveTab) old.Host.Visible = false;
            strip.Invalidate();
            if (Main.Ready)
            {
                BrowserTab t = ActiveTab;
                BeginInvoke((MethodInvoker)t.Activate);
            }
            Main.StateChanged();
        }

        public void ShowActive()
        {
            BrowserTab t = ActiveTab;
            if (t == null || !Main.Ready) return;
            t.Host.Visible = true;
            t.Host.BringToFront();
            t.EnsureCreated();
            t.Resize();
            addr.Text = t.Address;
            Main.UpdateStatus();
            if (IsActivePane) Main.ActiveFolderChanged();
        }

        public void CloseTab(BrowserTab t)
        {
            if (t.Locked || Tabs.Count <= 1) { SystemSounds.Beep.Play(); return; }
            int i = Tabs.IndexOf(t);
            BrowserTab cur = ActiveTab;
            Tabs.RemoveAt(i);
            if (t == cur) { active = -1; Select(Math.Min(i, Tabs.Count - 1)); }
            else active = Tabs.IndexOf(cur);
            t.Destroy();
            content.Controls.Remove(t.Host);
            t.Host.Dispose();
            if (t.Icon != null) { t.Icon.Dispose(); t.Icon = null; }
            strip.Invalidate();
            Main.StateChanged();
        }

        public void MoveTab(int from, int to)
        {
            if (from < 0 || from >= Tabs.Count || to < 0 || to >= Tabs.Count) return;
            BrowserTab cur = ActiveTab, t = Tabs[from];
            Tabs.RemoveAt(from);
            Tabs.Insert(to, t);
            active = Tabs.IndexOf(cur);
            strip.Invalidate();
            Main.StateChanged();
        }

        public void CycleTab(int d)
        {
            if (Tabs.Count > 1) Select((active + d + Tabs.Count) % Tabs.Count);
        }

        public void RefreshTabs() { strip.Invalidate(); }

        public void ToggleLock(BrowserTab t)
        {
            t.Locked = !t.Locked;
            t.LockedFolder = t.Locked ? t.Folder : null;
            strip.Invalidate();
            Main.StateChanged();
        }

        public void Nav(uint flags)
        {
            BrowserTab t = ActiveTab;
            if (t != null && t.Created) { t.Nav(flags); t.Activate(); }
        }

        public void TabNavigated(BrowserTab t)
        {
            Main.ApplyDefaultView(t); // a default view chosen in the title bar always wins
            if (t == ActiveTab)
            {
                if (!addr.Focused) addr.Text = t.Address;
                Main.UpdateStatus();
                if (IsActivePane) Main.ActiveFolderChanged();
            }
            strip.Invalidate();
            Main.StateChanged();
        }

        public void FocusAddress()
        {
            addr.Focus();
            addr.SelectAll();
        }

        void AddrKeyDown(object sender, KeyEventArgs e)
        {
            BrowserTab t = ActiveTab;
            if (t == null) return;
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                string p = Environment.ExpandEnvironmentVariables(addr.Text.Trim().Trim('"'));
                if (p.Length == 0) return;
                if (!t.Navigate(p)) { SystemSounds.Beep.Play(); return; }
                t.Activate();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                addr.Text = t.Address;
                t.Activate();
            }
        }

        public void ShowTabMenu(BrowserTab t, Point screen)
        {
            ContextMenuStrip m = Main.NewMenu();
            if (t != null)
            {
                BrowserTab tab = t;
                m.Items.Add(tab.Locked ? "Unlock tab" : "Lock tab to this folder", null, delegate { ToggleLock(tab); });
                m.Items.Add("Duplicate tab", null, delegate { AddTab(tab.Folder, false, true); });
                m.Items.Add(Main.PaneCount > 2 ? "Open in next pane" : "Open in other pane", null, delegate { Main.Other(this).AddTab(tab.Folder, false, true); });
                ToolStripItem add = m.Items.Add("Add to Shortcuts", null, delegate { Main.Shortcuts.Add(tab.Address, tab.Title); });
                add.Enabled = Util.IsNetworkPath(tab.Address) ? Path.IsPathRooted(tab.Address) : Directory.Exists(tab.Address);
                m.Items.Add(new ToolStripSeparator());
                ToolStripItem close = m.Items.Add("Close tab", null, delegate { CloseTab(tab); });
                close.Enabled = !tab.Locked && Tabs.Count > 1;
                m.Items.Add("Close other unlocked tabs", null, delegate
                {
                    Select(Tabs.IndexOf(tab));
                    foreach (BrowserTab x in Tabs.ToArray()) if (x != tab && !x.Locked) CloseTab(x);
                });
                m.Items.Add(new ToolStripSeparator());
            }
            m.Items.Add("New tab", null, delegate { NewTab(); });
            m.Show(screen);
        }

        void ShowMainMenu() { ShowMainMenu(menuBtn, new Point(menuBtn.Width, menuBtn.Height), ToolStripDropDownDirection.BelowLeft); }

        // The main menu (this pane's tab commands plus the app-wide ones), shown at a point of anchor.
        public void ShowMainMenu(Control anchor, Point at, ToolStripDropDownDirection direction)
        {
            ContextMenuStrip m = Main.NewMenu();
            BrowserTab t = ActiveTab;
            AddItem(m.Items, "New tab", "Ctrl+T", delegate { NewTab(); });
            if (t != null) AddItem(m.Items, t.Locked ? "Unlock this tab" : "Lock this tab to this folder", null, delegate { ToggleLock(t); });
            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem tree = AddItem(m.Items, "Tree pane", "Alt+T", delegate { Main.SetShowTree(!Main.ShowTree); });
            tree.Checked = Main.ShowTree;
            ToolStripMenuItem preview = AddItem(m.Items, "Preview pane", "Alt+P", delegate { Main.SetShowPreview(!Main.ShowPreview); });
            preview.Checked = Main.ShowPreview;
            ToolStripMenuItem shortcuts = AddItem(m.Items, "Show Shortcuts pane", null, delegate { Main.SetShowShortcuts(!Main.ShowShortcuts); });
            shortcuts.Checked = Main.ShowShortcuts;
            ToolStripMenuItem viewMenu = new ToolStripMenuItem("View mode");
            int curMode = 0, curSize = 0;
            bool haveMode = t != null && t.Created && t.GetViewMode(out curMode, out curSize);
            for (int i = 0; i < MainForm.AllViewNames.Length; i++)
            {
                int vm = MainForm.AllViewModes[i, 0], vs = MainForm.AllViewModes[i, 1];
                ToolStripMenuItem it = AddItem(viewMenu.DropDownItems, MainForm.AllViewNames[i], "Ctrl+Shift+" + (i + 1), delegate { Main.SetViewMode(vm, vs); });
                it.Checked = haveMode && curMode == vm && (vm != 1 || (vs == 256 ? curSize > 160 : vs == 96 ? curSize > 64 && curSize <= 160 : curSize <= 64));
                it.Enabled = haveMode;
            }
            viewMenu.DropDown.Renderer = m.Renderer;
            m.Items.Add(viewMenu);
            ToolStripMenuItem panesMenu = new ToolStripMenuItem("Panes side by side");
            string[] paneNames = { "One pane", "Two panes", "Three panes", "Four panes" };
            for (int i = 0; i < 4; i++)
            {
                int count = i + 1;
                ToolStripMenuItem it = AddItem(panesMenu.DropDownItems, paneNames[i], "Alt+" + count, delegate { Main.SetPaneCount(count); });
                it.Checked = Main.PaneCount == count;
            }
            panesMenu.DropDown.Renderer = m.Renderer;
            m.Items.Add(panesMenu);
            ToolStripMenuItem view = new ToolStripMenuItem("View options");
            ToolStripMenuItem hidden = AddItem(view.DropDownItems, "Show hidden files", "Ctrl+H", delegate { Main.ToggleHidden(); });
            hidden.Checked = Native.GetShowHidden();
            ToolStripMenuItem fit = AddItem(view.DropDownItems, "Auto-fit Name column", null, delegate { Main.SetAutoFit(!Main.AutoFit); });
            fit.Checked = Main.AutoFit;
            ToolStripMenuItem natural = AddItem(view.DropDownItems, "Natural number sorting (2 before 10)", null, delegate { Main.ToggleNaturalSort(); });
            natural.Checked = Native.GetNaturalSort();
            natural.Enabled = !Native.NaturalSortForcedByAdmin();
            ToolStripMenuItem sizes = AddItem(view.DropDownItems, "Folder sizes", null, delegate { Main.ToggleFolderSizes(); });
            sizes.Checked = Main.FolderSizes;
            view.DropDown.Renderer = m.Renderer;
            m.Items.Add(view);
            ToolStripMenuItem theme = new ToolStripMenuItem("Theme");
            string[] names = { "Match Windows", "Light", "Dark" };
            for (int i = 0; i < names.Length; i++)
            {
                int mode = i;
                ToolStripMenuItem it = AddItem(theme.DropDownItems, names[i], null, delegate { Main.SetThemeMode(mode); });
                it.Checked = Theme.Mode == i;
            }
            theme.DropDown.Renderer = m.Renderer;
            m.Items.Add(theme);
            m.Items.Add(new ToolStripSeparator());

            if (!Installer.IsRunningInstalledCopy())
                AddItem(m.Items, "Install on this computer…", null, delegate { Installer.InstallFromMenu(); });
            Updater.Release update = Main.AvailableUpdate;
            AddItem(m.Items, update != null ? "Update to version " + Util.FormatVersion(update.Version) + "…" : "Check for updates…", null,
                delegate { if (update != null) Main.OfferUpdate(update); else Main.CheckForUpdates(true); });
            ToolStripMenuItem auto = AddItem(m.Items, "Check for updates automatically", null, delegate { Main.SetAutoUpdateCheck(!Main.AutoUpdateCheck); });
            auto.Checked = Main.AutoUpdateCheck;
            AddItem(m.Items, "Keyboard shortcuts", null, delegate { Main.ShowHelp(); });
            AddItem(m.Items, "About " + Program.AppName, null, delegate
            {
                MessageBox.Show(Main, Program.AppName + " " + Installer.Version + "\n\nA light multi-pane file manager built on the Windows Explorer view.\n\nSettings: " + MainForm.StateFile + "\nShortcuts (shared through OneDrive): " + ShortcutsPane.ListFile,
                    "About " + Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            m.Show(anchor, at, direction);
        }

        static ToolStripMenuItem AddItem(ToolStripItemCollection items, string text, string keys, EventHandler click)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text, null, click);
            if (keys != null) it.ShortcutKeyDisplayString = keys;
            items.Add(it);
            return it;
        }
    }
}
