// OrclFX: touch gestures (Windows' WM_GESTURE, Windows 7 and later): pinch (zoom), one- and two-finger pan (with
// inertia after the finger lifts) and the two-finger tap. Taps and double-taps come as mouse clicks anyway.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    static class Gestures
    {
        public const int WM_GESTURE = 0x119, WM_GESTURENOTIFY = 0x11A;
        public const int GID_BEGIN = 1, GID_END = 2, GID_ZOOM = 3, GID_PAN = 4, GID_TWOFINGERTAP = 6;
        public const int GF_BEGIN = 1, GF_INERTIA = 2, GF_END = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct GESTUREINFO
        {
            public uint cbSize, dwFlags, dwID;
            public IntPtr hwndTarget;
            public short x, y;
            public uint dwInstanceID, dwSequenceID;
            public ulong ullArguments;
            public uint cbExtraArgs;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GESTURECONFIG { public uint dwID, dwWant, dwBlock; }

        [DllImport("user32.dll")] static extern bool GetGestureInfo(IntPtr h, ref GESTUREINFO info);
        [DllImport("user32.dll")] static extern bool CloseGestureInfoHandle(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetGestureConfig(IntPtr hwnd, uint reserved, uint count, GESTURECONFIG[] config, uint size);

        public struct Info
        {
            public int Id, Flags;
            public Point Screen;     // where: the fingers' middle (zoom), the finger (pan)
            public long Argument;    // zoom: the distance between the fingers
            public bool Begins { get { return (Flags & GF_BEGIN) != 0; } }
            public bool Ends { get { return (Flags & GF_END) != 0; } }
            public bool Inertia { get { return (Flags & GF_INERTIA) != 0; } }
        }

        // The gesture in a WM_GESTURE message (the handle is closed when it was read). False: not readable.
        public static bool Read(ref Message m, out Info info)
        {
            info = new Info();
            GESTUREINFO gi = new GESTUREINFO();
            gi.cbSize = (uint)Marshal.SizeOf(typeof(GESTUREINFO));
            try
            {
                if (!GetGestureInfo(m.LParam, ref gi)) return false;
            }
            catch (EntryPointNotFoundException) { return false; }
            info.Id = (int)gi.dwID;
            info.Flags = (int)gi.dwFlags;
            info.Screen = new Point(gi.x, gi.y);
            info.Argument = (long)(gi.ullArguments & 0xFFFFFFFF);
            return true;
        }

        // Handled gestures' handles are closed (DefWindowProc closes the others).
        public static void Done(ref Message m)
        {
            try { CloseGestureInfoHandle(m.LParam); } catch { }
            m.Result = IntPtr.Zero;
        }

        // Which gestures the window wants: pinch, pan with one finger in any direction (and its inertia), and
        // the two-finger tap. Called on WM_GESTURENOTIFY.
        public static void Configure(IntPtr hwnd)
        {
            GESTURECONFIG[] c = new GESTURECONFIG[3];
            c[0].dwID = GID_ZOOM; c[0].dwWant = 1;
            // GC_PAN | single finger vertically | horizontally | with inertia; no "gutter" (it locks to one axis).
            c[1].dwID = GID_PAN; c[1].dwWant = 0x1 | 0x2 | 0x4 | 0x10; c[1].dwBlock = 0x8;
            c[2].dwID = GID_TWOFINGERTAP; c[2].dwWant = 1;
            try { SetGestureConfig(hwnd, 0, (uint)c.Length, c, (uint)Marshal.SizeOf(typeof(GESTURECONFIG))); } catch { }
        }
    }
}
