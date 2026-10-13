// OrclFX: leaving the Space Viewer's full screen whatever it shows. A PDF, an Office file and the like are shown by
// Windows' preview handlers, which run in another program: once they have the keyboard and the mouse, the viewer's
// own keys and clicks never reach it. So:
// - the title bar (with its full-screen and close buttons) slides in over the top when the pointer touches the top
//   edge of the screen (and for a moment when full screen starts, as a hint), and
// - Esc is caught on its way to the other program while the viewer is the window in front (only Esc: the arrows,
//   Space and the rest still page through the PDF).
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    partial class QuickLook
    {
        readonly Timer barWatch = new Timer();
        bool barShown;          // the title bar is over the top of the full screen
        int barHideAt;          // when the pointer left it (it goes a moment later)
        int barHintUntil;       // shown at the start of full screen until then

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

        void FullScreenBarStart()
        {
            barWatch.Interval = 100;
            barWatch.Tick += delegate { WatchBar(); };
        }

        // Called when full screen starts or ends.
        void FullScreenBar(bool on)
        {
            if (on)
            {
                bar.Dock = DockStyle.None;
                barHintUntil = Environment.TickCount + 2000;
                ShowBar(true);
                barWatch.Start();
            }
            else
            {
                barWatch.Stop();
                barShown = false;
                bar.Dock = DockStyle.Top;
            }
        }

        void ShowBar(bool show)
        {
            if (show == barShown && bar.Visible == show) return;
            barShown = show;
            if (show)
            {
                bar.SetBounds(0, 0, ClientSize.Width, bar.Height);
                bar.Visible = true;
                bar.BringToFront();
            }
            else bar.Visible = false;
        }

        void WatchBar()
        {
            if (!fullScreen || IsDisposed) { barWatch.Stop(); return; }
            // Only while the viewer is the window in front (also when a preview handler inside it has the keyboard).
            bool front = GetForegroundWindow() == Handle;
            Point p = Cursor.Position;
            Rectangle screen = Bounds;
            bool atTop = front && screen.Contains(p) && p.Y <= screen.Top + Native.Px(4);
            bool overBar = barShown && screen.Contains(p) && p.Y < screen.Top + bar.Height + Native.Px(24);
            if (atTop) { ShowPointer(); ShowBar(true); barHideAt = 0; return; }
            if (!barShown) return;
            if (overBar || unchecked(Environment.TickCount - barHintUntil) < 0) { barHideAt = 0; return; }
            if (barHideAt == 0) { barHideAt = Environment.TickCount + 600; return; }
            if (unchecked(Environment.TickCount - barHideAt) >= 0) ShowBar(false);
        }

        // ---- Esc on its way to a preview handler

        Native.HookProc keyHookProc;
        IntPtr keyHook;

        void KeyHookStart()
        {
            keyHookProc = KeyHook;
            try { keyHook = Native.SetWindowsHookEx(13, keyHookProc, GetModuleHandle(null), 0); } // WH_KEYBOARD_LL
            catch { keyHook = IntPtr.Zero; }
        }

        void KeyHookStop()
        {
            if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
            keyHook = IntPtr.Zero;
        }

        IntPtr KeyHook(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                const int WM_KEYDOWN = 0x100, VK_ESCAPE = 0x1B;
                if (code == 0 && (int)wParam == WM_KEYDOWN && Marshal.ReadInt32(lParam) == VK_ESCAPE && EscGoesElsewhere())
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed) return;
                        Program.Trace("quick look: Esc taken back from the preview");
                        if (fullScreen) ToggleFullScreen(); else Close();
                    });
                    return (IntPtr)1; // swallowed: the preview handler doesn't get it
                }
            }
            catch { }
            return Native.CallNextHookEx(keyHook, code, wParam, lParam);
        }

        // The viewer is the window in front, but the keyboard is in another program's window inside it (a preview
        // handler): Esc would go there. (When the keyboard is in the app, its own key handling has Esc.)
        bool EscGoesElsewhere()
        {
            if (IsDisposed || !IsHandleCreated || GetForegroundWindow() != Handle) return false;
            uint pid;
            GetWindowThreadProcessId(Handle, out pid);
            // (Thread 0: the window in front's input, which the preview handler's window shares with this one.)
            GUITHREADINFO gi = new GUITHREADINFO();
            gi.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
            if (!GetGUIThreadInfo(0, ref gi) || gi.hwndFocus == IntPtr.Zero) return false;
            uint focusPid;
            GetWindowThreadProcessId(gi.hwndFocus, out focusPid);
            return focusPid != pid;
        }

        // Test hooks.
        internal string TestBar { get { return "title bar " + (bar.Visible ? "shown" : "hidden") + " at " + bar.Bounds + ", docked " + bar.Dock; } }
        internal bool TestEscElsewhere { get { return EscGoesElsewhere(); } }
        internal void TestPointerAtTop() { barHintUntil = 0; ShowBar(true); barHideAt = 0; }
    }
}
