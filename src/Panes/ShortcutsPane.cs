// Orcl File Explorer: Shortcuts pane: the shared list of saved folders along the bottom.
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
    class ShortcutsPane : Panel
    {
        readonly MainForm main;
        readonly Label header = new Label();
        readonly Panel headerBar = new Panel();
        // As arranged, Name A-Z, Name Z-A, Folder path A-Z. Sorting only changes the display: the shared
        // file keeps the arranged order (each item's Name holds its arranged position).
        readonly GlyphButton[] sortButtons = new GlyphButton[4];
        static readonly string[] SortNames = { "As arranged", "Name A to Z", "Name Z to A", "Folder path A to Z" };
        int sortMode, nextSeq;
        readonly ListView list = new ListView();
        readonly ImageList icons = new ImageList();
        int widthValue;        // 0 = fit the longest name
        bool widthInChars;
        readonly Label notice = new Label();
        readonly HashSet<string> unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FileSystemWatcher watcher;
        readonly Timer reloadTimer = new Timer();
        bool loadedOk;                 // the shared list has been read (or didn't exist yet)
        DateTime knownStamp;           // last-write time of the file as we last read or wrote it
        bool dirty;                    // changes made here that aren't in the shared file yet
        List<KeyValuePair<string, string>> pendingLegacy;
        // The list as last read from / written to the shared file (label, path), arranged order: the common
        // ancestor for merging changes made here with changes made on another computer meanwhile.
        List<KeyValuePair<string, string>> baseEntries = new List<KeyValuePair<string, string>>();
        string saveError;
        readonly Timer saveRetry = new Timer();
        bool checking, checkAgain;
        ListViewItem editingItem;
        // While editing a label: true = the edit renames the real folder, false = only the shortcut's label.
        bool renamingFolder, editRequested;
        string labelBeforeEdit;
        readonly HashSet<string> iconsPending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Folder icons are looked up on a background STA thread (the shell needs COM, and a slow or offline
        // drive mustn't freeze the window). Finished icons wait in iconsReady until the list can show them.
        readonly Queue<string> iconQueue = new Queue<string>();
        readonly List<KeyValuePair<string, Icon>> iconsReady = new List<KeyValuePair<string, Icon>>();
        bool iconWorkerRunning, iconsClosed;

        // The list lives in OneDrive (when present) so every computer signed in to it shares the same shortcuts.
        // Old per-computer shortcuts still waiting to be moved into the shared file (kept in state.txt until then).
        public List<KeyValuePair<string, string>> PendingLegacy { get { return pendingLegacy; } }

        // For closing: saves changes still waiting for a retry. False if they couldn't be saved.
        public bool FlushPending()
        {
            if (!dirty) return true;
            bool ok = SaveList();
            ShowAvailability();
            return ok;
        }

        public static readonly string ListFile = Environment.GetEnvironmentVariable("DUALPANE_SHORTCUTS") ?? Path.Combine(
            Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetEnvironmentVariable("OneDriveConsumer") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            "DualPane", "shortcuts.txt");

        public string NoticeText { get { return notice.Visible ? notice.Text : null; } }

        public ShortcutsPane(MainForm m)
        {
            main = m;
            Dock = DockStyle.Fill;
            headerBar.Dock = DockStyle.Top;
            headerBar.Height = Native.Px(24);
            header.Dock = DockStyle.Fill;
            header.Padding = new Padding(Native.Px(8), 0, 0, 0);
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.AutoEllipsis = true;
            header.Text = "Shortcuts  ·  drop folders here to add them, double-click to open (Ctrl+double-click or middle-click: new tab), F2 renames the folder, right-click for more";
            header.UseMnemonic = false;
            headerBar.Controls.Add(header);
            for (int i = sortButtons.Length - 1; i >= 0; i--) // docked right: the last added sits furthest right
            {
                int mode = i;
                sortButtons[i] = new GlyphButton("", "Sort shortcuts: " + SortNames[i], DockStyle.Right);
                sortButtons[i].Width = Native.Px(28);
                sortButtons[i].Painter = delegate(Graphics g, Rectangle r, Color col) { DrawSortIcon(g, r, col, mode); };
                sortButtons[i].Click += delegate { SetSortMode(mode); };
            }
            for (int i = 0; i < sortButtons.Length; i++) headerBar.Controls.Add(sortButtons[i]);
            sortButtons[0].Checked = true;

            icons.ColorDepth = ColorDepth.Depth32Bit;
            icons.ImageSize = new Size(Native.Px(16), Native.Px(16));
            list.Dock = DockStyle.Fill;
            list.View = View.List;
            list.BorderStyle = BorderStyle.None;
            list.SmallImageList = icons;
            list.MultiSelect = false;
            list.LabelEdit = true;
            list.ShowItemToolTips = true;
            list.AllowDrop = true;
            list.HideSelection = true;
            // A single click only selects; double-click or Enter opens (Ctrl: in a new tab).
            list.ItemActivate += delegate
            {
                if (list.SelectedItems.Count == 0) return;
                ListViewItem it = list.SelectedItems[0];
                if (unavailable.Contains((string)it.Tag)) { SystemSounds.Beep.Play(); CheckAvailability(); return; }
                main.OpenFolder((string)it.Tag, (ModifierKeys & Keys.Control) != 0 ? 1 : 0);
            };
            list.MouseDown += delegate(object s, MouseEventArgs e)
            {
                ListViewItem it = list.GetItemAt(e.X, e.Y);
                if (e.Button == MouseButtons.Middle && it != null) main.OpenFolder((string)it.Tag, 1);
            };
            list.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) ShowMenu(list.GetItemAt(e.X, e.Y), list.PointToScreen(e.Location));
            };
            list.KeyDown += delegate(object s, KeyEventArgs e)
            {
                ListViewItem it = list.SelectedItems.Count > 0 ? list.SelectedItems[0] : null;
                if (it == null) return;
                if (e.KeyCode == Keys.Delete) { Remove(it); e.Handled = true; }
                else if (e.KeyCode == Keys.F2) { BeginRename(it, true); e.Handled = true; }
            };
            list.AfterLabelEdit += AfterLabelEdit;
            // Editing starts only from F2 or the menu, never from a slow click on a selected item.
            list.BeforeLabelEdit += delegate(object s, LabelEditEventArgs e) { if (!editRequested) e.CancelEdit = true; editRequested = false; };
            list.DragEnter += ListDragOver;
            list.DragOver += ListDragOver;
            list.DragDrop += ListDragDrop;
            // Theme and width are applied once the list has finished creating its items (not during creation).
            list.HandleCreated += delegate { list.BeginInvoke((MethodInvoker)delegate { ApplyTheme(); ApplyWidth(); }); };

            notice.Dock = DockStyle.Bottom;
            notice.Height = Native.Px(22);
            notice.Padding = new Padding(Native.Px(8), 0, Native.Px(8), 0);
            notice.TextAlign = ContentAlignment.MiddleLeft;
            notice.AutoEllipsis = true;
            notice.UseMnemonic = false;
            notice.Visible = false;
            reloadTimer.Interval = 700;
            reloadTimer.Tick += delegate { reloadTimer.Stop(); LoadList(); };
            saveRetry.Interval = 5000;
            saveRetry.Tick += delegate { if (SaveList()) saveRetry.Stop(); ShowAvailability(); };

            Controls.Add(list);
            Controls.Add(notice);
            Controls.Add(headerBar);
            list.ListViewItemSorter = new ShortcutSorter(0);
        }

        void QueueIcon(string path)
        {
            lock (iconQueue)
            {
                iconQueue.Enqueue(path);
                if (iconWorkerRunning) return;
                iconWorkerRunning = true;
            }
            System.Threading.Thread th = new System.Threading.Thread(IconWorker);
            th.IsBackground = true;
            th.SetApartmentState(System.Threading.ApartmentState.STA);
            th.Start();
        }

        void IconWorker()
        {
            while (true)
            {
                string path;
                lock (iconQueue)
                {
                    if (iconQueue.Count == 0) { iconWorkerRunning = false; break; }
                    path = iconQueue.Dequeue();
                }
                Icon ic = null;
                IntPtr pidl = Native.ParsePath(path);
                if (pidl != IntPtr.Zero)
                    try { ic = Native.SmallIcon(pidl); } catch { } finally { Marshal.FreeCoTaskMem(pidl); }
                lock (iconsReady)
                {
                    // The pane was closed meanwhile: nobody will use the icon.
                    if (iconsClosed) { if (ic != null) ic.Dispose(); continue; }
                    iconsReady.Add(new KeyValuePair<string, Icon>(path, ic));
                }
                // Before the window exists, OnHandleCreated picks the icons up instead.
                if (IsHandleCreated) try { BeginInvoke((MethodInvoker)ApplyIcons); } catch { }
            }
        }

        void ApplyIcons()
        {
            List<KeyValuePair<string, Icon>> ready;
            lock (iconsReady) { ready = new List<KeyValuePair<string, Icon>>(iconsReady); iconsReady.Clear(); }
            if (ready.Count == 0 || IsDisposed) return;
            foreach (KeyValuePair<string, Icon> r in ready)
            {
                iconsPending.Remove(r.Key);
                // the ImageList keeps using the icon until its handle exists: don't dispose it here
                if (r.Value != null && !icons.Images.ContainsKey(r.Key)) icons.Images.Add(r.Key, r.Value);
            }
            Program.Trace("shortcut icons: " + icons.Images.Count + " of " + list.Items.Count);
            list.Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyIcons();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); watcher = null; }
                reloadTimer.Dispose();
                saveRetry.Dispose();
                lock (iconQueue) iconQueue.Clear();
                lock (iconsReady)
                {
                    iconsClosed = true;
                    foreach (KeyValuePair<string, Icon> r in iconsReady) if (r.Value != null) r.Value.Dispose();
                    iconsReady.Clear();
                }
                icons.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---- sorting

        public int SortMode { get { return sortMode; } set { SetSortMode(value, false); } }

        void SetSortMode(int mode) { SetSortMode(mode, true); }

        void SetSortMode(int mode, bool save)
        {
            sortMode = Math.Max(0, Math.Min(SortNames.Length - 1, mode));
            for (int i = 0; i < sortButtons.Length; i++) sortButtons[i].Checked = i == sortMode;
            list.ListViewItemSorter = new ShortcutSorter(sortMode);
            ApplyWidth();
            if (save) main.StateChanged();
        }

        class ShortcutSorter : System.Collections.IComparer
        {
            readonly int mode;
            public ShortcutSorter(int m) { mode = m; }
            public int Compare(object x, object y)
            {
                ListViewItem a = (ListViewItem)x, b = (ListViewItem)y;
                switch (mode)
                {
                    case 1: return Native.CompareNatural(a.Text, b.Text);
                    case 2: return Native.CompareNatural(b.Text, a.Text);
                    case 3: return Native.CompareNatural((string)a.Tag, (string)b.Tag);
                    default: return string.CompareOrdinal(a.Name, b.Name); // arranged position
                }
            }
        }

        // Items in their arranged order (what the shared file stores), whatever the current sort.
        List<ListViewItem> Arranged()
        {
            List<ListViewItem> r = new List<ListViewItem>();
            foreach (ListViewItem it in list.Items) r.Add(it);
            r.Sort(delegate(ListViewItem a, ListViewItem b) { return string.CompareOrdinal(a.Name, b.Name); });
            return r;
        }

        static void DrawSortIcon(Graphics g, Rectangle r, Color c, int mode)
        {
            using (Pen p = new Pen(c))
            using (SolidBrush b = new SolidBrush(c))
            using (Font f = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            {
                int ax = r.Right - Native.Px(4); // arrow column on the right
                if (mode == 0)
                {
                    // as arranged: rows with a grip
                    for (int k = 0; k < 3; k++) { int y = r.Y + 2 + k * (r.Height - 4) / 2; g.DrawLine(p, r.X, y, r.X + r.Width * 6 / 10, y); }
                    g.DrawLine(p, ax, r.Y, ax, r.Bottom - 1);
                    g.DrawLine(p, ax - 2, r.Y + 2, ax, r.Y); g.DrawLine(p, ax + 2, r.Y + 2, ax, r.Y);
                    g.DrawLine(p, ax - 2, r.Bottom - 3, ax, r.Bottom - 1); g.DrawLine(p, ax + 2, r.Bottom - 3, ax, r.Bottom - 1);
                    return;
                }
                if (mode == 3)
                {
                    // folder path: a small folder
                    int fw = r.Width * 6 / 10, fh = r.Height * 6 / 10, fy = r.Y + (r.Height - fh) / 2;
                    g.DrawRectangle(p, r.X, fy, fw, fh);
                    g.DrawLine(p, r.X, fy, r.X + fw / 3, fy - 2); g.DrawLine(p, r.X + fw / 3, fy - 2, r.X + fw / 2, fy);
                }
                else
                {
                    string top = mode == 1 ? "A" : "Z", bottom = mode == 1 ? "Z" : "A";
                    TextRenderer.DrawText(g, top, f, new Rectangle(r.X - 1, r.Y - 3, r.Width / 2 + 2, r.Height / 2 + 4), c, TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter);
                    TextRenderer.DrawText(g, bottom, f, new Rectangle(r.X - 1, r.Y + r.Height / 2 - 2, r.Width / 2 + 2, r.Height / 2 + 4), c, TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter);
                }
                // downward arrow
                g.DrawLine(p, ax, r.Y, ax, r.Bottom - 1);
                g.DrawLine(p, ax - 2, r.Bottom - 3, ax, r.Bottom - 1);
                g.DrawLine(p, ax + 2, r.Bottom - 3, ax, r.Bottom - 1);
            }
        }

        // Saved as "0" (fit), "<n>c" (characters) or "<n>p" (pixels).
        public string WidthSetting
        {
            get { return widthValue == 0 ? "0" : widthValue + (widthInChars ? "c" : "p"); }
            set
            {
                int n;
                string v = (value ?? "").Trim();
                widthInChars = v.EndsWith("c");
                widthValue = int.TryParse(v.TrimEnd('c', 'p'), out n) && n > 0 ? n : 0;
                ApplyWidth();
            }
        }

        // In list view every column has the same width; names longer than it end in "…".
        void ApplyWidth()
        {
            if (!list.IsHandleCreated) return;
            int px;
            if (widthValue == 0) px = -1; // LVSCW_AUTOSIZE: fit the longest name
            else if (!widthInChars) px = widthValue;
            else
            {
                const string sample = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
                double avg = TextRenderer.MeasureText(sample, list.Font).Width / (double)sample.Length;
                px = (int)Math.Round(widthValue * avg) + Native.Px(16) + Native.Px(12);
            }
            const int LVM_SETCOLUMNWIDTH = 0x101E;
            Native.SendMessage(list.Handle, LVM_SETCOLUMNWIDTH, IntPtr.Zero, (IntPtr)px);
        }

        void AskWidth()
        {
            using (Form f = new Form())
            {
                f.Text = "Shortcut column width";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = f.ShowInTaskbar = false;
                f.Font = main.Font;
                f.BackColor = Theme.Menu;
                f.ForeColor = Theme.Text;
                f.ClientSize = new Size(Native.Px(320), Native.Px(110));
                Label l = new Label();
                l.Text = "Maximum width of each column:";
                l.AutoSize = true;
                l.Location = new Point(Native.Px(12), Native.Px(14));
                NumericUpDown num = new NumericUpDown();
                num.Minimum = 1; num.Maximum = 2000;
                num.Value = widthValue > 0 ? widthValue : 30;
                num.Location = new Point(Native.Px(12), Native.Px(40));
                num.Width = Native.Px(90);
                ComboBox unit = new ComboBox();
                unit.DropDownStyle = ComboBoxStyle.DropDownList;
                unit.Items.AddRange(new object[] { "characters", "pixels" });
                unit.SelectedIndex = widthValue > 0 && !widthInChars ? 1 : 0;
                unit.Location = new Point(Native.Px(110), Native.Px(40));
                unit.Width = Native.Px(110);
                Button ok = new Button();
                ok.Text = "OK"; ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(Native.Px(150), Native.Px(76)); ok.Width = Native.Px(75);
                Button cancel = new Button();
                cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(Native.Px(233), Native.Px(76)); cancel.Width = Native.Px(75);
                foreach (Control x in new Control[] { num, unit })
                {
                    x.BackColor = Theme.Input;
                    x.ForeColor = Theme.Text;
                }
                f.Controls.AddRange(new Control[] { l, num, unit, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.HandleCreated += delegate
                {
                    int dark = Theme.Dark ? 1 : 0;
                    Native.DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);
                };
                if (f.ShowDialog(main) != DialogResult.OK) return;
                widthValue = (int)num.Value;
                widthInChars = unit.SelectedIndex == 0;
                ApplyWidth();
                main.StateChanged();
            }
        }

        public IEnumerable<KeyValuePair<string, string>> Entries
        {
            get
            {
                foreach (ListViewItem it in list.Items)
                    yield return new KeyValuePair<string, string>((string)it.Tag, it.Text);
            }
        }

        public void ApplyTheme()
        {
            header.BackColor = headerBar.BackColor = Theme.Bar;
            header.ForeColor = Theme.TextDim;
            foreach (GlyphButton b in sortButtons) b.Invalidate();
            list.BackColor = Theme.Window;
            list.ForeColor = Theme.Text;
            notice.BackColor = Theme.Bar;
            notice.ForeColor = Theme.Dark ? Color.FromArgb(255, 170, 90) : Color.FromArgb(176, 80, 0);
            if (list.IsHandleCreated) Native.SetWindowTheme(list.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
            try { ShowAvailability(); } catch { }
            list.Invalidate();
        }

        // ---- shared list file (OneDrive)

        // Called once at startup: load the shared list, or create it from the shortcuts this computer had before.
        public void LoadOrMigrate(List<KeyValuePair<string, string>> legacy)
        {
            if (File.Exists(ListFile)) LoadList();
            else
            {
                loadedOk = true;
                foreach (KeyValuePair<string, string> s in legacy) Add(s.Value, s.Key, false);
                if (legacy.Count > 0 && !SaveList()) pendingLegacy = legacy;
                baseEntries = CurrentEntries();
                CheckAvailability();
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ListFile));
                watcher = new FileSystemWatcher(Path.GetDirectoryName(ListFile), Path.GetFileName(ListFile));
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                watcher.SynchronizingObject = this;
                FileSystemEventHandler h = delegate { OnListFileChanged(); };
                watcher.Changed += h;
                watcher.Created += h;
                watcher.Renamed += delegate { OnListFileChanged(); };
                // If the watcher's buffer overflows it stops reporting; reload and keep watching.
                watcher.Error += delegate { try { watcher.EnableRaisingEvents = true; } catch { } OnListFileChanged(); };
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        void OnListFileChanged()
        {
            try { if (File.Exists(ListFile) && File.GetLastWriteTimeUtc(ListFile) == knownStamp) return; } catch { } // our own save
            reloadTimer.Interval = 700;
            reloadTimer.Stop();
            reloadTimer.Start();
        }

        void LoadList()
        {
            // Never replace the list while a rename is being typed; try again shortly.
            if (editingItem != null) { reloadTimer.Stop(); reloadTimer.Start(); return; }
            string[] lines;
            DateTime stamp;
            try
            {
                stamp = File.GetLastWriteTimeUtc(ListFile);
                lines = File.ReadAllLines(ListFile, Encoding.UTF8);
            }
            catch
            {
                // OneDrive may still be downloading or syncing the file. Keep what we have, don't save, retry.
                if (!loadedOk)
                {
                    notice.Text = "\u26A0  The shared shortcuts list can't be read yet (OneDrive may still be syncing it). Changes won't be saved until it loads.";
                    notice.Visible = true;
                }
                reloadTimer.Interval = 5000;
                reloadTimer.Stop();
                reloadTimer.Start();
                return;
            }
            loadedOk = true;
            knownStamp = stamp;
            List<KeyValuePair<string, string>> remote = ShortcutList.Parse(lines);
            if (dirty)
            {
                // Changes made here haven't been saved yet (a save failed, or the file wasn't readable when
                // they were made): keep them by merging with the new contents, and save the result.
                List<KeyValuePair<string, string>> merged = ShortcutList.Merge(baseEntries, CurrentEntries(), remote);
                baseEntries = remote;
                SetEntries(merged);
                if (!SaveList()) saveRetry.Start();
                ShowAvailability();
                return;
            }
            baseEntries = remote;
            SetEntries(baseEntries);
        }

        // Replaces the displayed list (arranged order) without saving.
        void SetEntries(List<KeyValuePair<string, string>> entries)
        {
            list.BeginUpdate();
            list.Items.Clear();
            nextSeq = 0;
            foreach (KeyValuePair<string, string> s in entries) Add(s.Value, s.Key, false);
            list.EndUpdate();
            ApplyWidth();
            CheckAvailability();
        }

        List<KeyValuePair<string, string>> CurrentEntries()
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>();
            foreach (ListViewItem it in Arranged()) r.Add(new KeyValuePair<string, string>(it.Text, (string)it.Tag));
            return r;
        }

        bool SaveList()
        {
            if (!loadedOk) return false; // never overwrite a list we couldn't read
            try
            {
                List<KeyValuePair<string, string>> local = CurrentEntries();
                // Merges whatever another window or computer saved meanwhile, all under the file's lock.
                List<KeyValuePair<string, string>> entries = ShortcutList.SaveMerged(ListFile, baseEntries, local);
                if (!SameEntries(entries, local)) SetEntries(entries);
                knownStamp = File.GetLastWriteTimeUtc(ListFile);
                baseEntries = entries;
                dirty = false;
                pendingLegacy = null;
                saveError = null;
                return true;
            }
            catch (Exception ex)
            {
                saveError = ex.Message;
                Program.LogError(ex);
                return false;
            }
        }

        static bool SameEntries(List<KeyValuePair<string, string>> a, List<KeyValuePair<string, string>> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].Key != b[i].Key || a[i].Value != b[i].Value) return false;
            return true;
        }

        bool Contains(string path)
        {
            foreach (ListViewItem x in list.Items) if (Util.SameFolder((string)x.Tag, path)) return true;
            return false;
        }

        void BeginRename(ListViewItem it, bool folder)
        {
            string path = (string)it.Tag;
            if (folder && (unavailable.Contains(path) || !Directory.Exists(path) || Path.GetDirectoryName(path.TrimEnd('\\')) == null))
            {
                SystemSounds.Beep.Play();
                MessageBox.Show(main, Directory.Exists(path) ? "A drive can't be renamed from here." : "This folder isn't available on this computer, so it can't be renamed.",
                    "Rename folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            renamingFolder = folder;
            labelBeforeEdit = it.Text;
            editingItem = it;
            // When renaming the folder, edit its real name (the label may differ).
            if (folder) it.Text = Path.GetFileName(path.TrimEnd('\\'));
            editRequested = true;
            it.BeginEdit();
        }

        void AfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            ListViewItem it = editingItem;
            string label = e.Label == null ? null : e.Label.Trim();
            string before = labelBeforeEdit;
            e.CancelEdit = true; // we set the final text ourselves, after the edit box closes
            if (it == null) return;
            BeginInvoke((MethodInvoker)delegate
            {
                editingItem = null;
                if (it.ListView == null) return; // the list was reloaded meanwhile
                if (label == null || label.Length == 0 || label.Contains("|")) { it.Text = before; return; }
                if (!renamingFolder) { it.Text = label; ApplyWidth(); ListChanged(); return; }
                if (!RenameFolder(it, label)) it.Text = before;
            });
        }

        bool RenameFolder(ListViewItem it, string newName)
        {
            string oldPath = ((string)it.Tag).TrimEnd('\\');
            if (newName == Path.GetFileName(oldPath) || !Path.IsPathRooted(oldPath)) return false;
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.Trim('.').Length == 0)
            {
                MessageBox.Show(main, "A folder name can't contain any of these characters:  \\ / : * ? \" < > |", "Rename folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            string newPath = Path.Combine(Path.GetDirectoryName(oldPath), newName);
            bool caseOnly = newPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && (Directory.Exists(newPath) || File.Exists(newPath)))
            {
                MessageBox.Show(main, "There is already an item named \"" + newName + "\" in that folder.", "Rename folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!Native.ShellRename(main.Handle, oldPath, newPath) || !Directory.Exists(newPath)) return false;
            // Point this and any other shortcuts (and open tabs) inside the old folder at the new one.
            foreach (ListViewItem x in list.Items)
            {
                string moved = Util.Rebase((string)x.Tag, oldPath, newPath);
                if (moved == null) continue;
                x.Tag = moved;
                x.ToolTipText = moved;
            }
            it.Text = newName;
            ApplyWidth();
            ListChanged();
            main.FolderRenamed(oldPath, newPath);
            return true;
        }

        void ListChanged()
        {
            if (sortMode != 0) list.Sort();
            dirty = true;
            // A failed save is kept (the list on screen is the truth) and retried until it succeeds.
            if (!SaveList()) saveRetry.Start();
            ShowAvailability();
            main.StateChanged();
        }

        // Checks in the background (a missing network share can take a while to time out).
        public void CheckAvailability()
        {
            // One check at a time; a request that arrives meanwhile runs once the current one finishes.
            if (!IsHandleCreated) return; // results could not be delivered yet; OnShown checks again
            if (checking) { checkAgain = true; return; }
            checking = true;
            List<string> paths = new List<string>();
            foreach (ListViewItem it in list.Items) paths.Add((string)it.Tag);
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                List<string> missing = new List<string>();
                foreach (string p in paths)
                {
                    bool ok;
                    try { ok = Directory.Exists(p); } catch { ok = false; }
                    if (!ok) missing.Add(p);
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        checking = false;
                        unavailable.Clear();
                        foreach (string p in missing) unavailable.Add(p);
                        ShowAvailability();
                        if (checkAgain) { checkAgain = false; CheckAvailability(); }
                    });
                }
                catch { checking = false; } // window not created yet: the check after startup will run again
            });
            th.IsBackground = true;
            th.Start();
        }

        void ShowAvailability()
        {
            List<string> names = new List<string>();
            foreach (ListViewItem it in list.Items)
            {
                string p = (string)it.Tag;
                bool missing = unavailable.Contains(p);
                it.ForeColor = missing ? Theme.TextDim : Theme.Text;
                it.ToolTipText = missing ? "Not available on this computer: " + p : p;
                if (missing) names.Add(it.Text);
            }
            if (saveError != null || (!loadedOk && notice.Visible))
            {
                if (saveError != null)
                {
                    notice.Text = "\u26A0  Shortcuts couldn't be saved (" + saveError + "); retrying. They're kept here meanwhile.";
                    notice.Visible = true;
                }
                if (main.Shortcuts != null) main.UpdateStatus();
                return;
            }
            notice.Visible = names.Count > 0;
            if (names.Count > 0)
                notice.Text = "\u26A0  " + names.Count + (names.Count == 1 ? " shortcut points to a folder that isn't" : " shortcuts point to folders that aren't") +
                    " available on this computer: " + string.Join(", ", names.ToArray());
            if (main.Shortcuts != null) main.UpdateStatus();
        }

        public void Add(string path, string label, bool save = true)
        {
            if (string.IsNullOrEmpty(path)) return;
            foreach (ListViewItem x in list.Items)
                if (Util.SameFolder((string)x.Tag, path)) { x.Selected = true; x.EnsureVisible(); return; }
            if (string.IsNullOrEmpty(label)) label = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(label)) label = path;
            label = label.Replace('|', '-');
            if (!icons.Images.ContainsKey(path) && iconsPending.Add(path)) QueueIcon(path);
            ListViewItem it = new ListViewItem(label, path);
            it.Name = (nextSeq++).ToString("D9");
            it.Tag = path;
            it.ToolTipText = path;
            list.Items.Add(it);
            ApplyWidth();
            if (save) ListChanged();
        }

        void Remove(ListViewItem it)
        {
            list.Items.Remove(it);
            // Drop its cached icon (the image list keeps one per folder).
            if (icons.Images.ContainsKey((string)it.Tag)) icons.Images.RemoveByKey((string)it.Tag);
            unavailable.Remove((string)it.Tag);
            ApplyWidth();
            ShowAvailability();
            ListChanged();
        }

        void MoveItem(ListViewItem it, int to)
        {
            to = Math.Max(0, Math.Min(list.Items.Count - 1, to));
            if (it.Index == to || sortMode != 0) return;
            // Renumber the arranged positions and let the list re-sort (inserting would be re-sorted anyway).
            List<ListViewItem> order = Arranged();
            order.Remove(it);
            order.Insert(to, it);
            for (int k = 0; k < order.Count; k++) order[k].Name = k.ToString("D9");
            nextSeq = order.Count;
            list.Sort();
            ListChanged();
        }


        static List<string> DroppedFolders(IDataObject data)
        {
            List<string> result = new List<string>();
            string[] files = data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null;
            if (files != null) foreach (string f in files) if (Directory.Exists(f)) result.Add(f);
            return result;
        }

        void ListDragOver(object sender, DragEventArgs e)
        {
            if (DroppedFolders(e.Data).Count == 0) e.Effect = DragDropEffects.None;
            else if ((e.AllowedEffect & DragDropEffects.Link) != 0) e.Effect = DragDropEffects.Link;
            else if ((e.AllowedEffect & DragDropEffects.Copy) != 0) e.Effect = DragDropEffects.Copy;
            else e.Effect = DragDropEffects.None;
        }

        void ListDragDrop(object sender, DragEventArgs e)
        {
            // Adds shortcuts only. Nothing on disk is moved, copied or renamed from this pane.
            foreach (string f in DroppedFolders(e.Data)) Add(f, null);
        }

        void ShowMenu(ListViewItem it, Point screen)
        {
            ContextMenuStrip m = main.NewMenu();
            if (it != null)
            {
                string path = (string)it.Tag;
                m.Items.Add("Open", null, delegate { main.OpenFolder(path, 0); });
                m.Items.Add("Open in new tab", null, delegate { main.OpenFolder(path, 1); });
                m.Items.Add("Open in other pane", null, delegate { main.OpenFolder(path, 2); });
                m.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem renameFolder = new ToolStripMenuItem("Rename folder", null, delegate { BeginRename(it, true); });
                renameFolder.ShortcutKeyDisplayString = "F2";
                m.Items.Add(renameFolder);
                m.Items.Add("Rename shortcut label only", null, delegate { BeginRename(it, false); });
                ToolStripItem left = m.Items.Add("Move earlier", null, delegate { MoveItem(it, it.Index - 1); });
                left.Enabled = it.Index > 0 && sortMode == 0;
                ToolStripItem right = m.Items.Add("Move later", null, delegate { MoveItem(it, it.Index + 1); });
                right.Enabled = it.Index < list.Items.Count - 1 && sortMode == 0;
                m.Items.Add("Remove", null, delegate { Remove(it); });
                m.Items.Add(new ToolStripSeparator());
            }
            ToolStripMenuItem width = new ToolStripMenuItem("Column width");
            ToolStripMenuItem fit = new ToolStripMenuItem("Fit longest name", null, delegate { widthValue = 0; ApplyWidth(); main.StateChanged(); });
            fit.Checked = widthValue == 0;
            ToolStripMenuItem set = new ToolStripMenuItem(widthValue == 0 ? "Set width…" : "Set width… (now " + widthValue + (widthInChars ? " characters" : " px") + ")", null, delegate { AskWidth(); });
            set.Checked = widthValue != 0;
            width.DropDownItems.Add(fit);
            width.DropDownItems.Add(set);
            width.DropDown.Renderer = m.Renderer;
            m.Items.Add(width);
            m.Items.Add(new ToolStripSeparator());
            BrowserTab t = main.ActivePane.ActiveTab;
            ToolStripItem add = m.Items.Add("Add current folder", null, delegate { if (t != null) Add(t.Address, t.Title); });
            add.Enabled = t != null && Directory.Exists(t.Address);
            m.Show(screen);
        }
    }
}
