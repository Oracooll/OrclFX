// Orcl File Explorer: Main window: layout, commands, input routing and saved settings.
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
    partial class MainForm : Form, IMessageFilter
    {
        public static readonly string StateFile = AppPaths.StateFile;

        public readonly Pane[] Panes = new Pane[4];
        // How many panes are shown side by side (1-4). With 1, only the active pane is shown.
        public int PaneCount = 2;
        int multiCount = 2; // the last count above 1, used by Tab in single-pane mode
        public Pane ActivePane;
        public bool Ready, ShowTree = true, ShowPreview, ShowShortcuts = true, AutoFit = true, FolderSizes;
        SizeJob sizeJob;
        string sizeSkip;
        readonly Dictionary<string, SizeJob> sizeCache = new Dictionary<string, SizeJob>(StringComparer.OrdinalIgnoreCase);
        public readonly ShortcutsPane Shortcuts;
        readonly TreePane tree;
        readonly PreviewPane preview = new PreviewPane();
        // Layout: vsplit = [ treeSplit = [ tree | previewSplit = [ row = [pane | pane | ...] | preview ] ] / shortcuts ]
        readonly LiveSplit vsplit = new LiveSplit(), treeSplit = new LiveSplit(), previewSplit = new LiveSplit();
        readonly PaneRow row;
        readonly GlyphButton treeBtn, previewBtn;
        readonly TitleBar titleBar;
        int shortcutsHeight = 130, treeWidth = 260, previewWidth = 420;
        readonly Panel status = new Panel();
        readonly Label statusLeft = new Label(), statusRight = new Label();
        readonly Timer saveTimer = new Timer(), statusTimer = new Timer();
        Native.HookProc hookProc;
        IntPtr hook;
        float legacySplit = -1f;
        int startPane;
        string freeRoot;
        int freeTick, freeGen;
        readonly HashSet<string> freeBusy = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // drives with a query running
        string noticeText, stateSaveError;
        int noticeTick;

        // A short message in the status bar for a few seconds.
        public void Notice(string text)
        {
            Program.Trace("notice: " + text);
            noticeText = text;
            noticeTick = Environment.TickCount;
            UpdateStatus();
        }

        public MainForm()
        {
            Text = Program.AppName + " " + Installer.Version;
            // Room for the tree, one pane and the preview; a saved narrower size is widened to this.
            MinimumSize = new Size(Native.Px(800), Native.Px(400)); // wide enough for the title bar's buttons and icon
            Font = new Font("Segoe UI", 9f);
            Icon = AppIcon();
            titleBar = new TitleBar(this);
            titleBar.SetIcon(Icon);
            titleBar.FindButton.Click += delegate { ShowFind(); };
            titleBar.IconClick += delegate
            {
                Rectangle r = titleBar.IconRect;
                ActivePane.ShowMainMenu(titleBar, new Point(r.Left, r.Bottom + Native.Px(2)), ToolStripDropDownDirection.BelowRight);
            };
            for (int i = 0; i < 3; i++)
            {
                int mode = i;
                titleBar.ThemeButtons[i].Click += delegate { SetThemeMode(mode); };
            }
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Native.Px(1400), Native.Px(860));

            for (int i = 0; i < Panes.Length; i++) Panes[i] = new Pane(this);
            row = new PaneRow(Panes);
            row.WeightsChanged += delegate { StateChanged(); };

            vsplit.Dock = DockStyle.Fill;
            vsplit.Orientation = Orientation.Horizontal;
            vsplit.FixedPanel = FixedPanel.Panel2;
            vsplit.SplitterWidth = Native.Px(5);
            vsplit.TabStop = false;
            tree = new TreePane(this);
            previewSplit.Dock = DockStyle.Fill;
            previewSplit.FixedPanel = FixedPanel.Panel2;
            previewSplit.SplitterWidth = Native.Px(5);
            previewSplit.Panel1.Controls.Add(row);
            previewSplit.Panel2.Controls.Add(preview);
            treeSplit.Dock = DockStyle.Fill;
            treeSplit.FixedPanel = FixedPanel.Panel1;
            treeSplit.SplitterWidth = Native.Px(5);
            treeSplit.Panel1.Controls.Add(tree);
            treeSplit.Panel2.Controls.Add(previewSplit);
            vsplit.Panel1.Controls.Add(treeSplit);
            Shortcuts = new ShortcutsPane(this);
            vsplit.Panel2.Controls.Add(Shortcuts);

            status.Dock = DockStyle.Bottom;
            status.Height = Native.Px(26);
            status.Padding = new Padding(Native.Px(10), 0, Native.Px(10), 0);
            treeBtn = titleBar.LayoutButtons[0];
            previewBtn = titleBar.LayoutButtons[5];
            treeBtn.Click += delegate { SetShowTree(!ShowTree); };
            for (int i = 1; i <= 4; i++)
            {
                int count = i;
                titleBar.LayoutButtons[i].Click += delegate { SetPaneCount(count); };
            }
            previewBtn.Click += delegate { SetShowPreview(!ShowPreview); };
            for (int i = 0; i < 6; i++)
            {
                int v = i;
                titleBar.ViewButtons[i].Click += delegate { SetViewMode(ButtonViewModes[v, 0], ButtonViewModes[v, 1]); };
                titleBar.ViewButtons[i].MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) SetDefaultView(v); };
            }
            titleBar.LayoutButtons[6].Click += delegate { SetShowShortcuts(!ShowShortcuts); };
            statusLeft.Dock = DockStyle.Fill;
            statusLeft.TextAlign = ContentAlignment.MiddleLeft;
            statusRight.Dock = DockStyle.Right;
            statusRight.Width = Native.Px(300);
            statusRight.TextAlign = ContentAlignment.MiddleRight;
            status.Controls.Add(statusLeft);
            status.Controls.Add(statusRight);


            Controls.Add(vsplit);
            Controls.Add(status);
            Controls.Add(titleBar);
            ActivePane = Panes[0];

            LoadState();
            Theme.Update();
            ApplyTheme();

            saveTimer.Interval = 1500;
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveState(); };
            statusTimer.Interval = 300;
            statusTimer.Tick += delegate { UpdateStatus(); UpdatePreview(); AutoFitViews(); UpdateViewButtons(); };
        }

        // The app icon is embedded with all its sizes; fall back to the exe's icon.
        public static Icon AppIcon()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (s != null) return new Icon(s);
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { return null; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTitleBar();
            // Re-run WM_NCCALCSIZE so our own title bar replaces the system caption.
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            vsplit.Panel2MinSize = Native.Px(50);
            vsplit.Panel2Collapsed = !ShowShortcuts;
            int sh = Native.Px(shortcutsHeight);
            try { if (vsplit.Height > sh + Native.Px(200)) vsplit.SplitterDistance = vsplit.Height - sh - vsplit.SplitterWidth; }
            catch (Exception ex) { Program.LogError(ex); }
            vsplit.SplitterMoved += delegate { StateChanged(); };
            // A splitter rejects positions (and minimum sizes) that don't fit its width; never let that stop the
            // window from starting (the panes would stay empty).
            try
            {
                treeSplit.Panel1MinSize = Native.Px(120);
                previewSplit.Panel2MinSize = Native.Px(150);
                treeSplit.SplitterDistance = Math.Max(Native.Px(120), Math.Min(treeSplit.Width / 3, Native.Px(treeWidth)));
                int pw = Math.Max(Native.Px(150), Math.Min(previewSplit.Width / 2, Native.Px(previewWidth)));
                previewSplit.SplitterDistance = Math.Max(previewSplit.Panel1MinSize, previewSplit.Width - pw - previewSplit.SplitterWidth);
            }
            catch (Exception ex) { Program.LogError(ex); }
            treeSplit.SplitterMoved += delegate { StateChanged(); };
            previewSplit.SplitterMoved += delegate { StateChanged(); };
            treeSplit.Panel1Collapsed = !ShowTree;
            previewSplit.Panel2Collapsed = !ShowPreview;
            UpdateLayoutButtons();
            if (ShowTree) tree.EnsureCreated();
            Ready = true;
            ListenForShow();
            Activated += delegate { Shortcuts.CheckAvailability(); };
            // Test hook: DUALPANE_TEST_THEME=<0|1|2> switches theme 3 seconds after start (used to test live switching).
            int testTheme;
            if (int.TryParse(Environment.GetEnvironmentVariable("DUALPANE_TEST_THEME"), out testTheme))
            {
                Timer tt = new Timer();
                tt.Interval = 3000;
                tt.Tick += delegate { tt.Stop(); Program.Trace("test theme -> " + testTheme); SetThemeMode(testTheme); };
                tt.Start();
            }
            // Test hook: DUALPANE_TEST_FIND=<text> (or +<text> for names and contents) runs Find 3 seconds after
            // start, logs the results 10 seconds later, then presses Up and logs where the tab went.
            string testFind = Environment.GetEnvironmentVariable("DUALPANE_TEST_FIND");
            if (testFind != null)
            {
                Timer ft = new Timer();
                ft.Interval = 3000;
                int step = 0;
                ft.Tick += delegate
                {
                    if (step++ == 0) { ft.Interval = 10000; RunFind(testFind.TrimStart('+'), testFind.StartsWith("+")); return; }
                    BrowserTab a = ActivePane.ActiveTab;
                    if (step == 2)
                    {
                        ft.Interval = 1000;
                        UpdateStatus();
                        Program.Trace("find: tab â€œ" + a.Title + "â€, view items " + a.Count(Native.SVGIO_ALLVIEW) + ", status: " + statusLeft.Text + " | " + string.Join(" ; ", a.ItemPaths(10).ToArray()));
                        return;
                    }
                    string open = Environment.GetEnvironmentVariable("DUALPANE_TEST_FIND_OPEN");
                    if (step == 3 && open != null) { ft.Interval = 3000; Program.Trace("find: open " + open + ": " + a.TestOpenItem(open)); return; }
                    if (step == 3) { ft.Interval = 3000; a.GoUp(); return; }
                    ft.Stop();
                    Program.Trace("find: after " + (Environment.GetEnvironmentVariable("DUALPANE_TEST_FIND_OPEN") != null ? "opening" : "Up") + ": tab â€œ" + a.Title + "â€, folder " + a.Folder + ", results " + a.IsFindResults + ", view items " + a.Count(Native.SVGIO_ALLVIEW));
                };
                ft.Start();
            }
            // Test hook: DUALPANE_TEST_ADDSHORTCUT=<folder>[|<folder>...] adds those shortcuts 3 seconds after start,
            // removes the first one 3 seconds later, and logs which icon each shortcut shows after each step.
            string testAdd = Environment.GetEnvironmentVariable("DUALPANE_TEST_ADDSHORTCUT");
            if (testAdd != null)
            {
                Timer at = new Timer();
                at.Interval = 3000;
                int astep = 0;
                at.Tick += delegate
                {
                    astep++;
                    if (astep == 1) { Program.Trace("shortcuts before: " + Shortcuts.IconReport()); foreach (string f in testAdd.Split('|')) Shortcuts.Add(f, null); return; }
                    Program.Trace("shortcuts after " + (astep == 2 ? "adding" : "removing the first") + ": " + Shortcuts.IconReport());
                    if (astep == 2) Shortcuts.TestRemoveFirst();
                    else at.Stop();
                };
                at.Start();
            }
            // Test hook: DUALPANE_TEST_NAVIGATE=<folder> opens that folder from the active tab 3 seconds after start
            // and logs the active pane's tabs (L = locked) 3 seconds later.
            string testNav = Environment.GetEnvironmentVariable("DUALPANE_TEST_NAVIGATE");
            if (testNav != null)
            {
                Timer nt = new Timer();
                nt.Interval = 3000;
                bool navigated = false;
                nt.Tick += delegate
                {
                    if (!navigated) { navigated = true; ActivePane.ActiveTab.Navigate(testNav); return; }
                    nt.Stop();
                    List<string> row = new List<string>();
                    foreach (BrowserTab x in ActivePane.Tabs) row.Add((x.Locked ? "L:" : "U:") + x.Title + (x == ActivePane.ActiveTab ? "*" : ""));
                    int vm, vs;
                    ActivePane.ActiveTab.GetViewMode(out vm, out vs);
                    Program.Trace("tabs: " + string.Join(" | ", row.ToArray()) + "  view " + vm + "/" + vs);
                };
                nt.Start();
            }
            // Test hook: DUALPANE_TEST_HOLDREF=<seconds> takes a shell process reference (as a running copy does)
            // for that long, to test that closing waits for it.
            int holdSeconds;
            if (int.TryParse(Environment.GetEnvironmentVariable("DUALPANE_TEST_HOLDREF"), out holdSeconds))
            {
                IntPtr held = ProcessReference.TestTake();
                Program.Trace("holding a process reference: " + (held != IntPtr.Zero) + ", busy " + ProcessReference.Busy);
                System.Threading.Thread rt = new System.Threading.Thread(delegate()
                {
                    System.Threading.Thread.Sleep(holdSeconds * 1000);
                    Program.Trace("releasing the process reference");
                    if (held != IntPtr.Zero) Marshal.Release(held);
                });
                rt.IsBackground = true;
                rt.Start();
            }
            // Test hook: DUALPANE_TEST_LAYOUT=<name> switches to that saved layout 3 seconds after start (or, with
            // "save:<name>", saves the panes and tabs as it), then logs every shown pane's tabs; "back" undoes a switch.
            string testLayout = Environment.GetEnvironmentVariable("DUALPANE_TEST_LAYOUT");
            if (testLayout != null)
            {
                Timer lt = new Timer();
                lt.Interval = 3000;
                lt.Tick += delegate
                {
                    lt.Stop();
                    foreach (string step in testLayout.Split(';'))
                    {
                        if (step.StartsWith("save:")) LayoutFile.Save(AppPaths.LayoutsFile, CaptureLayout(step.Substring(5)));
                        else if (step == "back") SwitchBack();
                        else
                        {
                            Layout l = LayoutFile.Find(LayoutFile.Read(AppPaths.LayoutsFile), step);
                            if (l != null) ApplyLayout(l, l.Name); else Program.Trace("layout: no layout " + step);
                        }
                        List<string> panes = new List<string>();
                        foreach (Pane vp in VisiblePanes())
                        {
                            List<string> row = new List<string>();
                            foreach (BrowserTab x in vp.Tabs) row.Add((x.Locked ? "L:" : "U:") + x.SavedFolder + (x.Color != 0 ? " c" + x.Color : "") + (x == vp.ActiveTab ? "*" : ""));
                            panes.Add((vp == ActivePane ? "[active] " : "") + string.Join(" | ", row.ToArray()));
                        }
                        Program.Trace("layout after " + step + ": " + string.Join("  ||  ", panes.ToArray()));
                    }
                };
                lt.Start();
            }
            // Test hook: DUALPANE_TEST_NEW=folder|file creates a new folder or text file in the active tab 3 seconds
            // after start (the trace says whether renaming started).
            string testNew = Environment.GetEnvironmentVariable("DUALPANE_TEST_NEW");
            if (testNew != null)
            {
                Timer nwt = new Timer();
                nwt.Interval = 3000;
                nwt.Tick += delegate { nwt.Stop(); CreateNew(ActivePane, testNew == "folder"); };
                nwt.Start();
            }
            // Test hook: DUALPANE_TEST_PREVIEW=<file>[|<search>] shows that file in the preview pane 3 seconds after
            // start, typing the search text into its search box.
            string testPreview = Environment.GetEnvironmentVariable("DUALPANE_TEST_PREVIEW");
            if (testPreview != null)
            {
                Timer pvt = new Timer();
                pvt.Interval = 3000;
                pvt.Tick += delegate
                {
                    pvt.Stop();
                    string[] a = testPreview.Split('|');
                    Program.Trace("preview: selected " + ActivePane.ActiveTab.SelectPath(a[0], 0x1 | 0x4 | 0x8 | 0x10));
                    if (a.Length > 1) preview.TestSearch(a[1]);
                };
                pvt.Start();
            }
            // Test hook: DUALPANE_TEST_SELECT=<path>[|<path>...] selects those items of the active tab 3 seconds after
            // start and logs the status bar 4 seconds later; "ql:" before the first path opens Quick Look on it
            // instead, steps to the next item, and logs what Quick Look shows.
            string testSelect = Environment.GetEnvironmentVariable("DUALPANE_TEST_SELECT");
            if (testSelect != null)
            {
                Timer st = new Timer();
                st.Interval = 3000;
                int stage = 0;
                bool ql = testSelect.StartsWith("ql:");
                string[] items = (ql ? testSelect.Substring(3) : testSelect).Split('|');
                st.Tick += delegate
                {
                    BrowserTab a = ActivePane.ActiveTab;
                    if (stage == 0)
                    {
                        for (int i = 0; i < items.Length; i++) a.SelectPath(items[i], i == 0 ? 0x15u : 0x1u);
                        if (ql) Program.Trace("quick look opened " + ShowQuickLook(a));
                        st.Interval = ql ? 2500 : 4000;
                    }
                    else if (ql && stage == 1)
                    {
                        if (quickLook != null) { string next = a.StepSelection(1); if (next != null) quickLook.ShowFile(next); }
                        Program.Trace("quick look shows " + (quickLook == null ? "nothing" : quickLook.ShownPath));
                    }
                    else
                    {
                        st.Stop();
                        Program.Trace("status: " + statusLeft.Text);
                    }
                    stage++;
                };
                st.Start();
            }
            // Test hook: DUALPANE_TEST_FINDBOX=1 opens the Find box 3 seconds after start (to look at it).
            if (Environment.GetEnvironmentVariable("DUALPANE_TEST_FINDBOX") == "1")
            {
                Timer fb = new Timer();
                fb.Interval = 3000;
                fb.Tick += delegate { fb.Stop(); ShowFind(); };
                fb.Start();
            }
            Shortcuts.CheckAvailability();
            ActivePane = Panes[startPane];
            ApplyPaneLayout();
            foreach (Pane x in Panes) x.ApplyActiveLook();
            ActiveFolderChanged();
            hookProc = MouseHook;
            hook = Native.SetWindowsHookEx(Native.WH_MOUSE, hookProc, IntPtr.Zero, Native.GetCurrentThreadId());
            Application.AddMessageFilter(this);
            statusTimer.Start();
            ScheduleUpdateCheck();
            // Left open for days, it still checks once a day.
            Timer daily = new Timer();
            daily.Interval = 3600000;
            daily.Tick += delegate { ScheduleUpdateCheck(); };
            daily.Start();
            BrowserTab t = ActivePane.ActiveTab;
            if (t != null) BeginInvoke((MethodInvoker)t.Activate);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            saveTimer.Stop();
            // Save the settings and any shortcut changes still waiting for a retry. If that fails, say so
            // instead of exiting and losing them (but never hold up Windows shutting down).
            bool userClose = e.CloseReason != CloseReason.WindowsShutDown && e.CloseReason != CloseReason.TaskManagerClosing;
            // A copy, move or delete started here is still running: its progress window belongs to this window,
            // so hide instead of closing, and close once it has finished.
            if (userClose && ProcessReference.Busy > 0 && !closeWhenIdle)
            {
                SaveAll();
                e.Cancel = true;
                closeWhenIdle = true;
                Hide();
                WhenIdle();
                return;
            }
            while (true)
            {
                string problem = SaveAll();
                if (problem == null || !userClose) break;
                if (!Visible) Show(); // closing after waiting hidden: the question needs a visible window
                DialogResult r = MessageBox.Show(this, problem + "\n\nYes: try again\nNo: close anyway (changes since the last save are lost)\nCancel: keep the window open",
                    Program.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) continue;
                if (r == DialogResult.No) break;
                e.Cancel = true;
                restarting = false;
                closeWhenIdle = false; // the user keeps the window: don't close it later
                saveTimer.Interval = 10000;
                saveTimer.Start();
                return;
            }
            base.OnFormClosing(e);
            if (e.Cancel) return;
            statusTimer.Stop();
            Application.RemoveMessageFilter(this);
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            foreach (Pane p in Panes) foreach (BrowserTab t in p.Tabs) t.Destroy();
            preview.Shutdown();
            tree.Destroy();
            if (sizeJob != null) sizeJob.Cancel = true;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // Keep the system's side and bottom frame (resize borders, shadow) but drop the caption:
                // the client area starts at the top of the window, where our TitleBar draws.
                RECT before = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                base.WndProc(ref m);
                RECT after = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                after.top = before.top + (WindowState == FormWindowState.Maximized ? Native.GetSystemMetrics(33) + Native.GetSystemMetrics(92) : 0);
                Marshal.StructureToPtr(after, m.LParam, false);
                return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == 1)
                {
                    int lp = unchecked((int)(long)m.LParam);
                    Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    int edge = Native.Px(6);
                    if (WindowState != FormWindowState.Maximized && p.Y < edge)
                        m.Result = (IntPtr)(p.X < edge * 2 ? 13 : p.X > ClientSize.Width - edge * 2 ? 14 : 12); // HTTOPLEFT / HTTOPRIGHT / HTTOP
                    else if (p.Y < titleBar.Bottom)
                        m.Result = (IntPtr)2; // HTCAPTION
                }
                return;
            }
            base.WndProc(ref m);
            const int WM_SETTINGCHANGE = 0x1A;
            if (m.Msg == WM_SETTINGCHANGE && m.LParam != IntPtr.Zero && Theme.Mode == 0 &&
                Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet")
            {
                bool wasDark = Theme.Dark;
                Theme.Update();
                ApplyTheme();
                if (wasDark != Theme.Dark) RestartForTheme();
            }
        }

        // The panes currently on screen, left to right.
        public List<Pane> VisiblePanes()
        {
            List<Pane> v = new List<Pane>();
            if (PaneCount <= 1) v.Add(ActivePane);
            else for (int i = 0; i < PaneCount; i++) v.Add(Panes[i]);
            return v;
        }

        // The pane after p: the next visible one (wrapping). In single-pane mode, the next of the
        // panes used in multi-pane mode, so Tab and "open in next pane" still have somewhere to go.
        public Pane Other(Pane p)
        {
            int n = PaneCount > 1 ? PaneCount : multiCount;
            int i = Array.IndexOf(Panes, p);
            return Panes[(Math.Max(0, i) + 1) % n];
        }

        void ApplyPaneLayout()
        {
            // The active pane must be one of the visible ones.
            if (PaneCount > 1 && Array.IndexOf(Panes, ActivePane) >= PaneCount) ActivePane = Panes[PaneCount - 1];
            List<Pane> v = VisiblePanes();
            row.ShowPanes(v);
            if (Ready) foreach (Pane p in v) p.ShowActive();
            foreach (Pane p in Panes) p.ApplyActiveLook();
        }

        public void SetActivePane(Pane p)
        {
            if (ActivePane == p) return;
            ActivePane = p;
            if (PaneCount <= 1 && Ready)
            {
                ApplyPaneLayout();
                p.ShowActive();
                BrowserTab shown = p.ActiveTab;
                if (shown != null) BeginInvoke((MethodInvoker)shown.Activate); // keep focus out of the hidden pane
            }
            foreach (Pane x in Panes) x.ApplyActiveLook();
            UpdateStatus();
            ActiveFolderChanged();
        }

        // Keeps the Tree pane on the active pane's current folder.
        public void ActiveFolderChanged()
        {
            if (!Ready || !ShowTree) return;
            BrowserTab t = ActivePane.ActiveTab;
            if (t != null && !t.WaitingForNetwork) tree.SyncTo(t.Folder);
        }

        void UpdatePreview()
        {
            BrowserTab t = ActivePane == null ? null : ActivePane.ActiveTab;
            if (t == null || !t.Created) { if (ShowPreview) preview.Show(null); return; }
            if (!ShowPreview && !FolderSizes) return;
            string sel = t.SelectedPath();
            if (FolderSizes)
            {
                // Sizes are shown for the selected folder, or for the current folder when nothing is selected.
                // The folder check is only repeated when the selection or folder changes.
                string candidate = sel ?? t.Address;
                if (candidate != lastSizeCandidate)
                {
                    lastSizeCandidate = candidate;
                    // Sizes aren't calculated on network locations; checking one on this thread could freeze the
                    // window on a slow or lost share, so it isn't even looked at.
                    bool network = Util.IsNetworkPath(candidate);
                    lastSizeTarget = candidate != null && !network && Directory.Exists(candidate) ? candidate : null;
                    if (network) sizeSkip = "not calculated on network locations";
                }
                string target = lastSizeTarget;
                SizeJob j = target != null ? EnsureSizeJob(target) : null;
                if (target == null && sizeJob != null)
                {
                    if (!sizeJob.Finished) { sizeJob.Cancel = true; sizeCache.Remove(sizeJob.Root); }
                    sizeJob = null;
                }
                if (j != null)
                {
                    if (ShowPreview && preview.ShownJob != j) preview.ShowSizes(j);
                    return;
                }
            }
            if (ShowPreview) preview.Show(sel);
        }

        string lastSizeCandidate, lastSizeTarget;

        SizeJob EnsureSizeJob(string target)
        {
            if (sizeJob != null && Util.SameFolder(sizeJob.Root, target) &&
                !(sizeJob.Finished && (DateTime.Now - sizeJob.FinishedAt).TotalMinutes >= 2 && ActiveForm == this)) return sizeJob; // no rescans while away
            if (sizeJob != null && !sizeJob.Finished)
            {
                sizeJob.Cancel = true;
                sizeCache.Remove(sizeJob.Root);
            }
            sizeJob = null;
            sizeSkip = SizeSkipReason(target);
            if (sizeSkip != null) return null;
            SizeJob cached;
            if (sizeCache.TryGetValue(target, out cached) && cached.Finished && cached.Failure == null && cached.Errors == 0 && (DateTime.Now - cached.FinishedAt).TotalMinutes < 2)
                return sizeJob = cached;
            if (sizeCache.Count > 200) sizeCache.Clear();
            sizeJob = SizeJob.Start(target, delegate(SizeJob j)
            {
                try { BeginInvoke((MethodInvoker)delegate { SizeJobUpdated(j); }); } catch { }
            });
            sizeCache[target] = sizeJob;
            return sizeJob;
        }

        static string SizeSkipReason(string path)
        {
            try
            {
                if (path.StartsWith(@"\\")) return "not calculated on network locations";
                DriveType dt = new DriveInfo(Path.GetPathRoot(path)).DriveType;
                if (dt == DriveType.Network) return "not calculated on network drives";
                if (dt == DriveType.CDRom) return "not calculated on optical drives";
            }
            catch { return "not available here"; }
            return null;
        }

        public void SizeJobUpdated(SizeJob j)
        {
            if (j != sizeJob) return;
            if (j.Failure != null) { AutoDisableFolderSizes(j.Failure); return; }
            if (ShowPreview && preview.ShownJob == j) preview.ShowSizes(j);
            UpdateStatus();
        }

        // "Critical" cases switch the feature off rather than keep the disk busy.
        void AutoDisableFolderSizes(string reason)
        {
            if (!FolderSizes) return;
            FolderSizes = false;
            if (sizeJob != null) sizeJob.Cancel = true;
            sizeJob = null;
            sizeCache.Clear();
            preview.Clear();
            StateChanged();
            UpdateStatus();
            MessageBox.Show(this, "Folder sizes were turned off because " + reason + ".\n\nYou can turn them on again from the menu (View options â€º Folder sizes).",
                "Folder sizes", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void ToggleFolderSizes()
        {
            if (FolderSizes)
            {
                FolderSizes = false;
                if (sizeJob != null) sizeJob.Cancel = true;
                sizeJob = null;
                preview.Clear();
                StateChanged();
                UpdateStatus();
                return;
            }
            if (MessageBox.Show(this,
                "Folder sizes are worked out by scanning every file inside each folder. On big folders this keeps the disk busy and can take a while.\n\n" +
                "Orcl File Explorer scans in the background at low priority, skips network drives, and turns this off by itself if a scan gets out of hand " +
                "(more than " + SizeJob.MaxSeconds + " seconds or " + (SizeJob.MaxEntries / 1000000) + " million items).\n\nTurn on folder sizes?",
                "Folder sizes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (MessageBox.Show(this,
                "Are you sure? Sizes will be calculated every time you open or select a folder: in the Preview pane as a breakdown of its subfolders, and as a total in the status bar.",
                "Folder sizes", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            FolderSizes = true;
            StateChanged();
            UpdatePreview();
        }

        public void ToggleHidden()
        {
            Native.SetShowHidden(!Native.GetShowHidden());
            foreach (Pane p in Panes) foreach (BrowserTab t in p.Tabs) if (t.Created) t.RefreshView();
            tree.Destroy();
            if (ShowTree) { tree.EnsureCreated(); ActiveFolderChanged(); }
        }

        public void ToggleExtensions()
        {
            bool on = !Native.GetShowExtensions();
            Native.SetShowExtensions(on);
            foreach (Pane p in Panes) foreach (BrowserTab t in p.Tabs) if (t.Created) t.RefreshView();
            Notice("File name extensions are now " + (on ? "shown" : "hidden") + " (in File Explorer too).");
        }

        public void ToggleNaturalSort()
        {
            bool on = !Native.GetNaturalSort();
            try { Native.SetNaturalSort(on); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't change the sorting setting: " + ex.Message, "Sorting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            foreach (Pane p in Panes) foreach (BrowserTab t in p.Tabs) if (t.Created) t.RefreshView();
            MessageBox.Show(this, "Natural number sorting is now " + (on ? "on" : "off") + ".\n\nThis is a Windows setting, so it applies to File Explorer as well. " +
                "If the order doesn't change straight away, restart Orcl File Explorer.", "Sorting", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void SetAutoFit(bool on)
        {
            AutoFit = on;
            foreach (Pane p in Panes) foreach (BrowserTab t in p.Tabs) t.fitCount = -1;
            StateChanged();
        }

        void AutoFitViews()
        {
            if (!AutoFit) return;
            foreach (Pane p in Panes)
            {
                BrowserTab t = p.ActiveTab;
                if (t == null || !t.Created) continue;
                int n = t.Count(Native.SVGIO_ALLVIEW);
                if (n == t.fitCount && t.fitFolder == t.Folder) continue;
                t.fitCount = n;
                t.fitFolder = t.Folder;
                t.AutoFitName();
            }
        }

        public void StateChanged()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        // ---- theme

        public void SetThemeMode(int mode)
        {
            bool wasDark = Theme.Dark;
            Theme.Mode = mode;
            Theme.Update();
            ApplyTheme();
            StateChanged();
            if (wasDark != Theme.Dark) RestartForTheme();
        }

        bool restarting;
        bool closeWhenIdle, restartWhenIdle;   // put off until the shell's copies and moves have finished
        Timer idleTimer;

        // A second start of the app sets this event: show the window, even while it waits hidden for a copy
        // (it then stays open).
        void ListenForShow()
        {
            System.Threading.EventWaitHandle ev;
            try { ev = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, Program.ShowEventName); }
            catch { return; }
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                // Also stops when this window closes, so a later start's request isn't taken by a closing app.
                while (System.Threading.WaitHandle.WaitAny(new System.Threading.WaitHandle[] { ev, formClosed }) == 0)
                {
                    try { BeginInvoke((MethodInvoker)ShowFromElsewhere); } catch { break; }
                }
                ev.Dispose();
            });
            th.IsBackground = true;
            th.Start();
        }

        readonly System.Threading.ManualResetEvent formClosed = new System.Threading.ManualResetEvent(false);

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            formClosed.Set();
            base.OnFormClosed(e);
        }

        void ShowFromElsewhere()
        {
            if (IsDisposed) return;
            closeWhenIdle = false;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void WhenIdle()
        {
            if (idleTimer != null) return;
            idleTimer = new Timer();
            idleTimer.Interval = 500;
            idleTimer.Tick += delegate
            {
                if (ProcessReference.Busy > 0) return;
                idleTimer.Stop();
                idleTimer.Dispose();
                idleTimer = null;
                if (closeWhenIdle) { Close(); return; }
                if (restartWhenIdle) { restartWhenIdle = false; RestartForTheme(); }
            };
            idleTimer.Start();
        }

        // Windows fixes some light/dark decisions once per running app, so switching live leaves parts
        // (Explorer's lists, menus, scrollbars) in the old mode. Restarting is the only reliable switch.
        void RestartForTheme()
        {
            if (!Ready || restarting) return;
            // Not in the middle of a copy or move: the restart waits until it has finished.
            if (ProcessReference.Busy > 0)
            {
                restartWhenIdle = true;
                Notice("The theme switches when the current copy or move has finished.");
                WhenIdle();
                return;
            }
            saveTimer.Stop();
            // The new window reads the saved settings: don't restart if they couldn't be saved.
            string problem = SaveAll();
            if (problem != null)
            {
                Notice(problem + " The theme will fully apply after the next restart.");
                RecreateViews();
                return;
            }
            restarting = true;
            try
            {
                Program.Trace("restarting for theme");
                Process.Start(Application.ExecutablePath, "--restart --from " + Process.GetCurrentProcess().Id + (Program.Portable ? " --portable" : ""));
                Close();
            }
            catch { restarting = false; RecreateViews(); }
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Window;
            row.ApplyTheme();
            vsplit.BackColor = Theme.Border;
            vsplit.Panel1.BackColor = vsplit.Panel2.BackColor = Theme.Window;
            foreach (LiveSplit s in new LiveSplit[] { treeSplit, previewSplit })
            {
                s.BackColor = Theme.Border;
                s.Panel1.BackColor = s.Panel2.BackColor = Theme.Window;
            }
            Shortcuts.ApplyTheme();
            tree.ApplyTheme();
            preview.ApplyTheme();
            for (int i = 0; i < 3; i++) titleBar.ThemeButtons[i].Checked = Theme.Mode == i;
            titleBar.Invalidate(true);
            status.BackColor = Theme.Bar;
            statusLeft.ForeColor = statusRight.ForeColor = Theme.TextDim;
            foreach (Pane p in Panes) p.ApplyTheme();
            ApplyTitleBar();
            Invalidate(true);
        }

        void ApplyTitleBar()
        {
            if (!IsHandleCreated) return;
            int dark = Theme.Dark ? 1 : 0;
            try { Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }

        // The Explorer view picks its colors when it is created, so rebuild open views after a theme or tree change.
        void RecreateViews()
        {
            if (!Ready) return;
            foreach (Pane p in Panes) foreach (BrowserTab t in p.Tabs) if (t.Created) t.Recreate();
        }

        public void SetShowShortcuts(bool show)
        {
            ShowShortcuts = show;
            vsplit.Panel2Collapsed = !show;
            UpdateLayoutButtons();
            StateChanged();
        }

        // Explorer view modes (FOLDERVIEWMODE, icon size): the six title-bar buttons, then all eight for the menu.
        static readonly int[,] ButtonViewModes = { { 4, 16 }, { 3, 16 }, { 6, 48 }, { 8, 32 }, { 1, 48 }, { 1, 96 } };
        public static readonly string[] AllViewNames = { "Extra large icons", "Large icons", "Medium icons", "Small icons", "List", "Details", "Tiles", "Content" };
        public static readonly int[,] AllViewModes = { { 1, 256 }, { 1, 96 }, { 1, 48 }, { 2, 16 }, { 3, 16 }, { 4, 16 }, { 6, 48 }, { 8, 32 } };

        public void SetViewMode(int mode, int size)
        {
            BrowserTab t = ActivePane.ActiveTab;
            if (t == null || !t.Created) return;
            t.SetViewMode(mode, size);
            UpdateViewButtons();
            t.Activate();
        }

        // Lights the button for the active pane's current view (Explorer remembers the view per folder).
        // The view every folder opens in, as an index into ButtonViewModes (settings key "defaultview"); -1 = none,
        // each folder keeps the view Windows remembers for it.
        int defaultView = -1;

        // Right-click on a view button: it becomes the default (or stops being it, if it was).
        void SetDefaultView(int i)
        {
            defaultView = defaultView == i ? -1 : i;
            for (int k = 0; k < 6; k++) titleBar.ViewButtons[k].Marked = k == defaultView;
            if (defaultView >= 0)
            {
                // Right away for the folders on screen, too.
                foreach (Pane p in Panes) if (p.Visible && p.ActiveTab != null && p.ActiveTab.Created) ApplyDefaultView(p.ActiveTab);
                UpdateViewButtons();
                Notice("Every folder now opens in " + TitleBar.ViewNames[i] + " view (right-click it again to stop).");
            }
            else Notice("No default view: each folder opens in the view it was last shown in.");
            StateChanged();
        }

        // Called whenever a tab has opened a folder (or Find results).
        public void ApplyDefaultView(BrowserTab t)
        {
            if (defaultView < 0 || t == null || !t.Created) return;
            int mode, size;
            if (t.GetViewMode(out mode, out size) && mode == ButtonViewModes[defaultView, 0] && size == ButtonViewModes[defaultView, 1]) return;
            t.SetViewMode(ButtonViewModes[defaultView, 0], ButtonViewModes[defaultView, 1]);
        }

        void UpdateViewButtons()
        {
            BrowserTab t = ActivePane == null ? null : ActivePane.ActiveTab;
            int mode = 0, size = 0;
            if (t != null && t.Created) t.GetViewMode(out mode, out size);
            for (int i = 0; i < 6; i++)
            {
                bool on;
                if (ButtonViewModes[i, 0] != 1) on = mode == ButtonViewModes[i, 0];
                else on = mode == 1 && (ButtonViewModes[i, 1] > 64 ? size > 64 : size <= 64);
                if (titleBar.ViewButtons[i].Checked != on) titleBar.ViewButtons[i].Checked = on;
            }
        }

        // 1-4 panes side by side. Hidden panes keep their tabs and come back when shown again.
        // With 1, only the active pane is shown and Tab switches which pane that is.
        public void SetPaneCount(int count)
        {
            count = Math.Max(1, Math.Min(Panes.Length, count));
            if (count > 1) multiCount = count;
            PaneCount = count;
            ApplyPaneLayout();
            UpdateLayoutButtons();
            UpdateStatus();
            ActiveFolderChanged();
            BrowserTab t = ActivePane.ActiveTab;
            if (t != null) BeginInvoke((MethodInvoker)t.Activate);
            StateChanged();
        }

        void UpdateLayoutButtons()
        {
            titleBar.LayoutButtons[0].Checked = ShowTree;
            for (int i = 1; i <= 4; i++) titleBar.LayoutButtons[i].Checked = PaneCount == i;
            titleBar.LayoutButtons[5].Checked = ShowPreview;
            titleBar.LayoutButtons[6].Checked = ShowShortcuts;
        }

        public void SetShowTree(bool show)
        {
            ShowTree = show;
            treeSplit.Panel1Collapsed = !show;
            UpdateLayoutButtons();
            if (show)
            {
                tree.EnsureCreated();
                ActiveFolderChanged();
            }
            StateChanged();
        }

        public void SetShowPreview(bool show)
        {
            ShowPreview = show;
            previewSplit.Panel2Collapsed = !show;
            UpdateLayoutButtons();
            if (show) UpdatePreview(); else preview.Clear();
            StateChanged();
        }

        public ContextMenuStrip NewMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new MenuRenderer();
            m.Font = Font;
            m.Closed += delegate { BeginInvoke((MethodInvoker)m.Dispose); };
            return m;
        }

        public void ShowHelp()
        {
            MessageBox.Show(this,
                "Double-click empty space\tGo up one level\n" +
                "Backspace / Alt+Up\tGo up one level\n" +
                "Alt+Left / Alt+Right\tBack / Forward\n" +
                "Tab\t\t\tSwitch pane\n" +
                "Alt+T / Alt+P / Alt+S\tTree / Preview / Shortcuts pane\n" +
                "Alt+1 ... Alt+4\t\tOne to four panes side by side\n" +
                "Double-click a divider\tMake the panes equal width\n" +
                "Ctrl+H\t\t\tShow / hide hidden files\n" +
                "Ctrl+E\t\t\tShow / hide file name extensions\n" +
                "Space\t\t\tQuick Look: a large preview (arrows: next file)\n" +
                "Click the address bar\tCopy the folder's path (double-click: type one)\n" +
                "Ctrl+F / F3\t\tFind in this folder and its subfolders\n" +
                "Ctrl+T\t\t\tNew tab\n" +
                "Ctrl+W / middle-click tab\tClose tab\n" +
                "Ctrl+Tab / Ctrl+Shift+Tab\tNext / previous tab\n" +
                "Ctrl+L / Alt+D / F4\tEdit address\n" +
                "Ctrl+Alt+T\t\tOpen a terminal in this folder\n" +
                "Right-click a tab\t\tLock, colour, duplicate, open in other pane\n" +
                "Drag a tab\t\tReorder tabs\n\n" +
                "Locked tabs always stay on their folder. Opening a folder from a locked tab opens it in a new tab.",
                "Keyboard shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- status bar

        // The total size of the selected items, counted in the background (folders too) whenever the selection
        // changes; null when there's nothing to show.
        SelectionSize selSize;
        string selKey;

        string SelectionSizeText(BrowserTab t, int sel)
        {
            string key = t.SelectionKey(sel);
            if (key == null) { ForgetSelectionSize(); return null; }
            key = t.GetHashCode() + "|" + key;
            if (key != selKey)
            {
                ForgetSelectionSize();
                selKey = key;
                // Listing a huge selection would hold up the window: no total for it.
                List<string> paths = sel <= 20000 ? t.SelectedPaths(20000) : null;
                if (paths != null && paths.Count > 0)
                    selSize = SelectionSize.Start(paths, delegate(SelectionSize j)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { if (j == selSize) UpdateStatus(); }); } catch { }
                    });
            }
            return selSize == null ? null : selSize.Text;
        }

        void ForgetSelectionSize()
        {
            if (selSize != null) selSize.Cancel = true;
            selSize = null;
            selKey = null;
        }

        // ---- Quick Look

        QuickLook quickLook;
        int typedTick = Environment.TickCount - 100000; // the last letter or digit typed (type-to-select in a list)

        // A large preview of the selected item; false when nothing that can be previewed is selected.
        bool ShowQuickLook(BrowserTab t)
        {
            string path = t.SelectedPath();
            if (path == null)
            {
                List<string> sel = t.SelectedPaths(1);
                if (sel.Count == 0) return false;
                path = sel[0];
            }
            if (quickLook != null) quickLook.Close();
            quickLook = new QuickLook(this, t, path);
            quickLook.Show(this);
            Program.Trace("quick look: " + path);
            return true;
        }

        public void QuickLookClosed(QuickLook q)
        {
            if (quickLook != q) return;
            quickLook = null;
            BrowserTab t = ActivePane == null ? null : ActivePane.ActiveTab;
            if (t != null && !IsDisposed) { Activate(); t.Activate(); }
        }

        public void UpdateStatus()
        {
            BrowserTab t = ActivePane == null ? null : ActivePane.ActiveTab;
            if (t == null || !t.Created) return;
            int all = t.Count(Native.SVGIO_ALLVIEW), sel = t.Count(Native.SVGIO_SELECTION);
            string s = t.IsFindResults ? FindStatus(t, all) : all < 0 ? "" : all + (all == 1 ? " item" : " items");
            if (sel > 0)
            {
                s += "     " + sel + " selected";
                string size = SelectionSizeText(t, sel);
                if (size != null) s += " (" + size + ")";
            }
            else ForgetSelectionSize();
            if (FolderSizes)
            {
                if (sizeJob != null && sizeJob.Skipped != null)
                    s += "     Folder size: " + sizeJob.Skipped;
                else if (sizeJob != null)
                    s += "     Folder size: " + (sizeJob.Errors > 0 ? "at least " : "") + Util.FormatBytes(sizeJob.TotalBytes) + (sizeJob.Finished ? "" : " (calculatingâ€¦)");
                else if (sizeSkip != null)
                    s += "     Folder size: " + sizeSkip;
            }
            string sn = Shortcuts == null ? null : Shortcuts.NoticeText;
            if (!ShowShortcuts && sn != null) s += "     " + sn;
            if (statusLeft.Text != s) statusLeft.Text = s;

            string root = null;
            try { if (t.Address.Length > 2 && (t.Address[1] == ':' || t.Address.StartsWith(@"\\"))) root = Path.GetPathRoot(t.Address); } catch { }
            // A slow or disconnected network drive must not freeze the window: ask in the background. Each
            // question has a number and only the newest answer is shown; a query still stuck on one drive
            // doesn't stop questions about another.
            bool moved = root != freeRoot;
            if (moved)
            {
                freeRoot = root;
                statusRight.Text = ""; // never show the previous drive's numbers for this one
            }
            if ((moved || unchecked(Environment.TickCount - freeTick) > 5000) && root != null && !freeBusy.Contains(root))
            {
                freeTick = Environment.TickCount;
                {
                    freeBusy.Add(root);
                    string askRoot = root;
                    int gen = ++freeGen;
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        ulong free, total, totalFree;
                        string f = "";
                        try
                        {
                            if (Native.GetDiskFreeSpaceEx(askRoot, out free, out total, out totalFree) && total > 0)
                                f = Util.FormatDiskSize(free) + " free of " + Util.FormatDiskSize(total) + " (" + (100 * free / total) + "%)";
                        }
                        catch { }
                        try { BeginInvoke((MethodInvoker)delegate { freeBusy.Remove(askRoot); if (gen == freeGen && askRoot == freeRoot) statusRight.Text = f; }); } catch { }
                    });
                }
            }
            if (noticeText != null && unchecked(Environment.TickCount - noticeTick) < 6000) s += "     " + noticeText;
            else if (available != null) s += "     â¬† Version " + Util.FormatVersion(available.Version) + " is available: menu â€º Update";
            if (stateSaveError != null) s += "     \u26A0 Settings couldn't be saved (" + stateSaveError + "); retrying.";
            if (statusLeft.Text != s) statusLeft.Text = s;
        }

        // ---- input routing

        // An exception thrown here would end the app (message filters run outside WinForms' error handler).
        bool IMessageFilter.PreFilterMessage(ref Message m)
        {
            try { return FilterMessage(ref m); }
            catch (Exception ex)
            {
                Program.LogError(ex);
                try { Notice("\u26A0 " + ex.Message); } catch { }
                return false;
            }
        }

        bool FilterMessage(ref Message m)
        {
            int msg = m.Msg;
            if (msg == 0x201 || msg == 0x204 || msg == 0x207)
            {
                foreach (Pane p in Panes) if (Native.Contains(p.Handle, m.HWnd)) { SetActivePane(p); break; }
                return false;
            }
            if (msg < 0x100 || msg > 0x109) return false;

            IntPtr focus = Native.GetFocus();
            BrowserTab ft = null;
            foreach (Pane p in Panes)
            {
                BrowserTab t = p.ActiveTab;
                if (!p.Visible) continue; // a pane hidden by single-pane mode
                if (t != null && t.Created && Native.Contains(t.Host.Handle, focus)) { ft = t; if (ActivePane != p) SetActivePane(p); }
            }
            if ((msg == 0x100 || msg == 0x104) && Shortcut((Keys)(int)m.WParam & Keys.KeyCode, ft, focus)) return true;
            if (ft != null)
            {
                MSG native = new MSG();
                native.hwnd = m.HWnd; native.message = m.Msg; native.wParam = m.WParam; native.lParam = m.LParam;
                if (ft.TranslateAccelerator(ref native) == 0) return true;
            }
            return false;
        }

        bool Shortcut(Keys key, BrowserTab ft, IntPtr focus)
        {
            bool ctrl = Native.KeyDown(0x11), shift = Native.KeyDown(0x10), alt = Native.KeyDown(0x12);
            if (Form.ActiveForm != this) return false;
            if (!ctrl && !alt && (key >= Keys.A && key <= Keys.Z || key >= Keys.D0 && key <= Keys.D9 || key >= Keys.NumPad0 && key <= Keys.NumPad9 || key >= Keys.Oem1))
                typedTick = Environment.TickCount;
            string fc = focus == IntPtr.Zero ? "" : Native.ClassName(focus);
            bool typing = fc == "Edit" || fc.Contains(".EDIT.") || fc.Contains("COMBOBOX") || fc == "ComboBox";
            if (typing)
            {
                bool addressKey = (ctrl && !alt && key == Keys.L) || (alt && !ctrl && key == Keys.D);
                if (!addressKey) return false;
            }
            Pane p = ActivePane;
            BrowserTab t = p.ActiveTab;
            if (ctrl && !alt && !shift && key == Keys.F || !ctrl && !alt && !shift && key == Keys.F3) { ShowFind(); return true; }
            if (ctrl && alt && !shift && key == Keys.T) { OpenTerminal(t); return true; }
            if (ctrl && !alt && key == Keys.T) { p.NewTab(); return true; }
            if (ctrl && !alt && key == Keys.W) { if (t != null) p.CloseTab(t); return true; }
            if (ctrl && !alt && key == Keys.Tab) { p.CycleTab(shift ? -1 : 1); return true; }
            if (alt && !ctrl && key == Keys.Left) { p.Nav(Native.SBSP_NAVIGATEBACK); return true; }
            if (alt && !ctrl && key == Keys.Right) { p.Nav(Native.SBSP_NAVIGATEFORWARD); return true; }
            if (alt && !ctrl && key == Keys.Up) { p.Nav(Native.SBSP_PARENT); return true; }
            if (ctrl && !alt && !shift && key == Keys.H) { ToggleHidden(); return true; }
            if (ctrl && !alt && !shift && key == Keys.E) { ToggleExtensions(); return true; }
            if (alt && !ctrl && key == Keys.T) { SetShowTree(!ShowTree); return true; }
            if (alt && !ctrl && key == Keys.P) { SetShowPreview(!ShowPreview); return true; }
            if (alt && !ctrl && key == Keys.S) { SetShowShortcuts(!ShowShortcuts); return true; }
            if (alt && !ctrl && key >= Keys.D1 && key <= Keys.D4) { SetPaneCount(key - Keys.D0); return true; }
            if ((ctrl && !alt && key == Keys.L) || (alt && !ctrl && key == Keys.D) || (!ctrl && !alt && key == Keys.F4)) { p.FocusAddress(); return true; }
            bool renaming = ft != null && Native.ClassName(focus) == "Edit";
            if (ft != null && !renaming && !ctrl && !alt)
            {
                if (key == Keys.Back) { p.Nav(Native.SBSP_PARENT); return true; }
                // Space: Quick Look, unless it's part of a name being typed to jump to a file ("my notes").
                if (key == Keys.Space && !shift && unchecked(Environment.TickCount - typedTick) > 1000 && ShowQuickLook(ft)) return true;
                if (key == Keys.Tab && !shift)
                {
                    Pane o = Other(p);
                    SetActivePane(o);
                    if (o.ActiveTab != null) o.ActiveTab.Activate();
                    return true;
                }
            }
            return false;
        }

        // Double-click on empty space in a view goes up one level. The Explorer items view detects
        // double-clicks itself (no WM_LBUTTONDBLCLK), so we time the two button-downs ourselves.
        // The first click of the pair has already cleared the selection if it landed on empty space,
        // so "nothing selected" on the second click means "empty space".
        IntPtr lastDownHwnd;
        int lastDownTick;
        POINT lastDownPt;
        bool lastDownInActivePane;

        IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                IntPtr r = MouseHookCore(code, wParam, lParam);
                if (r != IntPtr.Zero) return r;
            }
            catch { }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }

        // Returns 1 to swallow the click, 0 to pass it on.
        IntPtr MouseHookCore(int code, IntPtr wParam, IntPtr lParam)
        {
            int msg = (int)wParam;
            // HC_ACTION only: the hook is also called (HC_NOREMOVE) when a message is merely peeked at,
            // which would count one click twice and turn every single click into a "double-click".
            const int HC_ACTION = 0;
            if (code == HC_ACTION && (msg == 0x204 || msg == 0x207))
            {
                // Right/middle click in a pane also makes it the active one.
                MOUSEHOOKSTRUCT rs = (MOUSEHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MOUSEHOOKSTRUCT));
                Pane p = PaneOf(rs.hwnd);
                if (p != null && p != ActivePane) BeginInvoke((MethodInvoker)delegate { SetActivePane(p); });
            }
            if (code == HC_ACTION && (msg == 0x201 || msg == Native.WM_LBUTTONDBLCLK))
            {
                MOUSEHOOKSTRUCT hs = (MOUSEHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MOUSEHOOKSTRUCT));
                // The hook runs before our message filter makes the clicked pane active,
                // so this tells whether the pane was already active when the click arrived.
                Pane clicked = PaneOf(hs.hwnd);
                bool inActive = clicked != null && clicked == ActivePane;
                if (clicked != null && !inActive) BeginInvoke((MethodInvoker)delegate { SetActivePane(clicked); });
                bool dbl = msg == Native.WM_LBUTTONDBLCLK;
                if (!dbl)
                {
                    int now = Environment.TickCount;
                    Size slop = SystemInformation.DoubleClickSize;
                    dbl = hs.hwnd == lastDownHwnd && unchecked(now - lastDownTick) <= SystemInformation.DoubleClickTime &&
                          Math.Abs(hs.pt.x - lastDownPt.x) <= slop.Width && Math.Abs(hs.pt.y - lastDownPt.y) <= slop.Height;
                    // A double-click only counts if its first click landed in the pane that was already active:
                    // the first click on an inactive pane just activates it (and selects what it hit).
                    dbl = dbl && lastDownInActivePane;
                    lastDownHwnd = dbl ? IntPtr.Zero : hs.hwnd;
                    lastDownTick = now;
                    lastDownPt = hs.pt;
                    lastDownInActivePane = inActive;
                }
                else dbl = inActive;
                if (dbl && !Native.KeyDown(0x11) && !Native.KeyDown(0x10) && TryGoUpFromEmptySpace(hs.hwnd)) return (IntPtr)1;
            }
            return IntPtr.Zero;
        }

        Pane PaneOf(IntPtr hwnd)
        {
            foreach (Pane p in Panes) if (p.IsHandleCreated && Native.Contains(p.Handle, hwnd)) return p;
            return null;
        }

        bool TryGoUpFromEmptySpace(IntPtr hwnd)
        {
            string cls = Native.ClassName(hwnd);
            if (cls != "DirectUIHWND" && cls != "SysListView32") return false;
            foreach (Pane p in Panes)
            {
                BrowserTab t = p.ActiveTab;
                if (t == null || !t.Created) continue;
                if (Native.IsChild(t.Host.Handle, hwnd) && t.Count(Native.SVGIO_SELECTION) == 0)
                {
                    BeginInvoke((MethodInvoker)t.GoUp);
                    return true;
                }
            }
            return false;
        }

        // mode 0 = active tab, 1 = new tab in the active pane, 2 = the other pane
        public void FolderRenamed(string oldPath, string newPath)
        {
            foreach (Pane p in Panes)
                foreach (BrowserTab t in p.Tabs)
                {
                    string locked = Util.Rebase(t.LockedFolder, oldPath, newPath);
                    if (locked != null) t.LockedFolder = locked;
                    string moved = Util.Rebase(t.Folder, oldPath, newPath);
                    if (moved == null) continue;
                    if (t.Created) t.Navigate(moved);
                    else
                    {
                        t.Folder = t.Address = moved;
                        t.Title = Path.GetFileName(moved.TrimEnd('\\'));
                    }
                }
            foreach (Pane p in Panes) p.RefreshTabs();
            StateChanged();
        }

        public void OpenFolder(string path, int mode, bool focusView = true)
        {
            Pane p = mode == 2 ? Other(ActivePane) : ActivePane;
            BrowserTab t = p.ActiveTab;
            if (mode == 1 || t == null) { p.AddTab(path, false, true); return; }
            // A Find results tab "is" in its searched folder, but opening that folder should still show it.
            if ((t.IsFindResults || !Util.SameFolder(path, t.Folder)) && !t.Navigate(path)) { SystemSounds.Beep.Play(); return; }
            SetActivePane(p);
            if (focusView) t.Activate();
        }

        // ---- persistence

        void LoadState()
        {
            List<string>[] tabs = { new List<string>(), new List<string>(), new List<string>(), new List<string>() };
            List<int>[] colors = { new List<int>(), new List<int>(), new List<int>(), new List<int>() }; // per tab line
            List<KeyValuePair<string, string>> legacyShortcuts = new List<KeyValuePair<string, string>>();
            int[] sel = { 0, 0, 0, 0 };
            try
            {
                string[] stateLines = SettingsFile.ReadLines(StateFile);
                if (stateLines != null)
                {
                    foreach (string line in stateLines)
                    try
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                        int n;
                        switch (k)
                        {
                            case "window":
                                string[] a = v.Split(',');
                                int wx, wy, ww, wh;
                                if (a.Length == 4 && int.TryParse(a[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out wx) &&
                                    int.TryParse(a[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out wy) &&
                                    int.TryParse(a[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ww) &&
                                    int.TryParse(a[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out wh) && ww > 200 && wh > 150)
                                {
                                    // Moved back on screen if its title bar would be off it (a monitor unplugged or
                                    // rearranged since): otherwise it couldn't be dragged, maximized or closed.
                                    List<Rectangle> areas = new List<Rectangle>();
                                    areas.Add(Screen.PrimaryScreen.WorkingArea); // first: where a window that's nowhere goes
                                    foreach (Screen s in Screen.AllScreens) if (!s.Primary) areas.Add(s.WorkingArea);
                                    StartPosition = FormStartPosition.Manual;
                                    Bounds = WindowPlacement.Fit(new Rectangle(wx, wy, ww, wh), areas, Native.Px(34), Native.Px(120));
                                }
                                break;
                            case "maximized": if (v == "1") WindowState = FormWindowState.Maximized; break;
                            case "split": float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out legacySplit); break;
                            case "panes": if (int.TryParse(v, out n) && n >= 1 && n <= 4) PaneCount = n; break;
                            case "multipanes": if (int.TryParse(v, out n) && n >= 2 && n <= 4) multiCount = n; break;
                            case "paneweights":
                                string[] ws = v.Split(',');
                                for (int wi = 0; wi < ws.Length && wi < row.Weights.Length; wi++)
                                {
                                    float wv;
                                    if (float.TryParse(ws[wi], NumberStyles.Float, CultureInfo.InvariantCulture, out wv) && wv > 0.05f && wv < 20f) row.Weights[wi] = wv;
                                }
                                break;
                            case "treepane": ShowTree = v != "0"; break;
                            case "preview": ShowPreview = v == "1"; break;
                            case "single": if (v == "1") PaneCount = 1; break; // older settings files
                            case "autofit": AutoFit = v != "0"; break;
                            case "foldersizes": FolderSizes = v == "1"; break;
                            case "autoupdate": AutoUpdateCheck = v != "0"; break;
                            case "find": if (v.Trim().Length > 0 && findHistory.Count < FindQuery.HistorySize) findHistory.Add(v); break;
                            case "findcontents": findContents = v == "1"; break;
                            case "updatecheck": long ut; if (long.TryParse(v, out ut) && ut > 0 && ut <= DateTime.MaxValue.Ticks) lastUpdateCheck = new DateTime(ut, DateTimeKind.Utc); break;
                            case "treewidth": if (int.TryParse(v, out n) && n >= 80 && n <= 5000) treeWidth = n; break;
                            case "previewwidth": if (int.TryParse(v, out n) && n >= 100 && n <= 5000) previewWidth = n; break;
                            case "shortcuts": ShowShortcuts = v != "0"; break;
                            case "shortcutsheight": if (int.TryParse(v, out n) && n >= 40 && n <= 5000) shortcutsHeight = n; break;
                            case "shortcutswidth": Shortcuts.WidthSetting = v; break;
                            case "shortcutsort": if (int.TryParse(v, out n)) Shortcuts.SortMode = n; break;
                            case "shortcut":
                                int bar = v.IndexOf('|');
                                if (bar > 0) legacyShortcuts.Add(new KeyValuePair<string, string>(v.Substring(0, bar), v.Substring(bar + 1)));
                                break;
                            case "theme": if (int.TryParse(v, out n) && n >= 0 && n <= 2) Theme.Mode = n; break;
                            case "defaultview": if (int.TryParse(v, out n) && n >= 0 && n < 6) { defaultView = n; titleBar.ViewButtons[n].Marked = true; } break;
                            case "activepane": if (int.TryParse(v, out n) && n >= 0 && n < Panes.Length) startPane = n; break;
                            default:
                                int pi; bool isTab;
                                if (SettingsFile.TryParseTabColorKey(k, out pi))
                                {
                                    if (colors[pi].Count > 0 && int.TryParse(v, out n)) colors[pi][colors[pi].Count - 1] = LayoutFile.ClampColor(n);
                                }
                                else if (SettingsFile.TryParsePaneKey(k, out pi, out isTab))
                                {
                                    if (isTab) { tabs[pi].Add(v); colors[pi].Add(0); }
                                    else int.TryParse(v, out sel[pi]);
                                }
                                break;
                        }
                    }
                    catch { } // a damaged line is skipped; the rest of the file still loads
                }
            }
            catch { }
            // Older files stored one split ratio for two panes.
            if (legacySplit >= 0.1f && legacySplit <= 0.9f) { row.Weights[0] = legacySplit * 2; row.Weights[1] = (1 - legacySplit) * 2; }
            Shortcuts.LoadOrMigrate(legacyShortcuts);

            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string[] defaults = { Native.ThisPC, Directory.Exists(downloads) ? downloads : Native.ThisPC,
                Directory.Exists(desktop) ? desktop : Native.ThisPC, Directory.Exists(documents) ? documents : Native.ThisPC };
            for (int i = 0; i < Panes.Length; i++)
            {
                for (int j = 0; j < tabs[i].Count; j++)
                {
                    bool locked; string folder;
                    if (SettingsFile.TryParseTab(tabs[i][j], out locked, out folder) && folder.Trim().Length > 0)
                        Panes[i].AddTab(folder, locked, false, true).Color = colors[i][j];
                }
                // A pane whose saved tabs were all unusable (or that had none) still gets one.
                if (Panes[i].Tabs.Count == 0) Panes[i].AddTab(defaults[i], false, false, true);
                Panes[i].Select(Math.Max(0, Math.Min(sel[i], Panes[i].Tabs.Count - 1)));
            }
        }

        // Saves the settings and pending shortcut changes. Null when both are saved, otherwise what failed.
        string SaveAll()
        {
            bool state = SaveState(), shortcuts = Shortcuts.FlushPending();
            if (state && shortcuts) return null;
            return !state ? "Your settings couldn't be saved (" + stateSaveError + ")." + (shortcuts ? "" : " Your shortcut changes couldn't be saved either.")
                : "Your shortcut changes couldn't be saved.";
        }

        bool SaveState()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                sb.AppendLine("window=" + b.X + "," + b.Y + "," + b.Width + "," + b.Height);
                sb.AppendLine("maximized=" + (WindowState == FormWindowState.Maximized ? "1" : "0"));
                sb.AppendLine("panes=" + PaneCount);
                sb.AppendLine("multipanes=" + multiCount);
                string[] wsOut = new string[row.Weights.Length];
                for (int wi = 0; wi < wsOut.Length; wi++) wsOut[wi] = row.Weights[wi].ToString("0.000", CultureInfo.InvariantCulture);
                sb.AppendLine("paneweights=" + string.Join(",", wsOut));
                sb.AppendLine("treepane=" + (ShowTree ? "1" : "0"));
                sb.AppendLine("preview=" + (ShowPreview ? "1" : "0"));
                sb.AppendLine("autofit=" + (AutoFit ? "1" : "0"));
                sb.AppendLine("foldersizes=" + (FolderSizes ? "1" : "0"));
                sb.AppendLine("autoupdate=" + (AutoUpdateCheck ? "1" : "0"));
                sb.AppendLine("findcontents=" + (findContents ? "1" : "0"));
                foreach (string f in findHistory) sb.AppendLine("find=" + f.Replace("\r", " ").Replace("\n", " "));
                sb.AppendLine("updatecheck=" + lastUpdateCheck.Ticks);
                int tw = Ready && ShowTree ? treeSplit.SplitterDistance : Native.Px(treeWidth);
                int pw = Ready && ShowPreview ? previewSplit.Panel2.Width : Native.Px(previewWidth);
                sb.AppendLine("treewidth=" + (int)Math.Round(tw * 100.0 / Native.Px(100)));
                sb.AppendLine("previewwidth=" + (int)Math.Round(pw * 100.0 / Native.Px(100)));
                sb.AppendLine("shortcuts=" + (ShowShortcuts ? "1" : "0"));
                int h = Ready && ShowShortcuts ? vsplit.Panel2.Height : Native.Px(shortcutsHeight);
                sb.AppendLine("shortcutsheight=" + (int)Math.Round(h * 100.0 / Native.Px(100)));
                sb.AppendLine("shortcutswidth=" + Shortcuts.WidthSetting);
                sb.AppendLine("shortcutsort=" + Shortcuts.SortMode);
                sb.AppendLine("theme=" + Theme.Mode);
                sb.AppendLine("defaultview=" + defaultView);
                sb.AppendLine("activepane=" + Array.IndexOf(Panes, ActivePane));
                for (int i = 0; i < Panes.Length; i++)
                {
                    sb.AppendLine("pane" + i + ".active=" + Panes[i].ActiveIndex);
                    foreach (BrowserTab t in Panes[i].Tabs)
                    {
                        sb.AppendLine("pane" + i + ".tab=" + SettingsFile.FormatTab(t.Locked, t.SavedFolder));
                        if (t.Color != 0) sb.AppendLine("pane" + i + ".tabcolor=" + t.Color);
                    }
                }
                // Shortcuts that couldn't be moved to the shared file yet stay here so they aren't lost.
                if (Shortcuts.PendingLegacy != null)
                    foreach (KeyValuePair<string, string> s in Shortcuts.PendingLegacy) sb.AppendLine("shortcut=" + s.Key + "|" + s.Value);
                Util.WriteAllTextAtomic(StateFile, sb.ToString());
                stateSaveError = null;
            }
            catch (Exception ex)
            {
                if (stateSaveError != ex.Message) Program.LogError(ex); // retried every 10 s: logged once
                stateSaveError = ex.Message;
                saveTimer.Stop();
                saveTimer.Interval = 10000; // retry
                saveTimer.Start();
                UpdateStatus();
                return false;
            }
            saveTimer.Interval = 1500;
            return true;
        }
    }
}
