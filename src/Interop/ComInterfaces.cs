// OrclFX: Win32 structures and the Shell COM interfaces the app uses.
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
        [PreserveSig] int CreateViewWindow(IntPtr psvPrevious, IntPtr pfs, IntPtr psb, IntPtr prcView, out IntPtr phWnd);
        [PreserveSig] int DestroyViewWindow();
        [PreserveSig] int GetCurrentInfo(IntPtr pfs);
        [PreserveSig] int AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lparam);
        [PreserveSig] int SaveViewState();
        [PreserveSig] int SelectItem(IntPtr pidlItem, uint uFlags);
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

    // A Find results view asks its host (through IObjectWithSite / IServiceProvider) for an ICommDlgBrowser, so
    // the app can handle double-click and Enter itself, the way the Open and Save dialogs do.
    [ComImport, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IObjectWithSite
    {
        [PreserveSig] int SetSite([MarshalAs(UnmanagedType.IUnknown)] object pUnkSite);
        [PreserveSig] int GetSite(ref Guid riid, out IntPtr ppvSite);
    }

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport, Guid("000214F1-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICommDlgBrowser
    {
        [PreserveSig] int OnDefaultCommand([MarshalAs(UnmanagedType.IUnknown)] object ppshv);
        [PreserveSig] int OnStateChange([MarshalAs(UnmanagedType.IUnknown)] object ppshv, uint uChange);
        [PreserveSig] int IncludeObject([MarshalAs(UnmanagedType.IUnknown)] object ppshv, IntPtr pidl);
    }

    // The list behind IExplorerBrowser.FillFromObject: Find adds its results to it as they come in.
    [ComImport, Guid("96E5AE6D-6AE1-4b1c-900C-C6480EAA8828"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IResultsFolder
    {
        [PreserveSig] int AddItem(IShellItem psi);
        [PreserveSig] int AddIDList(IntPtr pidl, IntPtr ppidlAdded);
        [PreserveSig] int RemoveItem(IShellItem psi);
        [PreserveSig] int RemoveIDList(IntPtr pidl);
        [PreserveSig] int RemoveAll();
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
}
