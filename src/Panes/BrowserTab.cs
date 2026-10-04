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
    public class BrowserTab : IExplorerBrowserEvents
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

        public void EnsureCreated()
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
            if (browser == null) { Folder = path; return true; }
            IntPtr pidl = Native.ParsePath(path);
            if (pidl == IntPtr.Zero) return false;
            int hr;
            try { hr = browser.BrowseToIDList(pidl, Native.SBSP_ABSOLUTE); } finally { Marshal.FreeCoTaskMem(pidl); }
            // A locked tab cancels the navigation on purpose and opens a new tab instead: that counts as success.
            return hr == 0 || hr == Native.HRESULT_CANCELLED;
        }

        public void Nav(uint flags) { if (browser != null) browser.BrowseToIDList(IntPtr.Zero, flags); }
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

        public void Destroy()
        {
            if (browser == null) return;
            try
            {
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
