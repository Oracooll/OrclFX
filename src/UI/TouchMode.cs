// OrclFX: touch mode (the hand button in the title bar; settings key "touch"): everything tapped is about half as
// big again (buttons, tabs, the address bar, dividers, menus, rows in the tree and the Shortcuts pane), sized for a
// finger. It is read before the window is built and switching it rebuilds the window (as switching light and dark
// does). Dragging with a finger (tabs, dividers, the Shortcuts pane's title strip) works in either mode: AllowDrag
// tells Windows to pass a finger's drag on as a mouse drag instead of scrolling.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    static class TouchMode
    {
        public static bool On;

        // A size of something to tap or grab (96-dpi pixels): half as big again in touch mode.
        public static int Hit(int v) { return Native.Px(On ? (v * 3 + 1) / 2 : v); }

        // A divider's thickness.
        public static int Bar { get { return Native.Px(On ? 12 : 5); } }

        // The setting from the settings file's lines (before the window is built).
        public static void ReadEarly(string[] lines)
        {
            if (lines == null) return;
            foreach (string l in lines)
                if (l.StartsWith("touch=", StringComparison.Ordinal)) On = l.Substring(6).Trim() == "1";
        }

        // ---- finger drags

        [StructLayout(LayoutKind.Sequential)]
        struct GESTURECONFIG { public uint dwID, dwWant, dwBlock; }
        [DllImport("user32.dll")] static extern bool SetGestureConfig(IntPtr hwnd, uint reserved, uint count, GESTURECONFIG[] config, uint size);

        // Windows turns a finger's drag into scrolling (a "pan") unless the window says no: then it comes as a mouse
        // drag, which the control already handles.
        public static void AllowDrag(Control c)
        {
            new DragWindow(c);
        }

        class DragWindow : NativeWindow
        {
            public DragWindow(Control c)
            {
                if (c.IsHandleCreated) AssignHandle(c.Handle);
                c.HandleCreated += delegate { if (Handle != c.Handle) { if (Handle != IntPtr.Zero) ReleaseHandle(); AssignHandle(c.Handle); } };
                c.HandleDestroyed += delegate { if (Handle != IntPtr.Zero) ReleaseHandle(); };
            }

            protected override void WndProc(ref Message m)
            {
                // (Only a touch on this window itself: the notice also comes up from the windows inside it.)
                if (m.Msg == Gestures.WM_GESTURENOTIFY && Marshal.ReadIntPtr(m.LParam, 8) == Handle)
                {
                    GESTURECONFIG[] c = new GESTURECONFIG[1];
                    c[0].dwID = Gestures.GID_PAN; c[0].dwWant = 0; c[0].dwBlock = 1; // GC_PAN: no panning
                    try { SetGestureConfig(Handle, 0, 1, c, (uint)Marshal.SizeOf(typeof(GESTURECONFIG))); } catch { }
                }
                base.WndProc(ref m);
            }
        }

        // ---- menus

        // Taller menu items in touch mode (and in their submenus).
        public static void Menu(ToolStripDropDown m)
        {
            if (!On) return;
            m.Opening += delegate { Pad(m.Items); };
        }

        static void Pad(ToolStripItemCollection items)
        {
            foreach (ToolStripItem i in items)
            {
                if (i is ToolStripSeparator) continue;
                // A fixed, taller height (padding would push the text to the top of the item).
                if (i.AutoSize)
                {
                    Size pref = i.GetPreferredSize(Size.Empty);
                    i.AutoSize = false;
                    i.Size = new Size(pref.Width, pref.Height + Native.Px(14));
                }
                ToolStripMenuItem mi = i as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems) Pad(mi.DropDownItems);
            }
        }

        // ---- rows

        // An icon centred in a taller picture: list rows take their height from their pictures.
        public static Image Padded(Icon icon, Size cell)
        {
            Bitmap b = new Bitmap(cell.Width, cell.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            using (Bitmap ib = icon.ToBitmap())
                g.DrawImage(ib, new Rectangle((cell.Width - ib.Width) / 2, (cell.Height - ib.Height) / 2, ib.Width, ib.Height));
            return b;
        }

        public const int TVM_SETITEMHEIGHT = 0x111B;

        // ---- the title bar's button: a hand with its index finger up

        public static void DrawHand(Graphics g, Rectangle r, Color c)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height + 3) / 16f, x = r.X + (r.Width - 16 * s) / 2, y = r.Y + (r.Height - 16 * s) / 2 - 1.5f * s;
            using (GraphicsPath p = new GraphicsPath())
            using (Pen pen = new Pen(c, Math.Max(1f, 1.2f * s)))
            {
                pen.LineJoin = LineJoin.Round;
                Func<float, float, PointF> P = delegate(float px, float py) { return new PointF(x + px * s, y + py * s); };
                // The index finger (up), the folded fingers, the thumb and the wrist.
                p.AddLine(P(5.5f, 9.5f), P(5.5f, 2.5f));
                p.AddArc(x + 5.5f * s, y + 1f * s, 2.6f * s, 2.6f * s, 180, 180);
                p.AddLine(P(8.1f, 2.3f), P(8.1f, 8.2f));
                p.AddArc(x + 8.1f * s, y + 6.9f * s, 2.3f * s, 2.3f * s, 180, 180);
                p.AddArc(x + 10.4f * s, y + 7.5f * s, 2.3f * s, 2.3f * s, 180, 180);
                p.AddLine(P(12.7f, 8.6f), P(12.7f, 12.5f));
                p.AddLine(P(12.7f, 12.5f), P(11.5f, 15.5f));
                p.AddLine(P(11.5f, 15.5f), P(6.2f, 15.5f));
                p.AddLine(P(6.2f, 15.5f), P(3.2f, 11.3f));
                p.AddArc(x + 2.3f * s, y + 8.6f * s, 2.4f * s, 2.4f * s, 200, 140);
                p.CloseFigure();
                g.DrawPath(pen, p);
            }
            g.SmoothingMode = old;
        }
    }
}
