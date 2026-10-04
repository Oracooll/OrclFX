// Orcl File Explorer: building a Windows Search results folder (what File Explorer's search box shows).
using System;
using System.Runtime.InteropServices;

namespace OrclFileExplorer
{
    [ComImport, Guid("a0ffbc28-5482-4366-be27-3e81e78e06c2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISearchFolderItemFactory
    {
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName);
        [PreserveSig] int SetFolderTypeID(Guid ftid);
        [PreserveSig] int SetFolderLogicalViewMode(int flvm);
        [PreserveSig] int SetIconSize(int iIconSize);
        [PreserveSig] int SetVisibleColumns(uint cVisibleColumns, [In, MarshalAs(UnmanagedType.LPArray)] PROPERTYKEY[] rgKey);
        [PreserveSig] int SetSortColumns(uint cSortColumns, IntPtr rgSortColumns);
        [PreserveSig] int SetGroupColumn(ref PROPERTYKEY keyGroup);
        [PreserveSig] int SetStacks(uint cStackKeys, IntPtr rgStackKeys);
        [PreserveSig] int SetScope(IShellItemArray psiaScope);
        [PreserveSig] int SetCondition([MarshalAs(UnmanagedType.IUnknown)] object pCondition);
        [PreserveSig] int GetShellItem(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int GetIDList(out IntPtr ppidl);
    }

    [ComImport, Guid("a879e3c4-af77-44fb-8f37-ebd1487cf920"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IQueryParserManager
    {
        [PreserveSig] int CreateLoadedParser([MarshalAs(UnmanagedType.LPWStr)] string pszCatalog, ushort langidForKeywords, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppQueryParser);
        [PreserveSig] int InitializeOptions(bool fUnderstandNQS, bool fAutoWildCard, IQueryParser pQueryParser);
        [PreserveSig] int SetOption(int option, IntPtr pOptionValue);
    }

    [ComImport, Guid("2EBDEE67-3505-43f8-9946-EA44ABC8E5B0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IQueryParser
    {
        [PreserveSig] int Parse([MarshalAs(UnmanagedType.LPWStr)] string pszInputString, IntPtr pCustomProperties, out IQuerySolution ppSolution);
        // SetOption, GetOption, SetMultiOption, GetSchemaProvider, RestateToString, ParsePropertyValue and
        // RestatePropertyValueToString follow; they aren't used.
    }

    // IQuerySolution derives from IConditionFactory (MakeNot, MakeAndOr, MakeLeaf, Resolve).
    [ComImport, Guid("D6EBC66B-8921-4193-AFDD-A1789FB7FF57"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IQuerySolution
    {
        [PreserveSig] int MakeNot(IntPtr pcSub, bool fSimplify, out IntPtr ppcResult);
        [PreserveSig] int MakeAndOr(int ct, IntPtr peuSubs, bool fSimplify, out IntPtr ppcResult);
        [PreserveSig] int MakeLeaf(IntPtr pszPropertyName, int cop, IntPtr pszValueType, IntPtr ppropvar, IntPtr pPropertyNameTerm, IntPtr pOperationTerm, IntPtr pValueTerm, bool fExpand, out IntPtr ppcResult);
        [PreserveSig] int Resolve([MarshalAs(UnmanagedType.IUnknown)] object pc, int sqro, IntPtr pstReferenceTime, [MarshalAs(UnmanagedType.IUnknown)] out object ppcResolved);
        [PreserveSig] int GetQuery([MarshalAs(UnmanagedType.IUnknown)] out object ppQueryNode, IntPtr ppMainType);
    }

    static class WindowsSearch
    {
        static readonly Guid CLSID_SearchFolderItemFactory = new Guid("14010e02-bbbd-41f0-88e3-eda371216584");
        static readonly Guid CLSID_QueryParserManager = new Guid("5088B39A-29B4-4d9d-8245-4EE289222F66");

        [DllImport("shell32.dll")]
        static extern int SHCreateShellItemArrayFromShellItem(IShellItem psi, ref Guid riid, out IShellItemArray ppv);
        [DllImport("kernel32.dll")]
        static extern ushort GetUserDefaultUILanguage();
        [DllImport("kernel32.dll")]
        static extern void GetLocalTime(IntPtr lpSystemTime);

        // A Windows Search results folder for text in root and its subfolders, parsed the way File Explorer's
        // search box parses it (words, *.pdf, kind:, size:, date: ...; words also match the start of longer
        // words). Throws with a readable message if Windows Search can't do it.
        public static IShellItem Create(string root, string text, string displayName)
        {
            object factoryObj = null, managerObj = null, parserObj = null, condition = null, resolved = null, item = null;
            IShellItem scopeItem = null;
            IShellItemArray scope = null;
            IQuerySolution solution = null;
            try
            {
                managerObj = Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_QueryParserManager));
                IQueryParserManager manager = (IQueryParserManager)managerObj;
                Guid iidParser = typeof(IQueryParser).GUID;
                Check(manager.CreateLoadedParser("SystemIndex", GetUserDefaultUILanguage(), ref iidParser, out parserObj), "the query parser");
                IQueryParser parser = (IQueryParser)parserObj;
                Check(manager.InitializeOptions(false, true, parser), "the query parser");
                Check(parser.Parse(text, IntPtr.Zero, out solution), "the search text");
                Check(solution.GetQuery(out condition, IntPtr.Zero), "the search text (query)");
                // Relative dates (date:this week) are resolved against the current time.
                IntPtr now = Marshal.AllocCoTaskMem(16); // SYSTEMTIME
                try
                {
                    GetLocalTime(now);
                    Check(solution.Resolve(condition, 0x40 /* SQRO_DONT_SPLIT_WORDS */, now, out resolved), "the search text (resolve)");
                }
                finally { Marshal.FreeCoTaskMem(now); }

                factoryObj = Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_SearchFolderItemFactory));
                ISearchFolderItemFactory factory = (ISearchFolderItemFactory)factoryObj;
                scopeItem = Native.ItemFromPath(root);
                if (scopeItem == null) throw new InvalidOperationException("the folder can't be opened");
                Guid iidArray = Native.IID_IShellItemArray;
                Check(SHCreateShellItemArrayFromShellItem(scopeItem, ref iidArray, out scope), "the folder");
                Check(factory.SetDisplayName(displayName), "the results folder");
                Check(factory.SetScope(scope), "the folder");
                Check(factory.SetCondition(resolved), "the search text");
                Guid iidItem = Native.IID_IShellItem;
                Check(factory.GetShellItem(ref iidItem, out item), "the results folder");
                IShellItem result = (IShellItem)item;
                item = null; // handed to the caller
                return result;
            }
            finally
            {
                foreach (object o in new object[] { factoryObj, managerObj, parserObj, condition, resolved, item, scopeItem, scope, solution })
                    if (o != null) try { Marshal.ReleaseComObject(o); } catch { }
            }
        }

        static void Check(int hr, string what)
        {
            if (hr != 0) throw new InvalidOperationException("Windows Search couldn't use " + what + " (0x" + hr.ToString("X8") + ")");
        }
    }
}
