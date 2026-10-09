// OrclFX: P/Invoke declarations and thin wrappers around Windows and Shell APIs.
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
    static class Native
    {
        public const uint SBSP_ABSOLUTE = 0x0, SBSP_PARENT = 0x2000, SBSP_NAVIGATEBACK = 0x4000, SBSP_NAVIGATEFORWARD = 0x8000;
        public const uint EBO_SHOWFRAMES = 0x2, EBO_NOBORDER = 0x40;
        public const uint SIGDN_NORMALDISPLAY = 0, SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000, SIGDN_FILESYSPATH = 0x80058000;
        public const uint SVGIO_SELECTION = 1, SVGIO_ALLVIEW = 2;
        public const int HRESULT_CANCELLED = unchecked((int)0x800704C7);
        public const int WH_MOUSE = 7, WM_LBUTTONDBLCLK = 0x203;
        public const string ThisPC = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

        public static readonly Guid IID_IFolderView = new Guid("cde725b0-ccc9-4519-917e-325d72fab4ce");
        public static readonly Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        public static readonly Guid IID_IShellItemArray = new Guid("b63ea76d-1f85-456f-a19c-48159efa858b");
        public static readonly Guid CLSID_NamespaceTreeControl = new Guid("AE054212-3535-4430-83ED-D501AA6680E6");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int AssocQueryString(uint flags, uint str, string assoc, string extra, StringBuilder outBuf, ref uint outLen);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attrs, bool create, IntPtr template, out System.Runtime.InteropServices.ComTypes.IStream stream);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hgt, bool repaint);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        // For windows of other processes (a preview handler's): doesn't wait for them, so a hung one can't block us.
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public static IShellItem ItemFromPath(string path)
        {
            Guid iid = IID_IShellItem;
            IShellItem item;
            return SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item) == 0 ? item : null;
        }

        public static string ItemName(IShellItem item, uint sigdn)
        {
            IntPtr p;
            if (item == null || item.GetDisplayName(sigdn, out p) != 0 || p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
        }

        // The CLSID of the preview handler registered for a file extension, or null.
        public static string PreviewHandlerFor(string ext)
        {
            StringBuilder sb = new StringBuilder(64);
            uint n = (uint)sb.Capacity;
            const uint ASSOCF_INIT_DEFAULTTOPROGID = 0x4, ASSOCSTR_SHELLEXTENSION = 16;
            return AssocQueryString(ASSOCF_INIT_DEFAULTTOPROGID, ASSOCSTR_SHELLEXTENSION, ext, "{8895b1c6-b41f-4c1c-a562-0d564250836f}", sb, ref n) == 0
                ? sb.ToString() : null;
        }

        [DllImport("ole32.dll")]
        static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);

        public static object CreateComObject(Guid clsid, uint context)
        {
            Guid unknown = new Guid("00000000-0000-0000-C000-000000000046");
            object o;
            try { return CoCreateInstance(ref clsid, IntPtr.Zero, context, ref unknown, out o) == 0 ? o : null; }
            catch { return null; }
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int StrCmpLogicalW(string a, string b);
        public static int CompareNatural(string a, string b) { return StrCmpLogicalW(a ?? "", b ?? ""); }

        public static uint ColorRef(Color c) { return (uint)(c.R | (c.G << 8) | (c.B << 16)); }

        // ---- "Show hidden files" (the per-user shell setting File Explorer also uses)

        [DllImport("shell32.dll")] static extern void SHGetSetSettings(IntPtr lpss, uint dwMask, bool bSet);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);

        public static bool GetShowHidden()
        {
            IntPtr p = Marshal.AllocHGlobal(64);
            try
            {
                for (int i = 0; i < 64; i += 4) Marshal.WriteInt32(p, i, 0);
                SHGetSetSettings(p, 0x1 /* SSF_SHOWALLOBJECTS */, false);
                return (Marshal.ReadInt32(p) & 1) != 0;
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        // "Show file name extensions" (SSF_SHOWEXTENSIONS; File Explorer's setting too).
        public static bool GetShowExtensions()
        {
            IntPtr p = Marshal.AllocHGlobal(64);
            try
            {
                for (int i = 0; i < 64; i += 4) Marshal.WriteInt32(p, i, 0);
                SHGetSetSettings(p, 0x2, false);
                return (Marshal.ReadInt32(p) & 2) != 0;
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        public static void SetShowExtensions(bool on)
        {
            IntPtr p = Marshal.AllocHGlobal(64);
            try
            {
                for (int i = 0; i < 64; i += 4) Marshal.WriteInt32(p, i, 0);
                Marshal.WriteInt32(p, on ? 2 : 0);
                SHGetSetSettings(p, 0x2, true);
            }
            finally { Marshal.FreeHGlobal(p); }
            IntPtr r;
            SendMessageTimeout((IntPtr)0xFFFF, 0x1A, IntPtr.Zero, "ShellState", 0x2 /* SMTO_ABORTIFHUNG */, 1000, out r);
        }

        [DllImport("shell32.dll")] public static extern IntPtr ILFindLastID(IntPtr pidl);

        [DllImport("oleacc.dll")]
        static extern int AccessibleObjectFromPoint(POINT pt, [MarshalAs(UnmanagedType.IDispatch)] out object acc, out object child);

        public const int ROLE_SYSTEM_LIST = 0x21;
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);

        // The accessibility role of what is at a screen point (an Explorer view says "list" for its empty space, and
        // "split button" for a column header), or 0 when it can't be told.
        public static int AccessibleRoleAt(POINT pt)
        {
            object acc, child;
            try
            {
                if (AccessibleObjectFromPoint(pt, out acc, out child) != 0 || acc == null) return 0;
                object role = acc.GetType().InvokeMember("accRole", System.Reflection.BindingFlags.GetProperty, null, acc, new object[] { child });
                Marshal.ReleaseComObject(acc);
                return role is int ? (int)role : 0;
            }
            catch { return 0; }
        }

        public static void SetShowHidden(bool on)
        {
            IntPtr p = Marshal.AllocHGlobal(64);
            try
            {
                for (int i = 0; i < 64; i += 4) Marshal.WriteInt32(p, i, 0);
                Marshal.WriteInt32(p, on ? 1 : 0);
                SHGetSetSettings(p, 0x1, true);
            }
            finally { Marshal.FreeHGlobal(p); }
            IntPtr r;
            SendMessageTimeout((IntPtr)0xFFFF, 0x1A, IntPtr.Zero, "ShellState", 0x2 /* SMTO_ABORTIFHUNG */, 1000, out r);
        }

        // ---- Natural ("2 before 10") sorting: on unless the NoStrCmpLogical policy is set

        const string ExplorerPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        public static bool NaturalSortForcedByAdmin()
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(ExplorerPolicies))
                return k != null && k.GetValue("NoStrCmpLogical") is int;
        }

        public static bool GetNaturalSort()
        {
            foreach (RegistryKey root in new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser })
                using (RegistryKey k = root.OpenSubKey(ExplorerPolicies))
                {
                    object v = k == null ? null : k.GetValue("NoStrCmpLogical");
                    if (v is int) return (int)v == 0;
                }
            return true;
        }

        public static void SetNaturalSort(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ExplorerPolicies))
            {
                if (on) k.DeleteValue("NoStrCmpLogical", false);
                else k.SetValue("NoStrCmpLogical", 1, RegistryValueKind.DWord);
            }
            IntPtr r;
            SendMessageTimeout((IntPtr)0xFFFF, 0x1A, IntPtr.Zero, "Policy", 0x2, 1000, out r);
        }

        // ---- Fast directory enumeration for folder sizes

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public uint ftCreationLow, ftCreationHigh, ftAccessLow, ftAccessHigh, ftWriteLow, ftWriteHigh;
            public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileEx(string name, int infoLevel, out WIN32_FIND_DATA data, int searchOp, IntPtr filter, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFile(IntPtr h, out WIN32_FIND_DATA data);
        [DllImport("kernel32.dll")] public static extern bool FindClose(IntPtr h);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] public static extern bool SetThreadPriority(IntPtr h, int priority);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFinalPathNameByHandle(IntPtr file, StringBuilder path, uint size, uint flags);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern uint GetDriveType(string root);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool DeleteFile(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern uint GetFileAttributes(string name);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        // Renames through the shell, like File Explorer does (so Explorer's Undo can reverse it).
        public static bool ShellRename(IntPtr owner, string from, string to)
        {
            SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
            op.hwnd = owner;
            op.wFunc = 0x4;                 // FO_RENAME
            op.pFrom = from + "\0";        // double-null terminated
            op.pTo = to + "\0";
            op.fFlags = 0x40;               // FOF_ALLOWUNDO
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }

        // Moves a file to the Recycle Bin without asking, but with Windows' warning when it would be deleted for
        // good instead (a network drive, a full Recycle Bin).
        public static bool ShellRecycle(IntPtr owner, string path)
        {
            SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
            op.hwnd = owner;
            op.wFunc = 0x3;                 // FO_DELETE
            op.pFrom = path + "\0";
            op.fFlags = 0x40 | 0x10 | 0x4000; // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }

        public static readonly Guid CLSID_ExplorerBrowser = new Guid("71f96385-ddd6-48d3-a0c1-ae06e8b055fb");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdn, out IntPtr name);
        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attr, ref SHFILEINFO psfi, uint cb, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] public static extern IntPtr GetFocus();
        [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll")] static extern short GetKeyState(int vk);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetDiskFreeSpaceEx(string dir, out ulong freeToCaller, out ulong total, out ulong totalFree);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] public static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] public static extern void FlushMenuThemes();
        [DllImport("uxtheme.dll", EntryPoint = "#104")] public static extern void RefreshImmersiveColorPolicyState();
        [DllImport("uxtheme.dll", EntryPoint = "#133")] public static extern bool AllowDarkModeForWindow(IntPtr h, bool allow);

        static float scale = 0;
        public static int Px(int v)
        {
            if (scale == 0) using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            return (int)Math.Round(v * scale);
        }

        public static IntPtr ParsePath(string path)
        {
            IntPtr pidl; uint o;
            if (string.IsNullOrEmpty(path)) return IntPtr.Zero;
            return SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out o) == 0 ? pidl : IntPtr.Zero;
        }

        public static string GetName(IntPtr pidl, uint sigdn)
        {
            IntPtr p;
            if (SHGetNameFromIDList(pidl, sigdn, out p) != 0 || p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
        }

        public static Icon SmallIcon(IntPtr pidl)
        {
            SHFILEINFO fi = new SHFILEINFO();
            SHGetFileInfo(pidl, 0, ref fi, (uint)Marshal.SizeOf(fi), 0x100 | 0x1 | 0x8); // ICON | SMALLICON | PIDL
            if (fi.hIcon == IntPtr.Zero) return null;
            Icon icon = (Icon)Icon.FromHandle(fi.hIcon).Clone();
            DestroyIcon(fi.hIcon);
            return icon;
        }

        public static string ClassName(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc fn, IntPtr l);

        public static IntPtr FindChild(IntPtr parent, string cls)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(parent, delegate(IntPtr h, IntPtr l)
            {
                if (ClassName(h) != cls) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        public static bool KeyDown(int vk) { return (GetKeyState(vk) & 0x8000) != 0; }
        public static bool Contains(IntPtr parent, IntPtr h) { return parent != IntPtr.Zero && h != IntPtr.Zero && (h == parent || IsChild(parent, h)); }
    }
}
