// Orcl File Explorer: keeps the app running while the shell copies, moves or deletes files for it.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace OrclFileExplorer
{
    // Copies, moves and deletes started in an Explorer view (paste, drag and drop) run on shell threads inside
    // this process. File Explorer registers a "process reference" with SHSetInstanceExplorer: the shell holds it
    // while such work runs, and Explorer doesn't exit until it's released. Without it, closing the window (or a
    // restart for a theme change or an update) would end the process in the middle of a copy.
    //
    // The object is a minimal COM IUnknown built by hand, so its reference count can be read: 1 is ours, every
    // reference above that is work the shell still has running.
    static class ProcessReference
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int QueryInterfaceFn(IntPtr self, ref Guid iid, out IntPtr ppv);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate uint RefFn(IntPtr self);

        [DllImport("shell32.dll")] static extern void SHSetInstanceExplorer(IntPtr punk);
        [DllImport("shell32.dll")] static extern int SHGetInstanceExplorer(out IntPtr ppunk);

        static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
        // Kept in fields so the garbage collector never frees what native code calls.
        static QueryInterfaceFn queryInterface;
        static RefFn addRef, release;
        static IntPtr vtable, instance;
        static int refs = 1;

        public static void Register()
        {
            if (instance != IntPtr.Zero) return;
            queryInterface = QueryInterface;
            addRef = AddRef;
            release = Release;
            vtable = Marshal.AllocHGlobal(3 * IntPtr.Size);
            Marshal.WriteIntPtr(vtable, 0, Marshal.GetFunctionPointerForDelegate(queryInterface));
            Marshal.WriteIntPtr(vtable, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(addRef));
            Marshal.WriteIntPtr(vtable, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(release));
            instance = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(instance, vtable);
            SHSetInstanceExplorer(instance);
        }

        // How many shell operations (copies, moves, deletes ...) are still running for this app.
        public static int Busy { get { return Math.Max(0, Thread.VolatileRead(ref refs) - 1); } }

        static int QueryInterface(IntPtr self, ref Guid iid, out IntPtr ppv)
        {
            if (iid == IID_IUnknown) { ppv = self; AddRef(self); return 0; }
            ppv = IntPtr.Zero;
            return unchecked((int)0x80004002); // E_NOINTERFACE
        }

        static uint AddRef(IntPtr self) { return (uint)Interlocked.Increment(ref refs); }
        static uint Release(IntPtr self) { return (uint)Math.Max(1, Interlocked.Decrement(ref refs)); }

        // Test hook: what the shell does when it starts a long operation (the reference is held until released).
        internal static IntPtr TestTake()
        {
            IntPtr p;
            return SHGetInstanceExplorer(out p) == 0 ? p : IntPtr.Zero;
        }
    }
}
