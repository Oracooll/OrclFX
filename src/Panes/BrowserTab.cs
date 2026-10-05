// Orcl File Explorer: One tab: an embedded Windows Explorer view (IExplorerBrowser).
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
    [ComVisible(true)]
    public class BrowserTab : IExplorerBrowserEvents, IOleServiceProvider, ICommDlgBrowser
    {
        internal readonly Pane Pane;
        public readonly Panel Host = new Panel();
        IExplorerBrowser browser;
        uint cookie;
        public string Folder, Title, Address, LockedFolder;
        public Icon Icon;
        public bool Locked;

        internal BrowserTab(Pane pane, string folder, bool locked)
        {
            Pane = pane;
            Folder = Title = Address = folder;
            IntPtr pidl = Native.ParsePath(folder);
            if (pidl != IntPtr.Zero) try { ReadNames(pidl); } finally { Marshal.FreeCoTaskMem(pidl); }
            Locked = locked;
            if (locked) LockedFolder = Folder;
            Host.Dock = DockStyle.Fill;
            Host.Visible = false;
            Host.BackColor = Theme.Window;
            Host.Resize += delegate { Resize(); };
        }

        public bool Created { get { return browser != null; } }
        public string Tooltip { get { return (Locked ? "Locked tab: " : "") + Address; } }

        void ReadNames(IntPtr pidl)
        {
            Folder = Native.GetName(pidl, Native.SIGDN_DESKTOPABSOLUTEPARSING) ?? Folder;
            Title = Native.GetName(pidl, Native.SIGDN_NORMALDISPLAY) ?? Folder;
            Address = Native.GetName(pidl, Native.SIGDN_FILESYSPATH) ?? Title;
            Icon old = Icon;
            Icon = Native.SmallIcon(pidl);
            if (old != null) old.Dispose();
        }

        public void EnsureCreated() { EnsureCreated(true); }

        void EnsureCreated(bool navigate)
        {
            if (browser != null) return;
            browser = (IExplorerBrowser)Activator.CreateInstance(Type.GetTypeFromCLSID(Native.CLSID_ExplorerBrowser));
            RECT rc = Rect();
            FOLDERSETTINGS fs = new FOLDERSETTINGS();
            fs.ViewMode = 4; // details
            if (browser.Initialize(Host.Handle, ref rc, ref fs) != 0)
            {
                Marshal.ReleaseComObject(browser);
                browser = null;
                return;
            }
            browser.SetOptions(Native.EBO_NOBORDER);
            browser.SetPropertyBag("DualPane");
            browser.Advise(this, out cookie);
            if (!navigate) return;
            if (!Navigate(Locked ? LockedFolder : Folder) && !Locked) Navigate(Native.ThisPC);
        }

        public void Recreate()
        {
            Destroy();
            if (Pane.ActiveTab == this) { EnsureCreated(); Resize(); }
        }

        RECT Rect()
        {
            RECT r = new RECT();
            r.right = Host.ClientSize.Width;
            r.bottom = Host.ClientSize.Height;
            return r;
        }

        public void Resize()
        {
            if (browser == null) return;
            IntPtr hdwp = IntPtr.Zero;
            browser.SetRect(ref hdwp, Rect());
        }

        public bool Navigate(string path)
        {
            if (browser == null)
            {
                // Not shown yet: a locked tab still never leaves its folder; the folder opens in a new tab, as it
                // would from a shown one.
                if (Locked && !Util.SameFolder(path, LockedFolder)) { Pane.AddTab(path, false, true); return true; }
                Folder = path;
                return true;
            }
            IntPtr pidl = Native.ParsePath(path);
            if (pidl == IntPtr.Zero) return false;
            int hr;
            try { hr = browser.BrowseToIDList(pidl, Native.SBSP_ABSOLUTE); } finally { Marshal.FreeCoTaskMem(pidl); }
            // A locked tab cancels the navigation on purpose and opens a new tab instead: that counts as success.
            return hr == 0 || hr == Native.HRESULT_CANCELLED;
        }

        public void Nav(uint flags)
        {
            if (browser == null) return;
            // From Find results, Back and Up return to the folder that was searched.
            if (IsFindResults && (flags == Native.SBSP_PARENT || flags == Native.SBSP_NAVIGATEBACK)) { LeaveResults(FindRoot); return; }
            browser.BrowseToIDList(IntPtr.Zero, flags);
        }
        public void GoUp() { Nav(Native.SBSP_PARENT); }

        T View<T>(Guid iid) where T : class
        {
            if (browser == null) return null;
            object o;
            if (browser.GetCurrentView(ref iid, out o) != 0) return null;
            return o as T;
        }

        public IntPtr ViewWindow()
        {
            return Native.FindChild(Host.Handle, "SHELLDLL_DefView");
        }

        public int Count(uint what)
        {
            IFolderView v = View<IFolderView>(Native.IID_IFolderView);
            if (v == null) return -1;
            int n;
            int hr = v.ItemCount(what, out n);
            Marshal.ReleaseComObject(v);
            return hr == 0 ? n : -1;
        }

        // FOLDERVIEWMODE (1 icon, 2 small icon, 3 list, 4 details, 6 tile, 8 content) and icon size in pixels.
        public bool GetViewMode(out int mode, out int size)
        {
            mode = size = 0;
            IFolderView2 v = View<IFolderView2>(new Guid("1af3a467-214f-4298-908e-06b03e0b39f9"));
            if (v == null) return false;
            try { return v.GetViewModeAndIconSize(out mode, out size) == 0; }
            catch { return false; }
            finally { Marshal.ReleaseComObject(v); }
        }

        public void SetViewMode(int mode, int size)
        {
            IFolderView2 v = View<IFolderView2>(new Guid("1af3a467-214f-4298-908e-06b03e0b39f9"));
            if (v == null) return;
            try { v.SetViewModeAndIconSize(mode, size); } catch { }
            finally { Marshal.ReleaseComObject(v); }
            fitCount = -1; // re-fit the Name column if this is Details
        }

        public void RefreshView()
        {
            IShellView v = View<IShellView>(new Guid("000214E3-0000-0000-C000-000000000046"));
            if (v == null) return;
            try { v.Refresh(); } catch { }
            Marshal.ReleaseComObject(v);
        }

        // Auto-fit is re-applied when the folder or its item count changes.
        internal int fitCount = -1;
        internal string fitFolder;

        // Sizes the Name column to its ideal width (the longest name), in Details view only.
        public void AutoFitName()
        {
            IFolderView fv = View<IFolderView>(Native.IID_IFolderView);
            if (fv == null) return;
            uint mode;
            bool details = fv.GetCurrentViewMode(out mode) == 0 && mode == 4;
            Marshal.ReleaseComObject(fv);
            if (!details) return;
            IColumnManager cm = View<IColumnManager>(new Guid("d8ec27bb-3f3b-4042-b10a-4acfd924d453"));
            if (cm == null) return;
            try
            {
                PROPERTYKEY name = new PROPERTYKEY();
                name.fmtid = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC");
                name.pid = 10; // System.ItemNameDisplay
                CM_COLUMNINFO ci = new CM_COLUMNINFO();
                ci.cbSize = (uint)Marshal.SizeOf(typeof(CM_COLUMNINFO));
                ci.dwMask = 0x1 | 0x4; // CM_MASK_WIDTH | CM_MASK_IDEALWIDTH
                if (cm.GetColumnInfo(ref name, ref ci) != 0 || ci.uIdealWidth == 0) return;
                uint want = Math.Min(ci.uIdealWidth + (uint)Native.Px(12), (uint)Math.Max(Native.Px(150), Host.ClientSize.Width * 7 / 10));
                if (Math.Abs((int)want - (int)ci.uWidth) < 3) return;
                ci.dwMask = 0x1;
                ci.uWidth = want;
                cm.SetColumnInfo(ref name, ref ci);
            }
            catch { }
            finally { Marshal.ReleaseComObject(cm); }
        }

        // Full path of the single selected item, or null when nothing or several are selected.
        public string SelectedPath()
        {
            IFolderView v = View<IFolderView>(Native.IID_IFolderView);
            if (v == null) return null;
            string path = null;
            try
            {
                Guid iid = Native.IID_IShellItemArray;
                object o;
                if (v.Items(Native.SVGIO_SELECTION, ref iid, out o) == 0)
                {
                    IShellItemArray arr = o as IShellItemArray;
                    uint n;
                    IShellItem item;
                    if (arr != null && arr.GetCount(out n) == 0 && n == 1 && arr.GetItemAt(0, out item) == 0)
                    {
                        path = Native.ItemName(item, Native.SIGDN_FILESYSPATH);
                        Marshal.ReleaseComObject(item);
                    }
                    if (o != null) Marshal.ReleaseComObject(o);
                }
            }
            catch { }
            Marshal.ReleaseComObject(v);
            return path;
        }

        // Test hook: selects the item whose path ends with name and runs its default action (like a double-click).
        internal bool TestOpenItem(string name)
        {
            List<string> paths = ItemPaths(1000);
            int i = paths.FindIndex(delegate(string x) { return x != null && x.EndsWith(name, StringComparison.OrdinalIgnoreCase); });
            IFolderView2 v = View<IFolderView2>(new Guid("1af3a467-214f-4298-908e-06b03e0b39f9"));
            if (v == null || i < 0) return false;
            try { return v.SelectItem(i, 0x1 | 0x4 | 0x10) == 0 && v.InvokeVerbOnSelection(null) == 0; }
            finally { Marshal.ReleaseComObject(v); }
        }

        // Paths of the first items in the view (for the test hooks' log).
        internal List<string> ItemPaths(int max)
        {
            List<string> r = new List<string>();
            IFolderView v = View<IFolderView>(Native.IID_IFolderView);
            if (v == null) return r;
            try
            {
                Guid iid = Native.IID_IShellItemArray;
                object o;
                if (v.Items(Native.SVGIO_ALLVIEW, ref iid, out o) != 0) return r;
                IShellItemArray arr = o as IShellItemArray;
                uint n;
                if (arr != null && arr.GetCount(out n) == 0)
                    for (uint i = 0; i < n && i < max; i++)
                    {
                        IShellItem item;
                        if (arr.GetItemAt(i, out item) != 0) continue;
                        r.Add(Native.ItemName(item, Native.SIGDN_DESKTOPABSOLUTEPARSING));
                        Marshal.ReleaseComObject(item);
                    }
                if (o != null) Marshal.ReleaseComObject(o);
            }
            catch { }
            finally { Marshal.ReleaseComObject(v); }
            return r;
        }

        public int TranslateAccelerator(ref MSG msg)
        {
            IInputObject io = browser as IInputObject;
            return io == null ? 1 : io.TranslateAcceleratorIO(ref msg);
        }

        public void Activate()
        {
            if (Host.IsDisposed || browser == null) return;
            IntPtr h = ViewWindow();
            if (h != IntPtr.Zero) Native.SetFocus(h);
        }

        // ---- Find results
        // A tab showing Find results: FindText is what was searched for, FindRoot the folder searched (with its
        // subfolders). Folder stays FindRoot, so saving the tabs, New tab and the tree all use that folder.
        public string FindText, FindRoot;
        public bool FindInContents;   // the results come from Windows Search (names and contents)
        internal FileSearch Search;   // our own name search, while it runs or after it finished
        IResultsFolder results;
        bool findPending;             // results list requested, waiting for the view to show it
        readonly List<string> waiting = new List<string>();
        public bool IsFindResults { get { return FindText != null; } }

        // Shows an empty results list in this tab and fills it with what a name search finds.
        public void StartFind(string root, string text)
        {
            ClearFind(); // earlier results: their list is about to be replaced, and their search stopped
            FindText = text;
            FindRoot = root;
            FindInContents = false;
            findPending = true;
            if (!FillResults()) return;
            Search = FileSearch.Start(root, text, delegate(FileSearch s, List<string> batch)
            {
                try { Pane.BeginInvoke((MethodInvoker)delegate { Found(s, batch); }); } catch { }
            });
            SetFindNames();
            Pane.TabNavigated(this);
        }

        // Names and contents: Windows Search (the same as File Explorer's search box) shows its results here.
        public void StartWindowsSearch(string root, string text)
        {
            ClearFind();
            // A fresh view goes straight to the search (a new tab would otherwise still be opening its folder).
            DestroyBrowser();
            EnsureCreated(false);
            Resize();
            FindText = text;
            FindRoot = root;
            FindInContents = true;
            findPending = true;
            IShellItem item = null;
            try
            {
                item = WindowsSearch.Create(root, text, "Find: " + text);
                int hr = browser == null ? -1 : browser.BrowseToObject(item, 0);
                if (hr != 0) throw new InvalidOperationException("the results couldn't be shown (0x" + hr.ToString("X8") + ")");
            }
            catch (Exception ex)
            {
                ClearFind();
                Navigate(root);
                Pane.Main.Notice("⚠ Windows Search: " + ex.Message);
                return;
            }
            finally { if (item != null) Marshal.ReleaseComObject(item); }
            SetFindNames();
            Pane.TabNavigated(this);
        }

        // An Explorer view accepts an empty results list only before it has shown any folder, so Find starts
        // the tab with a fresh view.
        bool FillResults()
        {
            string root0 = FindRoot;
            DestroyBrowser();
            EnsureCreated(false);
            if (browser == null) { ClearFind(); EnsureCreated(); return false; }
            // Double-click and Enter in the results come to OnDefaultCommand (see below).
            IObjectWithSite site = browser as IObjectWithSite;
            if (site != null) site.SetSite(this);
            if (browser.FillFromObject(null, 0x200 /* EBF_NODROPTARGET */) == 0) { Resize(); WatchForResults(); return true; }
            // Back to the folder, rather than leaving an empty view.
            ClearFind();
            Navigate(root0);
            Pane.Main.Notice("⚠ Find couldn't show its results here");
            return false;
        }

        // The results list is on screen: connect to it and add what was found meanwhile.
        void ResultsShown()
        {
            if (!findPending) return;
            findPending = false;
            if (!FindInContents)
            {
                results = GetResultsFolder();
                if (results == null) Pane.Main.Notice("⚠ Find couldn't show its results here");
                else
                {
                    SetResultColumns();
                    if (waiting.Count > 0) { AddResults(waiting); waiting.Clear(); }
                }
            }
            SetFindNames();
            Pane.TabNavigated(this);
        }

        // In case the view doesn't report the results list as a navigation: look for it for up to 3 seconds.
        int findGeneration;   // each Find gets its own results watcher

        void WatchForResults()
        {
            Timer t = new Timer();
            int tries = 0, generation = ++findGeneration;
            t.Interval = 100;
            t.Tick += delegate
            {
                if (!findPending || FindInContents || generation != findGeneration) { t.Stop(); t.Dispose(); return; }
                if (++tries > 30)
                {
                    // The results list never appeared: back to the folder instead of a tab stuck in Find mode.
                    t.Stop();
                    t.Dispose();
                    string root = FindRoot;
                    LeaveResults(root);
                    Pane.Main.Notice("⚠ Find couldn't show its results here");
                    return;
                }
                IResultsFolder r = GetResultsFolder();
                if (r == null) return;
                Marshal.ReleaseComObject(r);
                t.Stop();
                t.Dispose();
                ResultsShown();
            };
            t.Start();
        }

        void SetFindNames()
        {
            Folder = FindRoot;
            Title = "Find: " + FindText;
            Address = "Find “" + FindText + "” in " + FindRoot + (FindInContents ? " (names and contents)" : "");
        }

        void Found(FileSearch s, List<string> batch)
        {
            if (s != Search) return;
            if (results == null) waiting.AddRange(batch);
            else AddResults(batch);
            if (Pane.ActiveTab == this) Pane.Main.UpdateStatus();
        }

        void AddResults(List<string> paths)
        {
            foreach (string path in paths)
            {
                IntPtr pidl = Native.ParsePath(path);
                if (pidl == IntPtr.Zero) continue;
                try { results.AddIDList(pidl, IntPtr.Zero); } catch { }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
        }

        static PROPERTYKEY Key(string fmtid, uint pid) { PROPERTYKEY k = new PROPERTYKEY(); k.fmtid = new Guid(fmtid); k.pid = pid; return k; }

        // Name, Folder path, Date modified, Type, Size: where each result is matters as much as its name.
        void SetResultColumns()
        {
            IColumnManager cm = View<IColumnManager>(new Guid("d8ec27bb-3f3b-4042-b10a-4acfd924d453"));
            if (cm == null) return;
            try
            {
                const string basic = "B725F130-47EF-101A-A5F1-02608C9EEBAC";
                PROPERTYKEY folder = Key("E3E0584C-B788-4A5A-BB20-7F5A44C9ACDD", 6); // System.ItemFolderPathDisplay
                PROPERTYKEY[] cols = { Key(basic, 10), folder, Key(basic, 14), Key(basic, 4), Key(basic, 12) };
                if (cm.SetColumns(cols, (uint)cols.Length) != 0) return;
                CM_COLUMNINFO ci = new CM_COLUMNINFO();
                ci.cbSize = (uint)Marshal.SizeOf(typeof(CM_COLUMNINFO));
                ci.dwMask = 0x1; // CM_MASK_WIDTH
                ci.uWidth = (uint)Native.Px(320);
                cm.SetColumnInfo(ref folder, ref ci);
            }
            catch { }
            finally { Marshal.ReleaseComObject(cm); }
        }

        IResultsFolder GetResultsFolder()
        {
            IFolderView v = View<IFolderView>(Native.IID_IFolderView);
            if (v == null) return null;
            try
            {
                Guid iid = typeof(IResultsFolder).GUID;
                IntPtr p;
                if (v.GetFolder(ref iid, out p) != 0 || p == IntPtr.Zero) return null;
                try { return Marshal.GetObjectForIUnknown(p) as IResultsFolder; }
                finally { Marshal.Release(p); }
            }
            finally { Marshal.ReleaseComObject(v); }
        }

        // Back to an ordinary folder view (of folder) in this tab.
        void LeaveResults(string folder)
        {
            ClearFind();
            DestroyBrowser();
            Folder = folder;
            if (Pane.ActiveTab == this) { EnsureCreated(); Resize(); Activate(); }
            Pane.TabNavigated(this);
        }

        public void StopFind()
        {
            if (Search != null) Search.Cancel = true;
        }

        void ClearFind()
        {
            StopFind();
            Search = null;
            FindText = FindRoot = null;
            findPending = false;
            waiting.Clear();
            if (results != null) { try { Marshal.ReleaseComObject(results); } catch { } results = null; }
        }

        public void Destroy()
        {
            ClearFind();
            DestroyBrowser();
        }

        // The results view asks for ICommDlgBrowser through its site; other services aren't offered.
        int IOleServiceProvider.QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject)
        {
            Guid commDlg = typeof(ICommDlgBrowser).GUID;   // SID_SExplorerBrowserFrame is this IID too
            ppvObject = IntPtr.Zero;
            if (guidService != commDlg || riid != commDlg) return unchecked((int)0x80004002); // E_NOINTERFACE
            ppvObject = Marshal.GetComInterfaceForObject(this, typeof(ICommDlgBrowser));
            return 0;
        }

        // Double-click or Enter in Find results: a results view can't open a folder in place (Windows would open
        // a separate File Explorer window), so a single selected folder opens in this tab instead. Files, and
        // several items, get the normal default action (S_FALSE).
        int ICommDlgBrowser.OnDefaultCommand(object view)
        {
            if (!IsFindResults || FindInContents) return 1;
            string path = SelectedPath();
            if (path == null || !Directory.Exists(path)) return 1;
            Pane.BeginInvoke((MethodInvoker)delegate { LeaveResults(path); });
            return 0;
        }

        int ICommDlgBrowser.OnStateChange(object view, uint change) { return 0; }
        int ICommDlgBrowser.IncludeObject(object view, IntPtr pidl) { return 0; }

        void DestroyBrowser()
        {
            if (browser == null) return;
            try
            {
                IObjectWithSite site = browser as IObjectWithSite;
                if (site != null) site.SetSite(null);
                if (cookie != 0) browser.Unadvise(cookie);
                browser.Destroy();
            }
            catch { }
            Marshal.ReleaseComObject(browser);
            browser = null;
            cookie = 0;
        }

        int IExplorerBrowserEvents.OnNavigationPending(IntPtr pidl)
        {
            // A view showing a results list can't navigate anywhere else, so opening a folder from the results
            // gives the tab a fresh view of that folder instead.
            if (IsFindResults && !findPending)
            {
                string to = Native.GetName(pidl, Native.SIGDN_DESKTOPABSOLUTEPARSING);
                if (to != null) { Pane.BeginInvoke((MethodInvoker)delegate { LeaveResults(to); }); return Native.HRESULT_CANCELLED; }
            }
            if (!Locked) return 0;
            string target = Native.GetName(pidl, Native.SIGDN_DESKTOPABSOLUTEPARSING);
            if (target == null || Util.SameFolder(target, LockedFolder)) return 0;
            // A locked tab never leaves its folder: open the destination in a new tab instead.
            Pane.BeginInvoke((MethodInvoker)delegate { Pane.AddTab(target, false, true); });
            return Native.HRESULT_CANCELLED;
        }

        int IExplorerBrowserEvents.OnViewCreated(object psv) { return 0; }

        int IExplorerBrowserEvents.OnNavigationComplete(IntPtr pidl)
        {
            // A results list (ours, or Windows Search's) has no file system path; the searched folder has one.
            if (IsFindResults && findPending && Native.GetName(pidl, Native.SIGDN_FILESYSPATH) == null)
            {
                Icon old = Icon;
                Icon = Native.SmallIcon(pidl);
                if (old != null) old.Dispose();
                ResultsShown();
                return 0;
            }
            // Left the results (opened a folder, Back, ...), or went to a folder before they appeared.
            if (IsFindResults && (!findPending || Native.GetName(pidl, Native.SIGDN_FILESYSPATH) != null)) ClearFind();
            ReadNames(pidl);
            Pane.TabNavigated(this);
            return 0;
        }

        int IExplorerBrowserEvents.OnNavigationFailed(IntPtr pidl)
        {
            string name = Native.GetName(pidl, Native.SIGDN_NORMALDISPLAY) ?? "the folder";
            Pane.BeginInvoke((MethodInvoker)delegate { Pane.Main.Notice("\u26A0 Couldn't open " + name); });
            return 0;
        }
    }
}
