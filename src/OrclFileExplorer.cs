// Orcl File Explorer (orclfx.exe): a light multi-pane file manager that hosts the real Windows Explorer view.
// Built with the C# 5 compiler that ships with .NET Framework 4.x (see build.ps1).
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

[assembly: AssemblyTitle("Orcl File Explorer")]
[assembly: AssemblyProduct("Orcl File Explorer")]
[assembly: AssemblyDescription("Dual-pane file manager")]
// Version shown as major.minor.build with three-digit build (1.1.001). Bump the build number for every
// release; the minor number only changes when the owner says so.
[assembly: AssemblyVersion("1.1.5.0")]
[assembly: AssemblyFileVersion("1.1.5.0")]

namespace OrclFileExplorer
{
    static class Program
    {
        public const string AppName = "Orcl File Explorer";
        public static bool Portable;

        public static void LogError(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MainForm.StateFile));
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(MainForm.StateFile), "errors.log"),
                    DateTime.Now.ToString("s") + "  " + Installer.Version + "\r\n" + ex + "\r\n\r\n");
            }
            catch { }
        }

        // Diagnostics: set DUALPANE_TRACE to a file path to log startup, restarts and exits.
        public static void Trace(string s)
        {
            string f = Environment.GetEnvironmentVariable("DUALPANE_TRACE");
            if (f == null) return;
            try { File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.fff") + " [" + Process.GetCurrentProcess().Id + "] " + s + "\r\n"); } catch { }
        }
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // An unexpected error shows a message instead of closing the app (and losing unsaved state).
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                LogError(e.Exception);
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Trace("start: " + string.Join(" ", args));
            bool portable = false, restarted = false;
            foreach (string a in args)
            {
                if (a == "--restart") restarted = true;
                if (a == "--uninstall") { Installer.Uninstall(); return; }
                if (a == "--install") { Installer.InstallQuietly(); return; }
                if (a == "--portable") portable = true;
            }
            Portable = portable;
            if (!restarted && !portable && !Installer.IsRunningInstalledCopy() && !Installer.OfferInstall()) return;
            // One window per user: a second launch brings the running one to the front instead,
            // so two windows never overwrite each other's saved tabs.
            bool first;
            using (System.Threading.Mutex single = new System.Threading.Mutex(true, "OrclFx.Instance." + Native.PathKey(MainForm.StateFile), out first))
            {
                // After a restart (theme change) the previous window may still be closing: wait for it.
                if (!first && restarted) { try { first = single.WaitOne(15000); } catch (System.Threading.AbandonedMutexException) { first = true; } }
                // Another window already uses this settings file (installed or portable): bring it forward instead,
                // so two windows never overwrite each other's tabs and shortcuts.
                if (!first) { Trace("another copy is running: exit"); Installer.ActivateRunningCopy(); return; }
                Application.Run(new MainForm());
                Trace("exit");
            }
        }
    }

    // ------------------------------------------------------------------ Win32 / Shell interop

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct FOLDERSETTINGS { public int ViewMode; public uint fFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam; public IntPtr lParam; public int time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEHOOKSTRUCT { public POINT pt; public IntPtr hwnd; public uint wHitTestCode; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [ComImport, Guid("dfd3b6b5-c10c-4be9-85f6-a66969f402f6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExplorerBrowser
    {
        [PreserveSig] int Initialize(IntPtr hwndParent, ref RECT prc, ref FOLDERSETTINGS pfs);
        [PreserveSig] int Destroy();
        [PreserveSig] int SetRect(ref IntPtr phdwp, RECT rcBrowser);
        [PreserveSig] int SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string pszPropertyBag);
        [PreserveSig] int SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string pszEmptyText);
        [PreserveSig] int SetFolderSettings(ref FOLDERSETTINGS pfs);
        [PreserveSig] int Advise(IExplorerBrowserEvents psbe, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOptions(uint dwFlag);
        [PreserveSig] int GetOptions(out uint pdwFlag);
        [PreserveSig] int BrowseToIDList(IntPtr pidl, uint uFlags);
        [PreserveSig] int BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object punk, uint uFlags);
        [PreserveSig] int FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, int dwFlags);
        [PreserveSig] int RemoveAll();
        [PreserveSig] int GetCurrentView(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport, Guid("361bbdc1-e4ff-4dab-b0f5-8cb73df5b2b6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExplorerBrowserEvents
    {
        [PreserveSig] int OnNavigationPending(IntPtr pidlFolder);
        [PreserveSig] int OnViewCreated([MarshalAs(UnmanagedType.IUnknown)] object psv);
        [PreserveSig] int OnNavigationComplete(IntPtr pidlFolder);
        [PreserveSig] int OnNavigationFailed(IntPtr pidlFolder);
    }

    [ComImport, Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
        [PreserveSig] int SetCurrentViewMode(uint ViewMode);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
        [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
        [PreserveSig] int Items(uint uFlags, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int piItem);
        [PreserveSig] int GetFocusedItem(out int piItem);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT ppt);
        [PreserveSig] int GetSpacing(IntPtr ppt);
        [PreserveSig] int GetDefaultSpacing(out POINT ppt);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int iItem, uint dwFlags);
        [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [ComImport, Guid("1af3a467-214f-4298-908e-06b03e0b39f9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFolderView2
    {
        // IFolderView
        [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
        [PreserveSig] int SetCurrentViewMode(uint ViewMode);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
        [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
        [PreserveSig] int Items(uint uFlags, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int piItem);
        [PreserveSig] int GetFocusedItem(out int piItem);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT ppt);
        [PreserveSig] int GetSpacing(IntPtr ppt);
        [PreserveSig] int GetDefaultSpacing(out POINT ppt);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int iItem, uint dwFlags);
        [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
        // IFolderView2
        [PreserveSig] int SetGroupBy(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.Bool)] bool fAscending);
        [PreserveSig] int GetGroupBy(out PROPERTYKEY pkey, [MarshalAs(UnmanagedType.Bool)] out bool pfAscending);
        [PreserveSig] int SetViewProperty(IntPtr pidl, ref PROPERTYKEY propkey, IntPtr propvar);
        [PreserveSig] int GetViewProperty(IntPtr pidl, ref PROPERTYKEY propkey, IntPtr ppropvar);
        [PreserveSig] int SetTileViewProperties(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszPropList);
        [PreserveSig] int SetExtendedTileViewProperties(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszPropList);
        [PreserveSig] int SetText(int iType, [MarshalAs(UnmanagedType.LPWStr)] string pwszText);
        [PreserveSig] int SetCurrentFolderFlags(uint dwMask, uint dwFlags);
        [PreserveSig] int GetCurrentFolderFlags(out uint pdwFlags);
        [PreserveSig] int GetSortColumnCount(out int pcColumns);
        [PreserveSig] int SetSortColumns(IntPtr rgSortColumns, int cColumns);
        [PreserveSig] int GetSortColumns(IntPtr rgSortColumns, int cColumns);
        [PreserveSig] int GetItem(int iItem, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int GetVisibleItem(int iStart, [MarshalAs(UnmanagedType.Bool)] bool fPrevious, out int piItem);
        [PreserveSig] int GetSelectedItem(int iStart, out int piItem);
        [PreserveSig] int GetSelection([MarshalAs(UnmanagedType.Bool)] bool fNoneImpliesFolder, out IShellItemArray ppsia);
        [PreserveSig] int GetSelectionState(IntPtr pidl, out uint pdwFlags);
        [PreserveSig] int InvokeVerbOnSelection([MarshalAs(UnmanagedType.LPStr)] string pszVerb);
        [PreserveSig] int SetViewModeAndIconSize(int uViewMode, int iImageSize);
        [PreserveSig] int GetViewModeAndIconSize(out int puViewMode, out int piImageSize);
        [PreserveSig] int SetGroupSubsetCount(uint cVisibleRows);
        [PreserveSig] int GetGroupSubsetCount(out uint pcVisibleRows);
        [PreserveSig] int SetRedraw([MarshalAs(UnmanagedType.Bool)] bool fRedrawOn);
        [PreserveSig] int IsMoveInSameFolder();
        [PreserveSig] int DoRename();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CM_COLUMNINFO
    {
        public uint cbSize, dwMask, dwState, uWidth, uDefaultWidth, uIdealWidth;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string wszName;
    }

    [ComImport, Guid("d8ec27bb-3f3b-4042-b10a-4acfd924d453"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IColumnManager
    {
        [PreserveSig] int SetColumnInfo(ref PROPERTYKEY propkey, ref CM_COLUMNINFO pcmci);
        [PreserveSig] int GetColumnInfo(ref PROPERTYKEY propkey, ref CM_COLUMNINFO pcmci);
        [PreserveSig] int GetColumnCount(uint dwFlags, out uint puCount);
        [PreserveSig] int GetColumns(uint dwFlags, [Out, MarshalAs(UnmanagedType.LPArray)] PROPERTYKEY[] rgkeyOrder, uint cColumns);
        [PreserveSig] int SetColumns([In, MarshalAs(UnmanagedType.LPArray)] PROPERTYKEY[] rgkeyOrder, uint cVisible);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellView
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
        [PreserveSig] int TranslateAccelerator(ref MSG pmsg);
        [PreserveSig] int EnableModeless([MarshalAs(UnmanagedType.Bool)] bool fEnable);
        [PreserveSig] int UIActivate(uint uState);
        [PreserveSig] int Refresh();
    }

    [ComImport, Guid("e693cf68-d967-4112-8763-99172aee5e5a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IVisualProperties
    {
        [PreserveSig] int SetWatermark(IntPtr hbmp, int vpwf);
        [PreserveSig] int SetColor(int vpcf, uint cr);
        [PreserveSig] int GetColor(int vpcf, out uint pcr);
        [PreserveSig] int SetItemHeight(int cyItemInPixels);
        [PreserveSig] int GetItemHeight(out int cyItemInPixels);
        [PreserveSig] int SetFont(IntPtr plf, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);
        [PreserveSig] int GetFont(IntPtr plf);
        [PreserveSig] int SetTheme([MarshalAs(UnmanagedType.LPWStr)] string pszSubAppName, [MarshalAs(UnmanagedType.LPWStr)] string pszSubIdList);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
        [PreserveSig] int GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int GetCount(out uint pdwNumItems);
        [PreserveSig] int GetItemAt(uint dwIndex, out IShellItem ppsi);
        [PreserveSig] int EnumItems(out IntPtr ppenumShellItems);
    }

    [ComImport, Guid("028212A3-B627-47e9-8856-C14265554E4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INameSpaceTreeControl
    {
        [PreserveSig] int Initialize(IntPtr hwndParent, ref RECT prc, uint nsctsFlags);
        [PreserveSig] int TreeAdvise([MarshalAs(UnmanagedType.IUnknown)] object punk, out uint pdwCookie);
        [PreserveSig] int TreeUnadvise(uint dwCookie);
        [PreserveSig] int AppendRoot(IShellItem psiRoot, uint grfEnumFlags, uint grfRootStyle, IntPtr pif);
        [PreserveSig] int InsertRoot(int iIndex, IShellItem psiRoot, uint grfEnumFlags, uint grfRootStyle, IntPtr pif);
        [PreserveSig] int RemoveRoot(IShellItem psiRoot);
        [PreserveSig] int RemoveAllRoots();
        [PreserveSig] int GetRootItems(out IShellItemArray ppsiaRootItems);
        [PreserveSig] int SetItemState(IShellItem psi, uint nstcisMask, uint nstcisFlags);
        [PreserveSig] int GetItemState(IShellItem psi, uint nstcisMask, out uint pnstcisFlags);
        [PreserveSig] int GetSelectedItems(out IShellItemArray psiaItems);
        [PreserveSig] int GetItemCustomState(IShellItem psi, out int piStateNumber);
        [PreserveSig] int SetItemCustomState(IShellItem psi, int iStateNumber);
        [PreserveSig] int EnsureItemVisible(IShellItem psi);
        [PreserveSig] int SetTheme([MarshalAs(UnmanagedType.LPWStr)] string pszTheme);
        [PreserveSig] int GetNextItem(IShellItem psi, int nstcgi, out IShellItem ppsiNext);
        [PreserveSig] int HitTest(ref POINT ppt, out IShellItem ppsiOut);
        [PreserveSig] int GetItemRect(IShellItem psi, out RECT prect);
        [PreserveSig] int CollapseAll();
    }

    [ComImport, Guid("93D77985-B3D8-4484-8318-672CDDA002CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INameSpaceTreeControlEvents
    {
        [PreserveSig] int OnItemClick(IShellItem psi, uint hitTest, uint clickType);
        [PreserveSig] int OnPropertyItemCommit(IShellItem psi);
        [PreserveSig] int OnItemStateChanging(IShellItem psi, uint mask, uint state);
        [PreserveSig] int OnItemStateChanged(IShellItem psi, uint mask, uint state);
        [PreserveSig] int OnSelectionChanged(IShellItemArray psiaSelection);
        [PreserveSig] int OnKeyboardInput(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int OnBeforeExpand(IShellItem psi);
        [PreserveSig] int OnAfterExpand(IShellItem psi);
        [PreserveSig] int OnBeginLabelEdit(IShellItem psi);
        [PreserveSig] int OnEndLabelEdit(IShellItem psi);
        [PreserveSig] int OnGetToolTip(IShellItem psi, IntPtr pszTip, int cchTip);
        [PreserveSig] int OnBeforeItemDelete(IShellItem psi);
        [PreserveSig] int OnItemAdded(IShellItem psi, int fIsRoot);
        [PreserveSig] int OnItemDeleted(IShellItem psi, int fIsRoot);
        [PreserveSig] int OnBeforeContextMenu(IShellItem psi, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int OnAfterContextMenu(IShellItem psi, IntPtr pcmIn, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int OnBeforeStateImageChange(IShellItem psi);
        [PreserveSig] int OnGetDefaultIconIndex(IShellItem psi, out int piDefaultIcon, out int piOpenIcon);
    }

    [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPreviewHandler
    {
        [PreserveSig] int SetWindow(IntPtr hwnd, ref RECT prc);
        [PreserveSig] int SetRect(ref RECT prc);
        [PreserveSig] int DoPreview();
        [PreserveSig] int Unload();
        [PreserveSig] int SetFocus();
        [PreserveSig] int QueryFocus(out IntPtr phwnd);
        [PreserveSig] int TranslateAccelerator(ref MSG pmsg);
    }

    [ComImport, Guid("196bf9a5-b346-4ef0-aa1e-5dcdb76768b1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPreviewHandlerVisuals
    {
        [PreserveSig] int SetBackgroundColor(uint color);
        [PreserveSig] int SetFont(IntPtr plf);
        [PreserveSig] int SetTextColor(uint color);
    }

    [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithFile
    {
        [PreserveSig] int Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
    }

    [ComImport, Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithItem
    {
        [PreserveSig] int Initialize(IShellItem psi, uint grfMode);
    }

    [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStream
    {
        [PreserveSig] int Initialize(System.Runtime.InteropServices.ComTypes.IStream pstream, uint grfMode);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, uint flags, out IntPtr phbm);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleWindow
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
    }

    [ComImport, Guid("68284faa-6a48-11d0-8c78-00c04fd918b4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInputObject
    {
        [PreserveSig] int UIActivateIO(int fActivate, IntPtr pMsg);
        [PreserveSig] int HasFocusIO();
        [PreserveSig] int TranslateAcceleratorIO(ref MSG pMsg);
    }

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

        // If path is oldRoot or inside it, returns the same path under newRoot; otherwise null.
        public static string Rebase(string path, string oldRoot, string newRoot)
        {
            if (path == null) return null;
            oldRoot = oldRoot.TrimEnd('\\');
            if (path.TrimEnd('\\').Equals(oldRoot, StringComparison.OrdinalIgnoreCase)) return newRoot;
            if (path.StartsWith(oldRoot + "\\", StringComparison.OrdinalIgnoreCase)) return newRoot.TrimEnd('\\') + path.Substring(oldRoot.Length);
            return null;
        }

        // Writes to a temporary file first and then swaps it in, so a crash never leaves a half-written file.
        public static void WriteAllTextAtomic(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            // A unique temp name, and one writer at a time per file across all processes.
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (System.Threading.Mutex m = new System.Threading.Mutex(false, "OrclFx.Write." + PathKey(path)))
            {
                bool owned = false;
                try { owned = m.WaitOne(5000); } catch (System.Threading.AbandonedMutexException) { owned = true; }
                if (!owned) throw new IOException("another program is writing " + Path.GetFileName(path));
                try
                {
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", true);
                    else File.Move(tmp, path);
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    m.ReleaseMutex();
                }
            }
        }

        // A short, stable key for a file path, usable in a mutex name.
        public static string PathKey(string path)
        {
            string p = Path.GetFullPath(path).ToLowerInvariant();
            using (System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(p));
                return BitConverter.ToString(h, 0, 10).Replace("-", "");
            }
        }

        public static string FormatBytes(long b)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = b;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return u == 0 ? b + " B" : v.ToString(v >= 100 ? "0" : "0.0") + " " + units[u];
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
        public static bool SameFolder(string a, string b)
        {
            return a != null && b != null && string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------ Theme

    static class Theme
    {
        public static int Mode;          // 0 = match Windows, 1 = light, 2 = dark
        public static bool Dark;
        public static Color Window, Bar, TabHover, Text, TextDim, Border, Hover, Input, Menu, Accent, Lock;
        public static readonly Font IconFont = MakeIconFont();

        static Font MakeIconFont()
        {
            Font f = new Font("Segoe Fluent Icons", 10f);
            if (f.Name == "Segoe Fluent Icons") return f;
            return new Font("Segoe MDL2 Assets", 10f);
        }

        static Color C(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        public static bool WindowsPrefersDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return v is int && (int)v == 0;
            }
            catch { return false; }
        }

        static Color ReadAccent()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                if (v is int) { int c = (int)v; return Color.FromArgb(c & 255, (c >> 8) & 255, (c >> 16) & 255); }
            }
            catch { }
            return C(0x0078d4);
        }

        public static void Update()
        {
            Dark = Mode == 2 || (Mode == 0 && WindowsPrefersDark());
            Accent = ReadAccent();
            if (Dark)
            {
                Window = C(0x191919); Bar = C(0x202020); TabHover = C(0x2d2d2d); Text = C(0xffffff); TextDim = C(0xa0a0a0);
                Border = C(0x3a3a3a); Hover = C(0x383838); Input = C(0x2b2b2b); Menu = C(0x2b2b2b); Lock = C(0xf0c050);
            }
            else
            {
                Window = C(0xffffff); Bar = C(0xf0f0f0); TabHover = C(0xe2e2e2); Text = C(0x1a1a1a); TextDim = C(0x606060);
                Border = C(0xd4d4d4); Hover = C(0xdedede); Input = C(0xffffff); Menu = C(0xf9f9f9); Lock = C(0xb07d00);
            }
            // Tell Windows which mode this app wants and drop its cached decision, so Explorer views
            // created after a live switch pick up the new mode (not just the ones created at startup).
            try { Native.SetPreferredAppMode(Dark ? 2 : 3); Native.RefreshImmersiveColorPolicyState(); Native.FlushMenuThemes(); } catch { }
        }
    }

    class MenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.Hover; } }
        public override Color MenuItemBorder { get { return Theme.Hover; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Menu; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Menu; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Menu; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Menu; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Menu; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Hover; } }
        public override Color CheckBackground { get { return Theme.Menu; } }
        public override Color CheckSelectedBackground { get { return Theme.Hover; } }
        public override Color CheckPressedBackground { get { return Theme.Hover; } }
    }

    class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDim;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, "", Theme.IconFont, e.ImageRectangle, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ------------------------------------------------------------------ Small custom controls

    class GlyphButton : Control
    {
        string glyph;
        readonly string label;
        bool hover, down, isChecked;
        public Color? HoverBack, HoverFore;
        public Action<Graphics, Rectangle, Color> Painter;

        public string Glyph
        {
            get { return glyph; }
            set { glyph = value; Invalidate(); }
        }
        static readonly ToolTip tips = new ToolTip();

        public GlyphButton(string glyph, string tip, DockStyle dock, string label = null)
        {
            this.glyph = glyph;
            this.label = label;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false; Dock = dock;
            Width = Native.Px(32) + (label == null ? 0 : TextRenderer.MeasureText(label, SystemFonts.MessageBoxFont).Width + Native.Px(4));
            tips.SetToolTip(this, tip);
        }

        public bool Checked
        {
            get { return isChecked; }
            set { isChecked = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Window);
            Rectangle r = new Rectangle(Native.Px(2), Native.Px(2), Width - Native.Px(4), Height - Native.Px(4));
            if (hover && HoverBack.HasValue)
            {
                using (SolidBrush b = new SolidBrush(HoverBack.Value)) g.FillRectangle(b, ClientRectangle);
                TextRenderer.DrawText(g, glyph, Theme.IconFont, ClientRectangle, HoverFore ?? Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }
            if (hover || isChecked)
                using (SolidBrush b = new SolidBrush(down ? Theme.Border : Theme.Hover)) g.FillRectangle(b, r);
            if (isChecked)
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X, r.Bottom - Native.Px(2), r.Width, Native.Px(2));
            Color fg = !Enabled ? Theme.TextDim : isChecked ? Theme.Text : (label != null ? Theme.TextDim : Theme.Text);
            Rectangle gr = label == null ? ClientRectangle : new Rectangle(0, 0, Native.Px(30), Height);
            if (Painter != null)
            {
                int w = Native.Px(16), h = Native.Px(13);
                Painter(g, new Rectangle(gr.X + (gr.Width - w) / 2, gr.Y + (gr.Height - h) / 2, w, h), fg);
            }
            else
                TextRenderer.DrawText(g, glyph, Theme.IconFont, gr, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (label != null)
                TextRenderer.DrawText(g, label, Font, new Rectangle(gr.Right, 0, Width - gr.Right, Height), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    class TabStrip : Control
    {
        readonly Pane pane;
        readonly List<Rectangle> rects = new List<Rectangle>();
        Rectangle plusRect;
        int hover = -1, drag = -1, tipIndex = -2;
        bool hoverPlus;
        readonly ToolTip tip = new ToolTip();
        Font bold;

        public TabStrip(Pane p)
        {
            pane = p;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false; Dock = DockStyle.Top; Height = Native.Px(32);
        }

        Font Bold { get { if (bold == null) bold = new Font(Font, FontStyle.Bold); return bold; } }
        protected override void OnFontChanged(EventArgs e)
        {
            if (bold != null) { bold.Dispose(); bold = null; }
            base.OnFontChanged(e);
        }

        void DoLayout(Graphics g)
        {
            rects.Clear();
            int n = pane.Tabs.Count, pad = Native.Px(10), icon = Native.Px(16), gap = Native.Px(6), plusW = Native.Px(34);
            int avail = Math.Max(1, Width - plusW - Native.Px(4));
            int[] pref = new int[n];
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                int w = pad + icon + gap + TextRenderer.MeasureText(g, pane.Tabs[i].Title, Bold, Size.Empty, TextFormatFlags.NoPadding).Width + pad;
                pref[i] = Math.Max(Native.Px(70), Math.Min(Native.Px(230), w));
                total += pref[i];
            }
            float k = total > avail ? (float)avail / total : 1f;
            int minW = Native.Px(40);
            if (n > 0 && minW * n > avail) minW = Math.Max(Native.Px(20), avail / n);
            int x = 0;
            for (int i = 0; i < n; i++)
            {
                int w = Math.Max(minW, (int)(pref[i] * k));
                rects.Add(new Rectangle(x, 0, w, Height));
                x += w;
            }
            plusRect = new Rectangle(Math.Min(x, Math.Max(0, Width - plusW)), 0, plusW, Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bar);
            DoLayout(g);
            int active = pane.ActiveIndex, top = Native.Px(4), pad = Native.Px(10), iconSize = Native.Px(16), gap = Native.Px(6);
            Rectangle activeRect = Rectangle.Empty;
            for (int i = 0; i < rects.Count; i++)
            {
                Rectangle r = rects[i];
                BrowserTab t = pane.Tabs[i];
                bool isActive = i == active;
                Rectangle body = new Rectangle(r.X, top, r.Width, Height - top);
                if (isActive)
                {
                    activeRect = body;
                    using (SolidBrush b = new SolidBrush(Theme.Window)) g.FillRectangle(b, body);
                    using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, body.X, body.Y, body.Width - 1, body.Height);
                    Color line = pane.IsActivePane ? Theme.Accent : Theme.TextDim;
                    using (SolidBrush b = new SolidBrush(line)) g.FillRectangle(b, body.X, body.Y, body.Width, Native.Px(2));
                }
                else
                {
                    if (i == hover) using (SolidBrush b = new SolidBrush(Theme.TabHover)) g.FillRectangle(b, body);
                    if (i + 1 != active && i != hover)
                        using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, r.Right - 1, Native.Px(10), r.Right - 1, Height - Native.Px(7));
                }
                Rectangle ir = new Rectangle(r.X + pad, top + (Height - top - iconSize) / 2, iconSize, iconSize);
                if (t.Locked)
                    TextRenderer.DrawText(g, "", Theme.IconFont, ir, Theme.Lock, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                else if (t.Icon != null)
                    g.DrawIcon(t.Icon, ir);
                Rectangle tr = new Rectangle(ir.Right + gap, top, Math.Max(0, r.Right - pad - ir.Right - gap), Height - top);
                TextRenderer.DrawText(g, t.Title, isActive ? Bold : Font, tr, isActive ? Theme.Text : Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
            if (hoverPlus)
                using (SolidBrush b = new SolidBrush(Theme.TabHover))
                    g.FillRectangle(b, new Rectangle(plusRect.X + Native.Px(3), top + Native.Px(2), plusRect.Width - Native.Px(6), Height - top - Native.Px(5)));
            TextRenderer.DrawText(g, "", Theme.IconFont, new Rectangle(plusRect.X, top, plusRect.Width, Height - top), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (Pen p = new Pen(Theme.Border))
            {
                if (activeRect.IsEmpty) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
                else
                {
                    g.DrawLine(p, 0, Height - 1, activeRect.Left, Height - 1);
                    g.DrawLine(p, activeRect.Right - 1, Height - 1, Width, Height - 1);
                }
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int i = HitTest(e.Location);
            if (i >= 0) { pane.Select(i); drag = i; }
            else if (plusRect.Contains(e.Location)) pane.NewTab();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitTest(e.Location);
            if (drag >= 0 && (MouseButtons & MouseButtons.Left) != 0 && i >= 0 && i != drag && drag < rects.Count)
            {
                // Moving right: the dragged tab's width must fit before the cursor; moving left: after it.
                // Otherwise tabs of different widths would swap back and forth on every mouse move.
                int dw = rects[drag].Width;
                bool pass = i > drag ? e.X >= rects[i].Right - dw : e.X <= rects[i].Left + dw;
                if (pass)
                {
                    pane.MoveTab(drag, i);
                    drag = i;
                    using (Graphics g = CreateGraphics()) DoLayout(g);
                }
            }
            bool hp = plusRect.Contains(e.Location);
            if (i != hover || hp != hoverPlus) { hover = i; hoverPlus = hp; Invalidate(); }
            int tipKey = hp ? -1 : i;
            if (tipKey != tipIndex)
            {
                tipIndex = tipKey;
                tip.SetToolTip(this, hp ? "New tab (Ctrl+T)" : i >= 0 ? pane.Tabs[i].Tooltip : null);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            drag = -1;
            int i = HitTest(e.Location);
            if (e.Button == MouseButtons.Middle && i >= 0) pane.CloseTab(pane.Tabs[i]);
            if (e.Button == MouseButtons.Right) pane.ShowTabMenu(i >= 0 ? pane.Tabs[i] : null, PointToScreen(e.Location));
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && HitTest(e.Location) < 0 && !plusRect.Contains(e.Location)) pane.NewTab();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1; hoverPlus = false; tipIndex = -2;
            Invalidate();
        }
    }

    // ------------------------------------------------------------------ One tab = one embedded Explorer view

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
            if (target == null || Native.SameFolder(target, LockedFolder)) return 0;
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

    // ------------------------------------------------------------------ Pane = tab strip + address bar + views

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
            int at = atEnd || active < 0 ? Tabs.Count : active + 1;
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
                add.Enabled = Directory.Exists(tab.Address);
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

        void ShowMainMenu()
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
            AddItem(m.Items, "Keyboard shortcuts", null, delegate { Main.ShowHelp(); });
            AddItem(m.Items, "About " + Program.AppName, null, delegate
            {
                MessageBox.Show(Main, Program.AppName + " " + Installer.Version + "\n\nA light multi-pane file manager built on the Windows Explorer view.\n\nSettings: " + MainForm.StateFile + "\nShortcuts (shared through OneDrive): " + ShortcutsPane.ListFile,
                    "About " + Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            m.Show(menuBtn, new Point(menuBtn.Width, menuBtn.Height), ToolStripDropDownDirection.BelowLeft);
        }

        static ToolStripMenuItem AddItem(ToolStripItemCollection items, string text, string keys, EventHandler click)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text, null, click);
            if (keys != null) it.ShortcutKeyDisplayString = keys;
            items.Add(it);
            return it;
        }
    }

    // ------------------------------------------------------------------ Custom title bar (icon, title, theme switch, caption buttons)

    class TitleBar : Control
    {
        readonly Form form;
        public readonly GlyphButton[] ThemeButtons = new GlyphButton[3];
        // Tree, 1 pane, 2 panes, 3 panes, 4 panes, Preview, Shortcuts
        public readonly GlyphButton[] LayoutButtons = new GlyphButton[7];
        // Details, List, Tiles, Content, Medium icons, Large icons (for the active pane)
        public readonly GlyphButton[] ViewButtons = new GlyphButton[6];
        public static readonly string[] ViewNames = { "Details", "List", "Tiles", "Content", "Medium icons", "Large icons" };
        readonly GlyphButton min, max, close;
        Icon icon;

        public TitleBar(Form f)
        {
            form = f;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Dock = DockStyle.Top;
            Height = Native.Px(34);

            string[] glyphs = { "", "", "" };
            string[] tips = { "Theme: match Windows", "Theme: light", "Theme: dark" };
            for (int i = 0; i < 3; i++)
            {
                ThemeButtons[i] = new GlyphButton(glyphs[i], tips[i], DockStyle.Right);
                ThemeButtons[i].Width = Native.Px(32);
            }
            Divider gap = new Divider();
            gap.Dock = DockStyle.Right;
            gap.Width = Native.Px(17);
            Divider gap2 = new Divider();
            gap2.Dock = DockStyle.Right;
            gap2.Width = Native.Px(17);
            string[] layoutTips = { "Tree pane (Alt+T)", "Single pane (Alt+1)", "Two panes (Alt+2)", "Three panes (Alt+3)", "Four panes (Alt+4)", "Preview pane (Alt+P)", "Shortcuts pane (Alt+S)" };
            for (int i = 0; i < 7; i++)
            {
                int kind = i;
                LayoutButtons[i] = new GlyphButton("", layoutTips[i], DockStyle.Right);
                LayoutButtons[i].Width = Native.Px(32);
                LayoutButtons[i].Painter = delegate(Graphics g, Rectangle r, Color c) { DrawLayoutIcon(g, r, c, kind); };
            }
            min = new GlyphButton("", "Minimize", DockStyle.Right);
            max = new GlyphButton("", "Maximize", DockStyle.Right);
            close = new GlyphButton("", "Close", DockStyle.Right);
            min.Width = max.Width = close.Width = Native.Px(46);
            close.HoverBack = Color.FromArgb(196, 43, 28);
            close.HoverFore = Color.White;
            min.Click += delegate { form.WindowState = FormWindowState.Minimized; };
            max.Click += delegate { form.WindowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; };
            close.Click += delegate { form.Close(); };
            // The last control added docks first, so this reads right-to-left: close, max, min, gap, dark, light, system.
            // Left to right: panes 1-4 | view modes | Tree, Preview, Shortcuts | themes | window buttons.
            for (int i = 1; i <= 4; i++) Controls.Add(LayoutButtons[i]);
            Divider vgap1 = new Divider();
            vgap1.Dock = DockStyle.Right;
            vgap1.Width = Native.Px(17);
            Controls.Add(vgap1);
            for (int i = 0; i < 6; i++)
            {
                int kind = i;
                ViewButtons[i] = new GlyphButton("", "View: " + ViewNames[i] + " (Ctrl+Shift+" + new[] { 6, 5, 7, 8, 3, 2 }[i] + ")", DockStyle.Right);
                ViewButtons[i].Width = Native.Px(30);
                ViewButtons[i].Painter = delegate(Graphics g, Rectangle r, Color c) { DrawViewIcon(g, r, c, kind); };
                Controls.Add(ViewButtons[i]);
            }
            Divider vgap2 = new Divider();
            vgap2.Dock = DockStyle.Right;
            vgap2.Width = Native.Px(17);
            Controls.Add(vgap2);
            Controls.Add(LayoutButtons[0]);
            Controls.Add(LayoutButtons[5]);
            Controls.Add(LayoutButtons[6]);
            Controls.Add(gap2);
            Controls.Add(ThemeButtons[0]);
            Controls.Add(ThemeButtons[1]);
            Controls.Add(ThemeButtons[2]);
            Controls.Add(gap);
            Controls.Add(min);
            Controls.Add(max);
            Controls.Add(close);
            form.Resize += delegate
            {
                bool m = form.WindowState == FormWindowState.Maximized;
                max.Glyph = m ? "" : "";
            };
            form.Activated += delegate { Invalidate(); };
            form.Deactivate += delegate { Invalidate(); };
        }

        // Small window-layout pictograms: an outline with the relevant region filled or divided.
        static void DrawLayoutIcon(Graphics g, Rectangle r, Color c, int kind)
        {
            using (Pen p = new Pen(c))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, c)))
            {
                Rectangle o = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
                int bw = Math.Max(3, r.Width * 5 / 16), bh = Math.Max(3, r.Height * 4 / 13);
                switch (kind)
                {
                    case 0: g.FillRectangle(b, r.X, r.Y, bw, r.Height); break;                       // tree: left column
                    case 2: case 3: case 4:                                                          // 2-4 panes: dividers
                        for (int k = 1; k < kind; k++)
                        {
                            int x = r.X + (r.Width - 1) * k / kind;
                            g.DrawLine(p, x, r.Y, x, r.Bottom - 1);
                        }
                        break;
                    case 5: g.FillRectangle(b, r.Right - bw, r.Y, bw, r.Height); break;              // preview: right column
                    case 6: g.FillRectangle(b, r.X, r.Bottom - bh, r.Width, bh); break;              // shortcuts: bottom strip
                }
                g.DrawRectangle(p, o);
            }
        }

        // Pictograms for the view modes, drawn in the same 16x13 box as the layout icons.
        static void DrawViewIcon(Graphics g, Rectangle r, Color c, int kind)
        {
            using (Pen p = new Pen(c))
            using (SolidBrush b = new SolidBrush(c))
            {
                int x = r.X, y = r.Y, w = r.Width, h = r.Height;
                switch (kind)
                {
                    case 0: // details: rows with a column divider
                        for (int k = 0; k < 4; k++) { int yy = y + 1 + k * (h - 2) / 3; g.DrawLine(p, x, yy, x + w - 1, yy); }
                        g.DrawLine(p, x + w * 5 / 9, y, x + w * 5 / 9, y + h - 1);
                        break;
                    case 1: // list: two columns of short rows
                        for (int k = 0; k < 4; k++)
                        {
                            int yy = y + 1 + k * (h - 2) / 3;
                            g.FillRectangle(b, x, yy - 1, 2, 2); g.DrawLine(p, x + 3, yy, x + w / 2 - 2, yy);
                            g.FillRectangle(b, x + w / 2 + 1, yy - 1, 2, 2); g.DrawLine(p, x + w / 2 + 4, yy, x + w - 1, yy);
                        }
                        break;
                    case 2: // tiles: two rows of [square + two lines]
                        for (int k = 0; k < 2; k++)
                        {
                            int yy = y + k * (h / 2 + 1), s = h / 2 - 1;
                            g.DrawRectangle(p, x, yy, s, s);
                            g.DrawLine(p, x + s + 3, yy + 1, x + w - 1, yy + 1);
                            g.DrawLine(p, x + s + 3, yy + s - 1, x + w - 4, yy + s - 1);
                        }
                        break;
                    case 3: // content: rows of [small square + long line], separated
                        for (int k = 0; k < 3; k++)
                        {
                            int yy = y + k * h / 3;
                            g.FillRectangle(b, x, yy + 1, 3, 3);
                            g.DrawLine(p, x + 5, yy + 2, x + w - 1, yy + 2);
                        }
                        break;
                    case 4: // medium icons: 3 x 2 squares
                        for (int k = 0; k < 6; k++)
                        {
                            int s = Math.Max(3, w / 4);
                            g.DrawRectangle(p, x + (k % 3) * (w - s - 1) / 2, y + (k / 3) * (h - s - 1), s, s);
                        }
                        break;
                    case 5: // large icons: 2 big squares
                        {
                            int s = Math.Min(w / 2 - 2, h - 2);
                            g.DrawRectangle(p, x, y + (h - s) / 2, s, s);
                            g.DrawRectangle(p, x + w - s - 1, y + (h - s) / 2, s, s);
                        }
                        break;
                }
            }
        }

        public void SetIcon(Icon i) { icon = i == null ? null : new Icon(i, Native.Px(16), Native.Px(16)); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bar);
            int s = Native.Px(16), x = Native.Px(12);
            if (icon != null) g.DrawIcon(icon, new Rectangle(x, (Height - s) / 2, s, s));
            bool activeWindow = Form.ActiveForm == form;
            TextRenderer.DrawText(g, form.Text, Font, new Rectangle(x + s + Native.Px(10), 0, Width / 2, Height),
                activeWindow ? Theme.Text : Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }

        // A thin vertical rule that separates the theme switch from the window buttons.
        class Divider : Control
        {
            public Divider()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Theme.Bar);
                int x = Width / 2;
                using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, x, 0, x, Height);
                using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; } // part of the caption
                base.WndProc(ref m);
            }
        }

        // Let the empty parts of the bar act as the window caption (drag, double-click, system menu).
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST) { m.Result = (IntPtr)HTTRANSPARENT; return; }
            base.WndProc(ref m);
        }
    }

    // ------------------------------------------------------------------ Splitter that resizes live while dragging

    class LiveSplit : SplitContainer
    {
        bool dragging;
        int grab;

        public LiveSplit()
        {
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);
        }

        bool Vertical { get { return Orientation == Orientation.Vertical; } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && SplitterRectangle.Contains(e.Location))
            {
                // Skip the base class so it doesn't start its own ghost-line drag.
                dragging = true;
                grab = (Vertical ? e.X : e.Y) - SplitterDistance;
                Capture = true;
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) { base.OnMouseMove(e); return; }
            int size = Vertical ? Width : Height;
            int pos = (Vertical ? e.X : e.Y) - grab;
            pos = Math.Max(Panel1MinSize, Math.Min(size - SplitterWidth - Panel2MinSize, pos));
            if (pos > 0 && pos != SplitterDistance)
            {
                SplitterDistance = pos;
                Update();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) { base.OnMouseUp(e); return; }
            dragging = false;
            Capture = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            dragging = false;
            base.OnMouseCaptureChanged(e);
        }
    }

    // ------------------------------------------------------------------ Tree pane (one tree, follows the active pane)

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
            if (tree == null || folder == null || Native.SameFolder(folder, synced)) return;
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

    // ------------------------------------------------------------------ Preview pane (previews the active pane's selected file)

    class PreviewPane : Panel
    {
        readonly Label header = new Label(), message = new Label();
        readonly Panel host = new Panel();
        readonly PictureBox picture = new PictureBox();
        readonly ListView sizes = new ListView();
        IPreviewHandler handler;
        string current = "";
        SizeJob shownJob;
        public SizeJob ShownJob { get { return shownJob; } }

        public PreviewPane()
        {
            Dock = DockStyle.Fill;
            header.Dock = DockStyle.Top;
            header.Height = Native.Px(24);
            header.Padding = new Padding(Native.Px(8), 0, Native.Px(8), 0);
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.AutoEllipsis = true;
            header.UseMnemonic = false;
            header.Text = "Preview";
            host.Dock = DockStyle.Fill;
            host.Resize += delegate { Layout2(); };
            picture.Dock = DockStyle.Fill;
            picture.SizeMode = PictureBoxSizeMode.Zoom;
            picture.Visible = false;
            message.Dock = DockStyle.Fill;
            message.TextAlign = ContentAlignment.MiddleCenter;
            message.Text = "Select a file to preview";
            sizes.Dock = DockStyle.Fill;
            sizes.View = View.Details;
            sizes.BorderStyle = BorderStyle.None;
            sizes.FullRowSelect = true;
            sizes.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            sizes.Columns.Add("Folder", Native.Px(170));
            sizes.Columns.Add("Size", Native.Px(80), HorizontalAlignment.Right);
            sizes.Columns.Add("%", Native.Px(44), HorizontalAlignment.Right);
            sizes.Columns.Add("Files", Native.Px(70), HorizontalAlignment.Right);
            sizes.Visible = false;
            sizes.HandleCreated += delegate { ThemeSizes(); };
            host.Controls.Add(sizes);
            host.Controls.Add(picture);
            host.Controls.Add(message);
            Controls.Add(host);
            Controls.Add(header);
        }

        public void ApplyTheme()
        {
            header.BackColor = Theme.Bar;
            header.ForeColor = Theme.TextDim;
            host.BackColor = picture.BackColor = message.BackColor = sizes.BackColor = Theme.Window;
            message.ForeColor = Theme.TextDim;
            sizes.ForeColor = Theme.Text;
            if (sizes.IsHandleCreated) ThemeSizes();
            string again = current;
            current = "";
            if (Visible) Show(again);
        }

        RECT HostRect()
        {
            RECT r = new RECT();
            r.right = host.ClientSize.Width;
            r.bottom = host.ClientSize.Height;
            return r;
        }

        void ThemeSizes()
        {
            Native.SetWindowTheme(sizes.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
            IntPtr header = Native.SendMessage(sizes.Handle, 0x101F /* LVM_GETHEADER */, IntPtr.Zero, IntPtr.Zero);
            if (header != IntPtr.Zero) Native.SetWindowTheme(header, Theme.Dark ? "DarkMode_ItemsView" : "ItemsView", null);
        }

        void Layout2()
        {
            if (handler == null) return;
            RECT r = HostRect();
            try { handler.SetRect(ref r); } catch { }
        }

        public void Show(string path)
        {
            if (path == null) path = "";
            if (path == current && shownJob == null) return;
            current = path;
            shownJob = null;
            sizes.Visible = false;
            Unload();
            header.Text = path.Length == 0 ? "Preview" : Path.GetFileName(path.TrimEnd('\\'));
            if (path.Length == 0) { ShowMessage("Select a file to preview"); return; }
            if (!Directory.Exists(path) && TryHandler(path)) return;
            StartThumbnail(path);
        }

        int thumbTicket;

        // Thumbnail extraction can be slow (video, large images, cloud files), so it runs off the UI thread;
        // a result that arrives after the selection changed is discarded.
        void StartThumbnail(string path)
        {
            int ticket = ++thumbTicket;
            SIZE s = new SIZE();
            s.cx = Math.Max(64, Math.Min(1024, host.ClientSize.Width));
            s.cy = Math.Max(64, Math.Min(1024, host.ClientSize.Height));
            ShowMessage("Loading preview…");
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                Bitmap bmp = null;
                IShellItem item = null;
                try
                {
                    item = Native.ItemFromPath(path);
                    IShellItemImageFactory fac = item as IShellItemImageFactory;
                    IntPtr hbmp;
                    if (fac != null && fac.GetImage(s, 0, out hbmp) == 0 && hbmp != IntPtr.Zero)
                    {
                        try { bmp = BitmapWithAlpha(hbmp); }
                        finally { Native.DeleteObject(hbmp); }
                    }
                }
                catch { }
                finally { if (item != null) try { Marshal.ReleaseComObject(item); } catch { } }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (ticket != thumbTicket || current != path) { if (bmp != null) bmp.Dispose(); return; }
                        if (bmp == null) { ShowMessage("No preview available"); return; }
                        Image old = picture.Image;
                        picture.Image = bmp;
                        if (old != null) old.Dispose();
                        message.Visible = false;
                        picture.Visible = true;
                    });
                }
                catch { if (bmp != null) bmp.Dispose(); }
            });
            th.SetApartmentState(System.Threading.ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
        }

        void ShowMessage(string text)
        {
            picture.Visible = false;
            message.Text = text;
            message.Visible = true;
        }

        bool TryHandler(string path)
        {
            string clsid = Native.PreviewHandlerFor(Path.GetExtension(path));
            if (clsid == null) return false;
            object o = null;
            try
            {
                // Out of process only, like File Explorer (prevhost.exe): a misbehaving handler can't crash or
                // run inside the app. Handlers that only work in process get the thumbnail instead.
                o = Native.CreateComObject(new Guid(clsid), 0x4 /* CLSCTX_LOCAL_SERVER */);
                if (o == null) return false;
                bool ok = false;
                IInitializeWithStream ws = o as IInitializeWithStream;
                System.Runtime.InteropServices.ComTypes.IStream stream;
                // STGM_READ | STGM_SHARE_DENY_NONE
                if (ws != null && Native.SHCreateStreamOnFileEx(path, 0x40, 0, false, IntPtr.Zero, out stream) == 0)
                {
                    ok = ws.Initialize(stream, 0) == 0;
                    Marshal.ReleaseComObject(stream); // the handler keeps its own reference if it needs one
                }
                if (!ok)
                {
                    IInitializeWithFile f = o as IInitializeWithFile;
                    if (f != null) ok = f.Initialize(path, 0) == 0;
                }
                if (!ok)
                {
                    IInitializeWithItem wi = o as IInitializeWithItem;
                    IShellItem item = wi != null ? Native.ItemFromPath(path) : null;
                    if (item != null) { ok = wi.Initialize(item, 0) == 0; Marshal.ReleaseComObject(item); }
                }
                if (!ok) { Marshal.ReleaseComObject(o); return false; }
                handler = (IPreviewHandler)o;
                IPreviewHandlerVisuals v = o as IPreviewHandlerVisuals;
                if (v != null)
                {
                    v.SetBackgroundColor(Native.ColorRef(Theme.Window));
                    v.SetTextColor(Native.ColorRef(Theme.Text));
                }
                message.Visible = picture.Visible = false;
                RECT r = HostRect();
                if (handler.SetWindow(host.Handle, ref r) != 0 || handler.DoPreview() != 0) { Unload(); return false; }
                return true;
            }
            catch
            {
                if (handler == null && o != null) try { Marshal.ReleaseComObject(o); } catch { }
                Unload();
                return false;
            }
        }

        // Image.FromHbitmap drops the alpha channel; keep it when the shell returns a transparent image.
        static Bitmap BitmapWithAlpha(IntPtr hbmp)
        {
            Bitmap rgb = Image.FromHbitmap(hbmp);
            if (Image.GetPixelFormatSize(rgb.PixelFormat) != 32) return rgb;
            int w = rgb.Width, h = rgb.Height, row = w * 4;
            Rectangle r = new Rectangle(0, 0, w, h);
            byte[] bytes = new byte[row * h];
            // Copy row by row: the stride can be negative (bottom-up bitmaps), so one big copy could read past the image.
            System.Drawing.Imaging.BitmapData d = rgb.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try { for (int y = 0; y < h; y++) Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), bytes, y * row, row); }
            finally { rgb.UnlockBits(d); }
            bool anyAlpha = false;
            for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) return rgb;
            Bitmap argb = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData a = argb.LockBits(r, System.Drawing.Imaging.ImageLockMode.WriteOnly, argb.PixelFormat);
            try { for (int y = 0; y < h; y++) Marshal.Copy(bytes, y * row, IntPtr.Add(a.Scan0, y * a.Stride), row); }
            finally { argb.UnlockBits(a); }
            rgb.Dispose();
            return argb;
        }
        public void Unload()
        {
            thumbTicket++; // any thumbnail still being made is no longer wanted
            if (handler != null)
            {
                try { handler.Unload(); } catch { }
                try { Marshal.FinalReleaseComObject(handler); } catch { }
                handler = null;
            }
            Image old = picture.Image;
            picture.Image = null;
            if (old != null) old.Dispose();
        }

        public void Clear()
        {
            Show(null);
        }

        // Shows a folder's subfolders with their sizes, largest first; called again as the scan progresses.
        public void ShowSizes(SizeJob job)
        {
            if (shownJob != job)
            {
                Unload();
                shownJob = job;
                current = job.Root;
                picture.Visible = message.Visible = false;
                sizes.Visible = true;
            }
            long total = job.TotalBytes;
            string state = job.Failure != null ? "stopped" : job.Finished ? Native.FormatBytes(total) + " in " + job.TotalFiles.ToString("N0") + " files" : "calculating… " + Native.FormatBytes(total);
            if (job.Errors > 0) state += "  (at least: " + job.Errors + (job.Errors == 1 ? " folder" : " folders") + " couldn't be read)";
            header.Text = Path.GetFileName(job.Root.TrimEnd('\\')) + "  ·  " + state;
            if (header.Text.StartsWith("  ")) header.Text = job.Root + "  ·  " + state;
            sizes.BeginUpdate();
            sizes.Items.Clear();
            foreach (SizeEntry e in job.Snapshot())
            {
                ListViewItem it = new ListViewItem(e.Name);
                it.SubItems.Add(e.Done ? Native.FormatBytes(e.Bytes) : (e.Bytes > 0 ? Native.FormatBytes(e.Bytes) + "…" : "…"));
                it.SubItems.Add(total > 0 ? (100.0 * e.Bytes / total).ToString("0") : "");
                it.SubItems.Add(e.Files.ToString("N0"));
                it.ForeColor = e.Done ? Theme.Text : Theme.TextDim;
                it.ToolTipText = e.Path;
                sizes.Items.Add(it);
            }
            sizes.EndUpdate();
        }
    }

    // ------------------------------------------------------------------ Folder sizes (background scan of one folder's subfolders)

    class SizeEntry
    {
        public string Name, Path;
        public long Bytes, Files;
        public bool Done;
    }

    class SizeJob
    {
        public const long MaxEntries = 2000000;
        public const int MaxSeconds = 90;

        public readonly string Root;
        public readonly List<SizeEntry> Entries = new List<SizeEntry>();
        public volatile bool Cancel;
        public bool Finished;
        public string Failure;   // set when the scan stopped for a "critical" reason
        public int Errors;       // folders that couldn't be read: the totals are then a lower bound
        public DateTime FinishedAt;
        readonly MainForm main;
        long scanned;
        Stopwatch clock;

        SizeJob(string root, MainForm m) { Root = root; main = m; }

        public long TotalBytes { get { long t = 0; lock (Entries) foreach (SizeEntry e in Entries) t += e.Bytes; return t; } }
        public long TotalFiles { get { long t = 0; lock (Entries) foreach (SizeEntry e in Entries) t += e.Files; return t; } }

        public static SizeJob Start(string root, MainForm m)
        {
            SizeJob j = new SizeJob(root, m);
            System.Threading.Thread th = new System.Threading.Thread(j.Run);
            th.IsBackground = true;
            th.Priority = System.Threading.ThreadPriority.BelowNormal;
            th.Start();
            return j;
        }

        void Run()
        {
            // Background mode lowers this thread's disk and CPU priority so browsing stays responsive.
            Native.SetThreadPriority(Native.GetCurrentThread(), 0x00010000 /* THREAD_MODE_BACKGROUND_BEGIN */);
            clock = Stopwatch.StartNew();
            try
            {
                SizeEntry here = new SizeEntry();
                here.Name = "(files in this folder)";
                here.Path = Root;
                List<SizeEntry> subs = new List<SizeEntry>();
                Enumerate(Root, delegate(Native.WIN32_FIND_DATA d, bool dir)
                {
                    if (dir)
                    {
                        SizeEntry e = new SizeEntry();
                        e.Name = d.cFileName;
                        e.Path = System.IO.Path.Combine(Root, d.cFileName);
                        subs.Add(e);
                    }
                    else { here.Bytes += Size(d); here.Files++; }
                });
                here.Done = true;
                lock (Entries) { Entries.AddRange(subs); if (here.Files > 0) Entries.Add(here); }
                Notify(true);
                foreach (SizeEntry e in subs)
                {
                    if (Cancel || Failure != null) break;
                    ScanTree(e);
                    lock (Entries) e.Done = true;
                    Notify(false);
                }
            }
            catch (Exception ex) { if (Failure == null && !Cancel) Failure = ex.Message; }
            Finished = true;
            FinishedAt = DateTime.Now;
            Notify(true);
        }

        static long Size(Native.WIN32_FIND_DATA d) { return ((long)d.nFileSizeHigh << 32) | d.nFileSizeLow; }

        void ScanTree(SizeEntry e)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(e.Path);
            while (stack.Count > 0 && !Cancel && Failure == null)
            {
                string dir = stack.Pop();
                long bytes = 0, files = 0;
                Enumerate(dir, delegate(Native.WIN32_FIND_DATA d, bool isDir)
                {
                    if (isDir) stack.Push(System.IO.Path.Combine(dir, d.cFileName));
                    else { bytes += Size(d); files++; }
                });
                // Readers lock Entries; update the live totals under the same lock.
                lock (Entries) { e.Bytes += bytes; e.Files += files; }
                Notify(false);
            }
        }

        delegate void Visit(Native.WIN32_FIND_DATA d, bool isDir);

        void Enumerate(string dir, Visit visit)
        {
            if (OverBudget()) return;
            string pattern = (dir.StartsWith(@"\\") ? dir : @"\\?\" + dir).TrimEnd('\\') + @"\*";
            Native.WIN32_FIND_DATA d;
            // FindExInfoBasic, FIND_FIRST_EX_LARGE_FETCH. Enumerating never downloads OneDrive files.
            IntPtr h = Native.FindFirstFileEx(pattern, 1, out d, 0, IntPtr.Zero, 2);
            if (h == (IntPtr)(-1))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 2 && err != 18) System.Threading.Interlocked.Increment(ref Errors); // not "no files"
                return;
            }
            try
            {
                do
                {
                    if (Cancel) return;
                    if (d.cFileName == "." || d.cFileName == "..") continue;
                    bool isDir = (d.dwFileAttributes & 0x10) != 0;
                    // Skip junctions and symbolic links so nothing is counted twice or loops. Other reparse
                    // points (OneDrive and other cloud folders) are ordinary folders and are scanned.
                    const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003, IO_REPARSE_TAG_SYMLINK = 0xA000000C;
                    if (isDir && (d.dwFileAttributes & 0x400) != 0 &&
                        (d.dwReserved0 == IO_REPARSE_TAG_MOUNT_POINT || d.dwReserved0 == IO_REPARSE_TAG_SYMLINK)) continue;
                    // The limits are checked for every item, so one huge folder can't run past them.
                    if (OverBudget()) return;
                    visit(d, isDir);
                }
                while (Native.FindNextFile(h, out d));
                if (Marshal.GetLastWin32Error() != 18 /* ERROR_NO_MORE_FILES */) System.Threading.Interlocked.Increment(ref Errors);
            }
            finally { Native.FindClose(h); }
        }

        bool OverBudget()
        {
            if (Failure != null) return true;
            if (++scanned > MaxEntries) { Failure = "more than " + (MaxEntries / 1000000) + " million items to scan"; return true; }
            if ((scanned & 255) == 0 && clock.Elapsed.TotalSeconds > MaxSeconds) { Failure = "the scan took longer than " + MaxSeconds + " seconds"; return true; }
            return false;
        }

        int lastNotifyTick = Environment.TickCount - 1000;

        // At most four UI updates a second, plus the forced ones at the start and the end.
        void Notify(bool force)
        {
            if (Cancel) return;
            if (!force && unchecked(Environment.TickCount - lastNotifyTick) < 250) return;
            lastNotifyTick = Environment.TickCount;
            try { main.BeginInvoke((MethodInvoker)delegate { main.SizeJobUpdated(this); }); } catch { }
        }

        // Copies the values first: the scan keeps updating the live entries while we sort.
        public List<SizeEntry> Snapshot()
        {
            List<SizeEntry> copy = new List<SizeEntry>();
            lock (Entries)
                foreach (SizeEntry e in Entries)
                {
                    SizeEntry s = new SizeEntry();
                    s.Name = e.Name; s.Path = e.Path; s.Bytes = e.Bytes; s.Files = e.Files; s.Done = e.Done;
                    copy.Add(s);
                }
            copy.Sort(delegate(SizeEntry a, SizeEntry b) { return b.Bytes.CompareTo(a.Bytes); });
            return copy;
        }
    }

    // ------------------------------------------------------------------ Shortcuts pane (bottom strip of saved folders)

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
        readonly HashSet<string> removedHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        // The list lives in OneDrive (when present) so every computer signed in to it shares the same shortcuts.
        // Old per-computer shortcuts still waiting to be moved into the shared file (kept in state.txt until then).
        public List<KeyValuePair<string, string>> PendingLegacy { get { return pendingLegacy; } }

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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); watcher = null; }
                reloadTimer.Dispose();
                saveRetry.Dispose();
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

        // Store paths relative to OneDrive / the user profile so they resolve on every computer.
        static string Portable(string path)
        {
            foreach (string v in new string[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial", "USERPROFILE" })
            {
                string root = Environment.GetEnvironmentVariable(v);
                if (string.IsNullOrEmpty(root)) continue;
                root = root.TrimEnd('\\');
                if (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                    return "%" + v + "%" + path.Substring(root.Length);
            }
            return path;
        }

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

        // Lines starting with "#" and without a "|" are comments, so a label like "#Archive" survives.
        static List<KeyValuePair<string, string>> ParseList(string[] lines)
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>();
            foreach (string line in lines)
            {
                int bar = line.IndexOf('|');
                if (bar <= 0) continue;
                r.Add(new KeyValuePair<string, string>(line.Substring(0, bar).Trim(),
                    Environment.ExpandEnvironmentVariables(line.Substring(bar + 1).Trim())));
            }
            return r;
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
            removedHere.Clear();
            baseEntries = ParseList(lines);
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

        // Three-way merge of the shortcuts (label, path) against their common ancestor: additions on either
        // side are kept, a deletion on either side wins, a label changed here wins over the other side, and
        // the order comes from whichever side rearranged it.
        static List<KeyValuePair<string, string>> Merge(List<KeyValuePair<string, string>> baseList,
            List<KeyValuePair<string, string>> local, List<KeyValuePair<string, string>> remote)
        {
            Dictionary<string, string> b = ToMap(baseList), l = ToMap(local), r = ToMap(remote);
            bool localReordered = !SameOrder(baseList, local, b, l);
            List<KeyValuePair<string, string>> first = localReordered ? local : remote, second = localReordered ? remote : local;
            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();
            HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (List<KeyValuePair<string, string>> src in new List<KeyValuePair<string, string>>[] { first, second })
                foreach (KeyValuePair<string, string> e in src)
                {
                    string p = Key(e.Value);
                    if (done.Contains(p)) continue;
                    bool inB = b.ContainsKey(p), inL = l.ContainsKey(p), inR = r.ContainsKey(p);
                    bool keep = inB ? (inL && inR) : (inL || inR);
                    if (!keep) continue;
                    done.Add(p);
                    string label = inL && (!inB || l[p] != b[p]) ? l[p] : inR ? r[p] : l[p];
                    result.Add(new KeyValuePair<string, string>(label, e.Value));
                }
            return result;
        }

        static string Key(string path) { return (path ?? "").TrimEnd('\\').ToLowerInvariant(); }

        static Dictionary<string, string> ToMap(List<KeyValuePair<string, string>> list)
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> e in list) m[Key(e.Value)] = e.Key;
            return m;
        }

        // Whether the entries both lists share appear in the same order.
        static bool SameOrder(List<KeyValuePair<string, string>> a, List<KeyValuePair<string, string>> b,
            Dictionary<string, string> aMap, Dictionary<string, string> bMap)
        {
            List<string> x = new List<string>(), y = new List<string>();
            foreach (KeyValuePair<string, string> e in a) if (bMap.ContainsKey(Key(e.Value))) x.Add(Key(e.Value));
            foreach (KeyValuePair<string, string> e in b) if (aMap.ContainsKey(Key(e.Value))) y.Add(Key(e.Value));
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++) if (x[i] != y[i]) return false;
            return true;
        }

        bool SaveList()
        {
            if (!loadedOk) return false; // never overwrite a list we couldn't read
            try
            {
                List<KeyValuePair<string, string>> entries = CurrentEntries();
                // Another computer changed the file since we read it: merge both sets of changes.
                if (File.Exists(ListFile) && File.GetLastWriteTimeUtc(ListFile) != knownStamp)
                {
                    entries = Merge(baseEntries, entries, ParseList(File.ReadAllLines(ListFile, Encoding.UTF8)));
                    SetEntries(entries);
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Orcl File Explorer shortcuts, one per line as: label, a vertical bar, then the folder. Shared between computers through OneDrive.");
                foreach (KeyValuePair<string, string> e in entries) sb.AppendLine(e.Key + "|" + Portable(e.Value));
                Native.WriteAllTextAtomic(ListFile, sb.ToString());
                knownStamp = File.GetLastWriteTimeUtc(ListFile);
                baseEntries = entries;
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

        bool Contains(string path)
        {
            foreach (ListViewItem x in list.Items) if (Native.SameFolder((string)x.Tag, path)) return true;
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
                string moved = Native.Rebase((string)x.Tag, oldPath, newPath);
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
                if (Native.SameFolder((string)x.Tag, path)) { x.Selected = true; x.EnsureVisible(); return; }
            if (string.IsNullOrEmpty(label)) label = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(label)) label = path;
            label = label.Replace('|', '-');
            removedHere.Remove(path);
            if (!icons.Images.ContainsKey(path) && iconsPending.Add(path))
            {
                string iconPath = path;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    Icon ic = null;
                    IntPtr pidl = Native.ParsePath(iconPath);
                    if (pidl != IntPtr.Zero)
                        try { ic = Native.SmallIcon(pidl); } catch { } finally { Marshal.FreeCoTaskMem(pidl); }
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            iconsPending.Remove(iconPath);
                            // the ImageList keeps using ic until its handle exists: don't dispose it here
                            if (ic != null && !icons.Images.ContainsKey(iconPath)) { icons.Images.Add(iconPath, ic); list.Invalidate(); }
                        });
                    }
                    catch { if (ic != null) ic.Dispose(); }
                });
            }
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
            removedHere.Add((string)it.Tag);
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

    // ------------------------------------------------------------------ Pane row: 1-4 file panes side by side

    // Lays out the visible file panes left to right, with a draggable divider between neighbours
    // that resizes them live. Each pane keeps a relative width (weight) even while it is hidden.
    class PaneRow : Panel
    {
        readonly Pane[] panes;
        readonly List<Pane> shown = new List<Pane>();
        readonly List<PaneBar> bars = new List<PaneBar>();
        public readonly float[] Weights = { 1f, 1f, 1f, 1f };
        public event EventHandler WeightsChanged;

        public PaneRow(Pane[] panes)
        {
            this.panes = panes;
            Dock = DockStyle.Fill;
            foreach (Pane p in panes)
            {
                p.Dock = DockStyle.None;
                p.Visible = false;
                Controls.Add(p);
            }
        }

        public int BarWidth { get { return Native.Px(5); } }
        public int MinPaneWidth { get { return Native.Px(160); } }

        public void ShowPanes(List<Pane> visible)
        {
            SuspendLayout();
            shown.Clear();
            shown.AddRange(visible);
            while (bars.Count < Math.Max(0, shown.Count - 1))
            {
                PaneBar b = new PaneBar(this, bars.Count);
                b.BackColor = Theme.Border;
                bars.Add(b);
                Controls.Add(b);
            }
            for (int i = 0; i < bars.Count; i++) bars[i].Visible = i < shown.Count - 1;
            foreach (Pane p in panes) p.Visible = shown.Contains(p);
            ResumeLayout(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int n = shown.Count;
            if (n == 0 || bars.Count < n - 1) return;
            int avail = Math.Max(n, ClientSize.Width - BarWidth * (n - 1));
            float sum = 0;
            foreach (Pane p in shown) sum += Weight(p);
            int x = 0;
            for (int i = 0; i < n; i++)
            {
                int w = i == n - 1 ? avail - (x - BarWidth * i) : (int)Math.Round(avail * Weight(shown[i]) / sum);
                shown[i].SetBounds(x, 0, Math.Max(1, w), ClientSize.Height);
                x += w;
                if (i < n - 1) { bars[i].SetBounds(x, 0, BarWidth, ClientSize.Height); x += BarWidth; }
            }
        }

        float Weight(Pane p) { return Math.Max(0.05f, Weights[Array.IndexOf(panes, p)]); }

        // Called while divider i (right edge of shown[i]) is dragged to barLeft (in this control's
        // coordinates). Works from the widths at the moment the divider was grabbed, so nothing jumps.
        // Only this divider moves: shown[i] changes by exactly the drag distance, panes to its left stay
        // put, and the panes to its right share the difference equally. With two panes this is an
        // ordinary split.
        float[] dragStart;
        int dragStartBar;

        internal void BeginDrag(int barLeft)
        {
            dragStart = new float[shown.Count];
            for (int k = 0; k < shown.Count; k++) dragStart[k] = shown[k].Width;
            dragStartBar = barLeft;
        }

        internal void DragBarTo(int i, int barLeft)
        {
            int n = shown.Count;
            if (i < 0 || i + 1 >= n || dragStart == null || dragStart.Length != n) return;
            float d = barLeft - dragStartBar;
            int right = n - 1 - i; // panes to the right of the divider share the change
            // Keep every pane at least MinPaneWidth wide.
            float lo = MinPaneWidth - dragStart[i], hi = float.MaxValue;
            for (int k = i + 1; k < n; k++) hi = Math.Min(hi, (dragStart[k] - MinPaneWidth) * right);
            d = Math.Max(lo, Math.Min(hi, d));
            float total = 0;
            float[] w = new float[n];
            for (int k = 0; k < n; k++)
            {
                w[k] = k < i ? dragStart[k] : k == i ? dragStart[k] + d : dragStart[k] - d / right;
                total += w[k];
            }
            for (int k = 0; k < n; k++) Weights[Array.IndexOf(panes, shown[k])] = Math.Max(0.05f, w[k] * n / total);
            PerformLayout();
            Update();
        }

        internal void BarDropped()
        {
            if (WeightsChanged != null) WeightsChanged(this, EventArgs.Empty);
        }



        public void ApplyTheme()
        {
            BackColor = Theme.Window;
            foreach (PaneBar b in bars) b.BackColor = Theme.Border;
        }

        public void ResetWidths()
        {
            for (int i = 0; i < Weights.Length; i++) Weights[i] = 1f;
            PerformLayout();
            BarDropped();
        }
    }

    class PaneBar : Control
    {
        readonly PaneRow row;
        readonly int index;
        int grab;
        bool dragging;

        public PaneBar(PaneRow r, int i)
        {
            row = r;
            index = i;
            Cursor = Cursors.VSplit;
            BackColor = Theme.Border;
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            grab = e.X; // where on the divider it was grabbed
            row.BeginDrag(Left);
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) row.DragBarTo(index, Left + e.X - grab);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            row.BarDropped();
        }

        // Double-click a divider to make all visible panes the same width.
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            row.ResetWidths();
        }
    }

    // ------------------------------------------------------------------ Main window

    class MainForm : Form, IMessageFilter
    {
        public static readonly string StateFile = Environment.GetEnvironmentVariable("DUALPANE_STATE") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DualPane", "state.txt");

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
        int freeTick;
        bool freeBusy;
        string noticeText, stateSaveError;
        int noticeTick;

        // A short message in the status bar for a few seconds.
        public void Notice(string text)
        {
            noticeText = text;
            noticeTick = Environment.TickCount;
            UpdateStatus();
        }

        public MainForm()
        {
            Text = Program.AppName + " " + Installer.Version;
            Font = new Font("Segoe UI", 9f);
            Icon = AppIcon();
            titleBar = new TitleBar(this);
            titleBar.SetIcon(Icon);
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
            if (vsplit.Height > sh + Native.Px(200)) vsplit.SplitterDistance = vsplit.Height - sh - vsplit.SplitterWidth;
            vsplit.SplitterMoved += delegate { StateChanged(); };
            treeSplit.Panel1MinSize = Native.Px(120);
            previewSplit.Panel2MinSize = Native.Px(150);
            treeSplit.SplitterDistance = Math.Max(Native.Px(120), Math.Min(treeSplit.Width / 3, Native.Px(treeWidth)));
            int pw = Math.Max(Native.Px(150), Math.Min(previewSplit.Width / 2, Native.Px(previewWidth)));
            previewSplit.SplitterDistance = Math.Max(previewSplit.Panel1MinSize, previewSplit.Width - pw - previewSplit.SplitterWidth);
            treeSplit.SplitterMoved += delegate { StateChanged(); };
            previewSplit.SplitterMoved += delegate { StateChanged(); };
            treeSplit.Panel1Collapsed = !ShowTree;
            previewSplit.Panel2Collapsed = !ShowPreview;
            UpdateLayoutButtons();
            if (ShowTree) tree.EnsureCreated();
            Ready = true;
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
            Shortcuts.CheckAvailability();
            ActivePane = Panes[startPane];
            ApplyPaneLayout();
            foreach (Pane x in Panes) x.ApplyActiveLook();
            ActiveFolderChanged();
            hookProc = MouseHook;
            hook = Native.SetWindowsHookEx(Native.WH_MOUSE, hookProc, IntPtr.Zero, Native.GetCurrentThreadId());
            Application.AddMessageFilter(this);
            statusTimer.Start();
            BrowserTab t = ActivePane.ActiveTab;
            if (t != null) BeginInvoke((MethodInvoker)t.Activate);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            saveTimer.Stop();
            statusTimer.Stop();
            SaveState();
            Application.RemoveMessageFilter(this);
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            foreach (Pane p in Panes) foreach (BrowserTab t in p.Tabs) t.Destroy();
            preview.Unload();
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
            if (t != null) tree.SyncTo(t.Folder);
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
                if (candidate != lastSizeCandidate) { lastSizeCandidate = candidate; lastSizeTarget = candidate != null && Directory.Exists(candidate) ? candidate : null; }
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
            if (sizeJob != null && Native.SameFolder(sizeJob.Root, target) &&
                !(sizeJob.Finished && (DateTime.Now - sizeJob.FinishedAt).TotalMinutes >= 2)) return sizeJob;
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
            sizeJob = SizeJob.Start(target, this);
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
            MessageBox.Show(this, "Folder sizes were turned off because " + reason + ".\n\nYou can turn them on again from the menu (View options › Folder sizes).",
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

        // Windows fixes some light/dark decisions once per running app, so switching live leaves parts
        // (Explorer's lists, menus, scrollbars) in the old mode. Restarting is the only reliable switch.
        void RestartForTheme()
        {
            if (!Ready || restarting) return;
            restarting = true;
            saveTimer.Stop();
            SaveState();
            try
            {
                Program.Trace("restarting for theme");
                Process.Start(Application.ExecutablePath, "--restart" + (Program.Portable ? " --portable" : ""));
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
                "Ctrl+T\t\t\tNew tab\n" +
                "Ctrl+W / middle-click tab\tClose tab\n" +
                "Ctrl+Tab / Ctrl+Shift+Tab\tNext / previous tab\n" +
                "Ctrl+L / Alt+D / F4\tEdit address\n" +
                "Right-click a tab\t\tLock, duplicate, open in other pane\n" +
                "Drag a tab\t\tReorder tabs\n\n" +
                "Locked tabs always stay on their folder. Opening a folder from a locked tab opens it in a new tab.",
                "Keyboard shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- status bar

        public void UpdateStatus()
        {
            BrowserTab t = ActivePane == null ? null : ActivePane.ActiveTab;
            if (t == null || !t.Created) return;
            int all = t.Count(Native.SVGIO_ALLVIEW), sel = t.Count(Native.SVGIO_SELECTION);
            string s = all < 0 ? "" : all + (all == 1 ? " item" : " items");
            if (sel > 0) s += "     " + sel + " selected";
            if (FolderSizes)
            {
                if (sizeJob != null)
                    s += "     Folder size: " + (sizeJob.Errors > 0 ? "at least " : "") + Native.FormatBytes(sizeJob.TotalBytes) + (sizeJob.Finished ? "" : " (calculating…)");
                else if (sizeSkip != null)
                    s += "     Folder size: " + sizeSkip;
            }
            string sn = Shortcuts == null ? null : Shortcuts.NoticeText;
            if (!ShowShortcuts && sn != null) s += "     " + sn;
            if (statusLeft.Text != s) statusLeft.Text = s;

            string root = null;
            try { if (t.Address.Length > 2 && (t.Address[1] == ':' || t.Address.StartsWith(@"\\"))) root = Path.GetPathRoot(t.Address); } catch { }
            if ((root != freeRoot || unchecked(Environment.TickCount - freeTick) > 5000) && !freeBusy)
            {
                // A slow or disconnected network drive must not freeze the window: ask in the background.
                freeRoot = root;
                freeTick = Environment.TickCount;
                if (root == null) statusRight.Text = "";
                else
                {
                    freeBusy = true;
                    string askRoot = root;
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        ulong free, total, totalFree;
                        string f = "";
                        try
                        {
                            if (Native.GetDiskFreeSpaceEx(askRoot, out free, out total, out totalFree) && total > 0)
                                f = FormatSize(free) + " free of " + FormatSize(total) + " (" + (100 * free / total) + "%)";
                        }
                        catch { }
                        try { BeginInvoke((MethodInvoker)delegate { freeBusy = false; if (askRoot == freeRoot) statusRight.Text = f; }); } catch { }
                    });
                }
            }
            if (noticeText != null && unchecked(Environment.TickCount - noticeTick) < 6000) s += "     " + noticeText;
            if (stateSaveError != null) s += "     \u26A0 Settings couldn't be saved (" + stateSaveError + "); retrying.";
            if (statusLeft.Text != s) statusLeft.Text = s;
        }

        static string FormatSize(ulong b)
        {
            double gb = b / 1073741824.0;
            return gb >= 1000 ? (gb / 1024).ToString("0.0") + " TB" : gb.ToString("0.0") + " GB";
        }

        // ---- input routing

        bool IMessageFilter.PreFilterMessage(ref Message m)
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
            string fc = focus == IntPtr.Zero ? "" : Native.ClassName(focus);
            bool typing = fc == "Edit" || fc.Contains(".EDIT.") || fc.Contains("COMBOBOX") || fc == "ComboBox";
            if (typing)
            {
                bool addressKey = (ctrl && !alt && key == Keys.L) || (alt && !ctrl && key == Keys.D);
                if (!addressKey) return false;
            }
            Pane p = ActivePane;
            BrowserTab t = p.ActiveTab;
            if (ctrl && !alt && key == Keys.T) { p.NewTab(); return true; }
            if (ctrl && !alt && key == Keys.W) { if (t != null) p.CloseTab(t); return true; }
            if (ctrl && !alt && key == Keys.Tab) { p.CycleTab(shift ? -1 : 1); return true; }
            if (alt && !ctrl && key == Keys.Left) { p.Nav(Native.SBSP_NAVIGATEBACK); return true; }
            if (alt && !ctrl && key == Keys.Right) { p.Nav(Native.SBSP_NAVIGATEFORWARD); return true; }
            if (alt && !ctrl && key == Keys.Up) { p.Nav(Native.SBSP_PARENT); return true; }
            if (ctrl && !alt && !shift && key == Keys.H) { ToggleHidden(); return true; }
            if (alt && !ctrl && key == Keys.T) { SetShowTree(!ShowTree); return true; }
            if (alt && !ctrl && key == Keys.P) { SetShowPreview(!ShowPreview); return true; }
            if (alt && !ctrl && key == Keys.S) { SetShowShortcuts(!ShowShortcuts); return true; }
            if (alt && !ctrl && key >= Keys.D1 && key <= Keys.D4) { SetPaneCount(key - Keys.D0); return true; }
            if ((ctrl && !alt && key == Keys.L) || (alt && !ctrl && key == Keys.D) || (!ctrl && !alt && key == Keys.F4)) { p.FocusAddress(); return true; }
            bool renaming = ft != null && Native.ClassName(focus) == "Edit";
            if (ft != null && !renaming && !ctrl && !alt)
            {
                if (key == Keys.Back) { p.Nav(Native.SBSP_PARENT); return true; }
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
                    string locked = Native.Rebase(t.LockedFolder, oldPath, newPath);
                    if (locked != null) t.LockedFolder = locked;
                    string moved = Native.Rebase(t.Folder, oldPath, newPath);
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
            if (!Native.SameFolder(path, t.Folder) && !t.Navigate(path)) { SystemSounds.Beep.Play(); return; }
            SetActivePane(p);
            if (focusView) t.Activate();
        }

        // ---- persistence

        // The settings lines, or those of the .bak copy when the file is missing, unreadable or empty.
        static string[] ReadStateLines()
        {
            foreach (string file in new string[] { StateFile, StateFile + ".bak" })
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    string[] lines = File.ReadAllLines(file);
                    foreach (string l in lines) if (l.StartsWith("pane") && l.Contains(".tab=")) return lines;
                }
                catch { }
            }
            return null;
        }

        void LoadState()
        {
            List<string>[] tabs = { new List<string>(), new List<string>(), new List<string>(), new List<string>() };
            List<KeyValuePair<string, string>> legacyShortcuts = new List<KeyValuePair<string, string>>();
            int[] sel = { 0, 0, 0, 0 };
            try
            {
                string[] stateLines = ReadStateLines();
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
                                    Rectangle r = new Rectangle(wx, wy, ww, wh);
                                    foreach (Screen s in Screen.AllScreens)
                                        if (s.WorkingArea.IntersectsWith(r)) { StartPosition = FormStartPosition.Manual; Bounds = r; break; }
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
                            case "treewidth": if (int.TryParse(v, out n) && n >= 80) treeWidth = n; break;
                            case "previewwidth": if (int.TryParse(v, out n) && n >= 100) previewWidth = n; break;
                            case "shortcuts": ShowShortcuts = v != "0"; break;
                            case "shortcutsheight": if (int.TryParse(v, out n) && n >= 40) shortcutsHeight = n; break;
                            case "shortcutswidth": Shortcuts.WidthSetting = v; break;
                            case "shortcutsort": if (int.TryParse(v, out n)) Shortcuts.SortMode = n; break;
                            case "shortcut":
                                int bar = v.IndexOf('|');
                                if (bar > 0) legacyShortcuts.Add(new KeyValuePair<string, string>(v.Substring(0, bar), v.Substring(bar + 1)));
                                break;
                            case "theme": if (int.TryParse(v, out n) && n >= 0 && n <= 2) Theme.Mode = n; break;
                            case "activepane": if (int.TryParse(v, out n) && n >= 0 && n < Panes.Length) startPane = n; break;
                            default:
                                // pane<i>.active / pane<i>.tab for panes 0-3
                                if (k.Length == 12 && k.StartsWith("pane") && k.EndsWith(".active") && k[4] >= '0' && k[4] <= '3') int.TryParse(v, out sel[k[4] - '0']);
                                else if (k.Length == 9 && k.StartsWith("pane") && k.EndsWith(".tab") && k[4] >= '0' && k[4] <= '3') tabs[k[4] - '0'].Add(v);
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
                if (tabs[i].Count == 0) tabs[i].Add("U|" + defaults[i]);
                foreach (string s in tabs[i])
                    if (s.Length > 2 && s[1] == '|') Panes[i].AddTab(s.Substring(2), s[0] == 'L', false, true);
                Panes[i].Select(Math.Max(0, Math.Min(sel[i], Panes[i].Tabs.Count - 1)));
            }
        }

        void SaveState()
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
                sb.AppendLine("activepane=" + Array.IndexOf(Panes, ActivePane));
                for (int i = 0; i < Panes.Length; i++)
                {
                    sb.AppendLine("pane" + i + ".active=" + Panes[i].ActiveIndex);
                    foreach (BrowserTab t in Panes[i].Tabs)
                        sb.AppendLine("pane" + i + ".tab=" + (t.Locked ? "L|" + t.LockedFolder : "U|" + t.Folder));
                }
                // Shortcuts that couldn't be moved to the shared file yet stay here so they aren't lost.
                if (Shortcuts.PendingLegacy != null)
                    foreach (KeyValuePair<string, string> s in Shortcuts.PendingLegacy) sb.AppendLine("shortcut=" + s.Key + "|" + s.Value);
                Native.WriteAllTextAtomic(StateFile, sb.ToString());
                stateSaveError = null;
            }
            catch (Exception ex)
            {
                stateSaveError = ex.Message;
                Program.LogError(ex);
                saveTimer.Stop();
                saveTimer.Interval = 10000; // retry
                saveTimer.Start();
                UpdateStatus();
                return;
            }
            saveTimer.Interval = 1500;
        }
    }

    // ------------------------------------------------------------------ Per-user install / uninstall (no admin needed)

    static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OrclFileExplorer";
        const string OldUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DualPane";
        public static readonly string Version = FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

        static string FormatVersion(Version v) { return v.Major + "." + v.Minor + "." + v.Build.ToString("000"); }
        static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Program.AppName);
        static readonly string InstalledExe = Path.Combine(InstallDir, "orclfx.exe");
        // Where versions before 1.1 (named DualPane) were installed.
        static readonly string OldInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DualPane");
        static readonly string OldInstalledExe = Path.Combine(OldInstallDir, "DualPane.exe");
        static readonly string StartMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppName + ".lnk");
        static readonly string OldStartMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DualPane.lnk");

        public static bool IsRunningInstalledCopy()
        {
            return string.Equals(Path.GetFullPath(Application.ExecutablePath), InstalledExe, StringComparison.OrdinalIgnoreCase);
        }

        // Returns true when this copy should keep running.
        public static bool OfferInstall()
        {
            bool installed = File.Exists(InstalledExe);
            string msg = installed
                ? "Orcl File Explorer is already installed on this computer.\n\nYes: update the installed copy to version " + Version + " and start it\nNo: just run this copy without installing\nCancel: quit"
                : "Install Orcl File Explorer on this computer?\n\nIt installs for your Windows account only (no admin rights needed), adds it to the Start menu, and can be removed from Settings > Apps.\n\nYes: install and start\nNo: just run this copy without installing\nCancel: quit";
            DialogResult r = MessageBox.Show(msg, Program.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.No) return true;
            if (!Install()) return false;
            Process.Start(InstalledExe);
            return false;
        }

        public static void InstallFromMenu()
        {
            if (Install())
                MessageBox.Show(Program.AppName + " " + Version + " is installed. You'll find it in the Start menu; right-click it there to pin it to the taskbar.",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // "DualPane.exe --install": install or update without asking (errors are still shown).
        public static void InstallQuietly()
        {
            if (IsRunningInstalledCopy()) return; // already the installed copy; nothing to copy
            Install();
        }

        // Brings the already-running DualPane window to the front (used when it is launched a second time).
        public static void ActivateRunningCopy()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("orclfx"))
            {
                if (p.Id == me || p.MainWindowHandle == IntPtr.Zero) continue;
                Native.ShowWindow(p.MainWindowHandle, 9 /* SW_RESTORE */);
                Native.SetForegroundWindow(p.MainWindowHandle);
                return;
            }
        }

        static bool Install()
        {
            try
            {
                Directory.CreateDirectory(InstallDir);
                File.Copy(Application.ExecutablePath, InstalledExe, true);
            }
            catch (IOException)
            {
                MessageBox.Show("The installed Orcl File Explorer is running. Close it and try again.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Install failed: " + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            try { CreateShortcut(StartMenuLink, InstalledExe); } catch { }
            MigrateOldInstall();
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", Program.AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Orcl");
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
            return true;
        }

        // Removes the pre-1.1 "DualPane" install and points a taskbar pin made for it at the new exe.
        static void MigrateOldInstall()
        {
            try { if (File.Exists(OldStartMenuLink)) File.Delete(OldStartMenuLink); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(OldUninstallKey, false); } catch { }
            try
            {
                string pins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                if (Directory.Exists(pins))
                    foreach (string link in Directory.GetFiles(pins, "*.lnk"))
                    {
                        Type t = Type.GetTypeFromProgID("WScript.Shell");
                        object shell = Activator.CreateInstance(t);
                        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                        Type lt = lnk.GetType();
                        string target = lt.InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null) as string;
                        if (string.Equals(target, OldInstalledExe, StringComparison.OrdinalIgnoreCase))
                        {
                            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe });
                            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { InstallDir });
                            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe + ",0" });
                            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                        }
                        Marshal.ReleaseComObject(lnk);
                        Marshal.ReleaseComObject(shell);
                    }
            }
            catch { }
            // The old exe may still be running; delete what we can, the rest goes next time.
            try { if (File.Exists(OldInstalledExe)) File.Delete(OldInstalledExe); } catch { }
            try { if (Directory.Exists(OldInstallDir)) Directory.Delete(OldInstallDir, false); } catch { } // only if now empty
        }

        static void CreateShortcut(string link, string target)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Multi-pane file manager" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            Marshal.ReleaseComObject(lnk);
            Marshal.ReleaseComObject(shell);
        }

        public static void Uninstall()
        {
            if (MessageBox.Show("Uninstall Orcl File Explorer from this computer?", Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            bool wipeSettings = MessageBox.Show("Also delete your saved tabs and settings?", Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            try { File.Delete(StartMenuLink); } catch { }
            try { File.Delete(OldStartMenuLink); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (wipeSettings)
            {
                // Only DualPane's own files, and the folder only if it is the default one and now empty.
                // The shared shortcuts list in OneDrive is left alone: other computers use it.
                string dir = Path.GetDirectoryName(MainForm.StateFile);
                foreach (string name in new string[] { MainForm.StateFile, MainForm.StateFile + ".bak" })
                    try { File.Delete(name); } catch { }
                string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DualPane");
                if (string.Equals(dir, defaultDir, StringComparison.OrdinalIgnoreCase))
                    try { Directory.Delete(dir, false); } catch { }
            }
            MessageBox.Show("Orcl File Explorer was uninstalled. Its program file is removed a few seconds after this message closes.",
                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            // The running exe can't delete itself: delete just orclfx.exe a moment after we exit (retrying while it
            // is still in use), then the folder only if nothing else is left in it.
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                "/c for /l %i in (1,1,15) do (ping 127.0.0.1 -n 2 > nul & del /f /q \"" + InstalledExe + "\" 2> nul & if not exist \"" + InstalledExe + "\" (rmdir \"" + InstallDir + "\" 2> nul & exit))");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            try { Process.Start(psi); } catch { }
        }
    }
}
