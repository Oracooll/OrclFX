// OrclFX: where the Shortcuts pane sits, like the Windows taskbar: at the bottom (default), left, right or top of the
// window, outside the Tree and Preview panes. It moves by dragging its title strip to an edge (a see-through outline
// shows where it lands; Esc cancels), or from its menu.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    partial class MainForm
    {
        public static readonly string[] ShortcutsDockNames = { "Bottom", "Left", "Right", "Top" };
        int shortcutsDock;              // 0 bottom, 1 left, 2 right, 3 top (settings key "shortcutsdock")
        int shortcutsSideWidth = 240;   // the pane's width at the left or right (96-dpi pixels)

        public int ShortcutsDock { get { return shortcutsDock; } }
        bool ShortcutsSideways { get { return shortcutsDock == 1 || shortcutsDock == 2; } }
        bool ShortcutsFirst { get { return shortcutsDock == 1 || shortcutsDock == 3; } }   // in vsplit's Panel1

        // The pane's current size across its edge (height at the top or bottom, width at the sides).
        int ShortcutsSize
        {
            get
            {
                SplitterPanel p = ShortcutsFirst ? vsplit.Panel1 : vsplit.Panel2;
                return ShortcutsSideways ? p.Width : p.Height;
            }
        }

        public void SetShortcutsDock(int dock)
        {
            if (dock < 0 || dock > 3 || dock == shortcutsDock) return;
            RememberShortcutsSize();
            shortcutsDock = dock;
            ApplyShortcutsDock();
            StateChanged();
            Notice("Shortcuts pane: " + ShortcutsDockNames[dock].ToLowerInvariant());
        }

        void RememberShortcutsSize()
        {
            if (!Ready || !ShowShortcuts) return;
            int px = (int)Math.Round(ShortcutsSize * 100.0 / Native.Px(100));
            if (px < 40) return;
            if (ShortcutsSideways) shortcutsSideWidth = px; else shortcutsHeight = px;
        }

        // Puts the Shortcuts pane in its place: vsplit holds it on one side and everything else on the other.
        void ApplyShortcutsDock()
        {
            vsplit.SuspendLayout();
            try
            {
                vsplit.Panel1MinSize = 0;
                vsplit.Panel2MinSize = 0;
                vsplit.Panel1Collapsed = false;
                vsplit.Panel2Collapsed = false;
                SplitterPanel mine = ShortcutsFirst ? vsplit.Panel1 : vsplit.Panel2, other = ShortcutsFirst ? vsplit.Panel2 : vsplit.Panel1;
                if (Shortcuts.Parent != mine || treeSplit.Parent != other)
                {
                    vsplit.Panel1.Controls.Clear();
                    vsplit.Panel2.Controls.Clear();
                    vsplit.Orientation = ShortcutsSideways ? Orientation.Vertical : Orientation.Horizontal;
                    mine.Controls.Add(Shortcuts);
                    other.Controls.Add(treeSplit);
                }
                vsplit.FixedPanel = ShortcutsFirst ? FixedPanel.Panel1 : FixedPanel.Panel2;
                Shortcuts.SetSideways(ShortcutsSideways);
                int total = ShortcutsSideways ? vsplit.Width : vsplit.Height;
                int size = Native.Px(ShortcutsSideways ? shortcutsSideWidth : shortcutsHeight);
                size = Math.Max(Native.Px(60), Math.Min(size, total - Native.Px(220)));
                try { if (size > 0) vsplit.SplitterDistance = ShortcutsFirst ? size : total - size - vsplit.SplitterWidth; }
                catch { }
                try
                {
                    if (ShortcutsFirst) vsplit.Panel1MinSize = Native.Px(50); else vsplit.Panel2MinSize = Native.Px(50);
                }
                catch { }
                if (ShortcutsFirst) vsplit.Panel1Collapsed = !ShowShortcuts; else vsplit.Panel2Collapsed = !ShowShortcuts;
            }
            finally { vsplit.ResumeLayout(true); }
        }

        // ---- moving it by dragging its title strip

        Form dockPreview;
        int dragTarget = -1;

        // The edge of the window's content the pointer is near (within its snap zone: a quarter of the window's
        // height or width, 80-200 pixels), and the strip the pane would take there; -1 when it's near no edge.
        int EdgeAt(Point screen, out Rectangle strip)
        {
            Rectangle area = vsplit.RectangleToScreen(vsplit.ClientRectangle);
            strip = Rectangle.Empty;
            int dLeft = screen.X - area.Left, dRight = area.Right - screen.X, dTop = screen.Y - area.Top, dBottom = area.Bottom - screen.Y;
            int edge = 0, best = dBottom;
            if (dLeft < best) { edge = 1; best = dLeft; }
            if (dRight < best) { edge = 2; best = dRight; }
            if (dTop < best) { edge = 3; best = dTop; }
            int zone = Math.Max(Native.Px(80), Math.Min(Native.Px(200), (edge == 1 || edge == 2 ? area.Width : area.Height) / 4));
            if (best > zone) return -1;
            int across = Native.Px(edge == 1 || edge == 2 ? shortcutsSideWidth : shortcutsHeight);
            if (edge == shortcutsDock && ShowShortcuts) across = ShortcutsSize;
            across = Math.Max(Native.Px(60), Math.Min(across, (edge == 1 || edge == 2 ? area.Width : area.Height) - Native.Px(220)));
            switch (edge)
            {
                case 1: strip = new Rectangle(area.Left, area.Top, across, area.Height); break;
                case 2: strip = new Rectangle(area.Right - across, area.Top, across, area.Height); break;
                case 3: strip = new Rectangle(area.Left, area.Top, area.Width, across); break;
                default: strip = new Rectangle(area.Left, area.Bottom - across, area.Width, across); break;
            }
            return edge;
        }

        public void ShortcutsDragMove(Point screen)
        {
            if (Native.KeyDown(0x1B)) { ShortcutsDragEnd(false); return; } // Esc
            Rectangle strip;
            dragTarget = EdgeAt(screen, out strip);
            if (dockPreview == null)
            {
                dockPreview = new DockOutline();
                dockPreview.BackColor = Theme.Accent;
            }
            // Near no edge: no outline, and letting go there leaves the pane where it is.
            if (dragTarget < 0) { if (dockPreview.Visible) dockPreview.Hide(); return; }
            dockPreview.Bounds = strip;
            if (!dockPreview.Visible) dockPreview.Show(this);
        }

        // apply: the mouse was let go (true) or the drag was cancelled.
        public void ShortcutsDragEnd(bool apply)
        {
            if (dockPreview != null) { dockPreview.Close(); dockPreview.Dispose(); dockPreview = null; }
            int target = dragTarget;
            dragTarget = -1;
            if (apply && target >= 0) SetShortcutsDock(target);
        }

        public bool ShortcutsDragging { get { return dockPreview != null; } }

        // Test hook use: where letting go at this point would put the pane (-1: nowhere).
        internal int TestEdgeAt(Point screen) { Rectangle r; return EdgeAt(screen, out r); }

        // A see-through outline of where the pane will go; it never takes the focus or the mouse.
        class DockOutline : Form
        {
            public DockOutline()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Opacity = 0.35;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000 | 0x20 | 0x80; // WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW
                    return cp;
                }
            }
        }
    }
}
