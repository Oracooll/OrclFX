// Orcl File Explorer: Folder tree (INameSpaceTreeControl) that follows the active pane.
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
    public class TreeEventSink : INameSpaceTreeControlEvents
    {
        readonly TreePane owner;
        const int E_NOTIMPL = unchecked((int)0x80004001);
        internal TreeEventSink(TreePane o) { owner = o; }

        public int OnItemClick(IShellItem psi, uint hitTest, uint clickType) { return owner.ItemClick(psi, hitTest, clickType); }
        public int OnSelectionChanged(IShellItemArray sel) { return owner.SelectionChanged(sel); }
        public int OnKeyboardInput(uint uMsg, IntPtr wParam, IntPtr lParam) { return owner.KeyboardInput(); }
        public int OnPropertyItemCommit(IShellItem psi) { return E_NOTIMPL; }
        public int OnItemStateChanging(IShellItem psi, uint mask, uint state) { return E_NOTIMPL; }
        public int OnItemStateChanged(IShellItem psi, uint mask, uint state) { return E_NOTIMPL; }
        public int OnBeforeExpand(IShellItem psi) { return E_NOTIMPL; }
        public int OnAfterExpand(IShellItem psi) { return E_NOTIMPL; }
        public int OnBeginLabelEdit(IShellItem psi) { return E_NOTIMPL; }
        public int OnEndLabelEdit(IShellItem psi) { return E_NOTIMPL; }
        public int OnGetToolTip(IShellItem psi, IntPtr pszTip, int cchTip) { return E_NOTIMPL; }
        public int OnBeforeItemDelete(IShellItem psi) { return E_NOTIMPL; }
        public int OnItemAdded(IShellItem psi, int fIsRoot) { return E_NOTIMPL; }
        public int OnItemDeleted(IShellItem psi, int fIsRoot) { return E_NOTIMPL; }
        public int OnBeforeContextMenu(IShellItem psi, ref Guid riid, out IntPtr ppv) { ppv = IntPtr.Zero; return E_NOTIMPL; }
        public int OnAfterContextMenu(IShellItem psi, IntPtr pcmIn, ref Guid riid, out IntPtr ppv) { ppv = IntPtr.Zero; return E_NOTIMPL; }
        public int OnBeforeStateImageChange(IShellItem psi) { return E_NOTIMPL; }
        public int OnGetDefaultIconIndex(IShellItem psi, out int piDefaultIcon, out int piOpenIcon) { piDefaultIcon = piOpenIcon = 0; return E_NOTIMPL; }
    }

    public class TreePane : Panel
    {
        readonly MainForm main;
        readonly Label header = new Label();
        readonly Panel host = new Panel();
        INameSpaceTreeControl tree;
        uint cookie;
        bool syncing;
        string synced;
        TreeEventSink sink;
        int keyTick = Environment.TickCount - 100000;

        const uint NSTCIS_SELECTED = 1, NSTCIS_EXPANDED = 2;
        const int E_NOTIMPL = unchecked((int)0x80004001), S_FALSE = 1;

        internal TreePane(MainForm m)
        {
            main = m;
            Dock = DockStyle.Fill;
            header.Dock = DockStyle.Top;
            header.Height = Native.Px(24);
            header.Padding = new Padding(Native.Px(8), 0, 0, 0);
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.Text = "Folders";
            host.Dock = DockStyle.Fill;
            host.Resize += delegate { FitTree(); };
            Controls.Add(host);
            Controls.Add(header);
        }

        public void EnsureCreated()
        {
            if (tree != null) return;
            tree = (INameSpaceTreeControl)Activator.CreateInstance(Type.GetTypeFromCLSID(Native.CLSID_NamespaceTreeControl));
            RECT rc = HostRect();
            // HASEXPANDOS | ROOTHASEXPANDO | FULLROWSELECT | SHOWSELECTIONALWAYS | EVENHEIGHT | NOEDITLABELS | TABSTOP | AUTOHSCROLL
            // (no FADEINOUTEXPANDOS, so the expand/collapse arrows stay visible instead of only on hover)
            uint style = 0x1 | 0x40 | 0x8 | 0x80 | 0x400 | 0x10000 | 0x20000 | 0x100000;
            tree.Initialize(host.Handle, ref rc, style);
            sink = new TreeEventSink(this);
            tree.TreeAdvise(sink, out cookie);
            // Folders, plus hidden ones when File Explorer is set to show hidden items.
            object hidden = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 2);
            uint SHCONTF_FOLDERS = 0x20u | (hidden is int && (int)hidden == 1 ? 0x80u : 0u);
            const uint NSTCRS_VISIBLE = 0, NSTCRS_EXPANDED = 2;
            AddRoot("::{f874310e-b6b7-47dc-bc84-b9e6b38f5903}", SHCONTF_FOLDERS, NSTCRS_VISIBLE);       // Home
            if (!AddRoot("::{018D5C66-4533-4307-9B53-224DE2ED1FE6}", SHCONTF_FOLDERS, NSTCRS_VISIBLE))  // OneDrive
            {
                string od = Environment.GetEnvironmentVariable("OneDrive");
                if (!string.IsNullOrEmpty(od) && Directory.Exists(od)) AddRoot(od, SHCONTF_FOLDERS, NSTCRS_VISIBLE);
            }
            AddRoot(Native.ThisPC, SHCONTF_FOLDERS, NSTCRS_EXPANDED);
            AddRoot("::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", SHCONTF_FOLDERS, NSTCRS_VISIBLE);       // Network
            ApplyTheme();
            synced = null;
        }

        bool AddRoot(string path, uint enumFlags, uint style)
        {
            IShellItem item = Native.ItemFromPath(path);
            if (item == null) return false;
            try { return tree.AppendRoot(item, enumFlags, style, IntPtr.Zero) == 0; }
            finally { Marshal.ReleaseComObject(item); }
        }

        RECT HostRect()
        {
            RECT r = new RECT();
            r.right = host.ClientSize.Width;
            r.bottom = host.ClientSize.Height;
            return r;
        }

        void FitTree()
        {
            // The tree control's own window is the host's direct child.
            IntPtr top = Native.GetWindow(host.Handle, 5 /* GW_CHILD */);
            if (top != IntPtr.Zero) Native.MoveWindow(top, 0, 0, host.ClientSize.Width, host.ClientSize.Height, true);
        }

        public void ApplyTheme()
        {
            header.BackColor = Theme.Bar;
            header.ForeColor = Theme.TextDim;
            host.BackColor = Theme.Window;
            if (tree == null) return;
            tree.SetTheme(Theme.Dark ? "DarkMode_Explorer" : "Explorer");
            IntPtr tv = Native.FindChild(host.Handle, "SysTreeView32");
            if (tv != IntPtr.Zero)
            {
                const int TVM_SETBKCOLOR = 0x111D, TVM_SETTEXTCOLOR = 0x111E;
                Native.SendMessage(tv, TVM_SETBKCOLOR, IntPtr.Zero, (IntPtr)Native.ColorRef(Theme.Window));
                Native.SendMessage(tv, TVM_SETTEXTCOLOR, IntPtr.Zero, (IntPtr)Native.ColorRef(Theme.Text));
                Native.SetWindowTheme(tv, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
            }
        }

        // Expand the tree down to the folder and select it, without navigating anything.
        public void SyncTo(string folder)
        {
            if (tree == null || folder == null || Util.SameFolder(folder, synced)) return;
            IShellItem item = Native.ItemFromPath(folder);
            if (item == null) return;
            synced = folder;
            syncing = true;
            try
            {
                List<IShellItem> chain = new List<IShellItem>();
                IShellItem p = item, parent;
                while (chain.Count < 64 && p.GetParent(out parent) == 0 && parent != null) { chain.Add(parent); p = parent; }
                for (int i = chain.Count - 1; i >= 0; i--) tree.SetItemState(chain[i], NSTCIS_EXPANDED, NSTCIS_EXPANDED);
                tree.SetItemState(item, NSTCIS_SELECTED, NSTCIS_SELECTED);
                tree.EnsureItemVisible(item);
                foreach (IShellItem x in chain) try { Marshal.ReleaseComObject(x); } catch { }
            }
            catch { }
            finally
            {
                syncing = false;
                try { Marshal.ReleaseComObject(item); } catch { }
            }
        }

        public void Destroy()
        {
            if (tree == null) return;
            try { if (cookie != 0) tree.TreeUnadvise(cookie); } catch { }
            IntPtr w = host.IsHandleCreated ? Native.GetWindow(host.Handle, 5) : IntPtr.Zero;
            Marshal.ReleaseComObject(tree);
            tree = null;
            cookie = 0;
            synced = null;
            if (w != IntPtr.Zero) Native.DestroyWindow(w);
        }

        static string PathOf(IShellItem item) { return Native.ItemName(item, Native.SIGDN_DESKTOPABSOLUTEPARSING); }

        internal int ItemClick(IShellItem psi, uint hitTest, uint clickType)
        {
            const uint ONITEMBUTTON = 0x10, ONITEMBODY = 0x2 | 0x4 | 0x20; // icon, label, rest of the row
            string path = PathOf(psi);
            if (path == null || (hitTest & ONITEMBUTTON) != 0 || (hitTest & ONITEMBODY) == 0) return S_FALSE;
            int button = (int)(clickType & 3);
            // Middle-click opens the folder in a new tab of the active pane; left-click opens it in the active tab.
            if (button == 2) { main.BeginInvoke((MethodInvoker)delegate { main.OpenFolder(path, 1); }); return 0; }
            if (button == 1) { synced = path; main.BeginInvoke((MethodInvoker)delegate { main.OpenFolder(path, 0, false); }); }
            return S_FALSE;
        }

        internal int SelectionChanged(IShellItemArray sel)
        {
            // Only follow selection changes made with the keyboard; clicks are handled in OnItemClick.
            if (syncing || sel == null || unchecked(Environment.TickCount - keyTick) > 700) return 0;
            uint n;
            IShellItem item;
            if (sel.GetCount(out n) != 0 || n == 0 || sel.GetItemAt(0, out item) != 0) return 0;
            string path = PathOf(item);
            if (path == null) return 0;
            synced = path;
            main.BeginInvoke((MethodInvoker)delegate { main.OpenFolder(path, 0, false); });
            return 0;
        }

        internal int KeyboardInput() { keyTick = Environment.TickCount; return S_FALSE; }
    }
}
