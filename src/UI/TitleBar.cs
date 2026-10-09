// OrclFX: Custom title bar: icon, title, layout/view/theme buttons, caption buttons.
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
    class TitleBar : Control
    {
        readonly Form form;
        public readonly GlyphButton[] ThemeButtons = new GlyphButton[3];
        // Tree, 1 pane, 2 panes, 3 panes, 4 panes, Preview, Shortcuts
        public readonly GlyphButton[] LayoutButtons = new GlyphButton[7];
        // Details, List, Tiles, Content, Medium icons, Large icons (for the active pane)
        // All eight views, in the order Ctrl+mouse wheel goes through them (smallest to largest).
        public static readonly string[] ViewNames = { "Details", "List", "Tiles", "Content", "Small icons", "Medium icons", "Large icons", "Extra large icons" };
        public readonly GlyphButton[] ViewButtons = new GlyphButton[ViewNames.Length];
        readonly GlyphButton min, max, close;
        public readonly GlyphButton FindButton;
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
            // Left to right: Find | panes 1-4 | view modes | Tree, Preview, Shortcuts | themes | window buttons.
            // Left to right, before the panes: Find (magnifier) | ...
            FindButton = new GlyphButton("", "Find in this folder and its subfolders (Ctrl+F)", DockStyle.Right);
            FindButton.Width = Native.Px(36);
            Controls.Add(FindButton);
            Divider fgap = new Divider();
            fgap.Dock = DockStyle.Right;
            fgap.Width = Native.Px(17);
            Controls.Add(fgap);
            for (int i = 1; i <= 4; i++) Controls.Add(LayoutButtons[i]);
            Divider vgap1 = new Divider();
            vgap1.Dock = DockStyle.Right;
            vgap1.Width = Native.Px(17);
            Controls.Add(vgap1);
            for (int i = 0; i < ViewNames.Length; i++)
            {
                int kind = i;
                ViewButtons[i] = new GlyphButton("", "View: " + ViewNames[i] + " (Ctrl+Shift+" + new[] { 6, 5, 7, 8, 4, 3, 2, 1 }[i] + "; Ctrl+mouse wheel goes through all views)\nRight-click: make it the default view for every folder (green tick); again to stop", DockStyle.Right);
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
                    case 4: // small icons: three rows of [dot + short line] in two columns
                        for (int k = 0; k < 6; k++)
                        {
                            int cx = x + (k % 2) * (w / 2 + 1), yy = y + 1 + (k / 2) * (h - 3) / 2;
                            g.FillRectangle(b, cx, yy - 1, 3, 3);
                            g.DrawLine(p, cx + 4, yy, cx + w / 2 - 2, yy);
                        }
                        break;
                    case 5: // medium icons: 3 x 2 squares
                        for (int k = 0; k < 6; k++)
                        {
                            int s = Math.Max(3, w / 4);
                            g.DrawRectangle(p, x + (k % 3) * (w - s - 1) / 2, y + (k / 3) * (h - s - 1), s, s);
                        }
                        break;
                    case 6: // large icons: 2 big squares
                        {
                            int s = Math.Min(w / 2 - 2, h - 2);
                            g.DrawRectangle(p, x, y + (h - s) / 2, s, s);
                            g.DrawRectangle(p, x + w - s - 1, y + (h - s) / 2, s, s);
                        }
                        break;
                    case 7: // extra large icons: one big picture (a frame with a hill and a sun)
                        {
                            int s = h - 1, x0 = x + (w - s) / 2;
                            g.DrawRectangle(p, x0, y, s, s);
                            g.FillPolygon(b, new Point[] { new Point(x0 + 1, y + s), new Point(x0 + s / 3, y + s / 2), new Point(x0 + s * 2 / 3, y + s - 2), new Point(x0 + s, y + s) });
                            g.FillRectangle(b, x0 + s - 4, y + 2, 2, 2);
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
            if (iconHot) using (SolidBrush b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, IconRect);
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

        // The app icon at the left opens the main menu.
        public event EventHandler IconClick;
        bool iconHot;

        // The icon with a little room around it, kept clear of the window's top edge so resizing still works there.
        public Rectangle IconRect
        {
            get
            {
                int s = Native.Px(16), pad = Native.Px(6);
                return new Rectangle(Native.Px(12) - pad, (Height - s) / 2 - pad, s + 2 * pad, s + 2 * pad);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hot = IconRect.Contains(e.Location);
            if (hot != iconHot) { iconHot = hot; Cursor = hot ? Cursors.Hand : Cursors.Default; Invalidate(IconRect); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (iconHot) { iconHot = false; Cursor = Cursors.Default; Invalidate(IconRect); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && IconRect.Contains(e.Location) && IconClick != null) IconClick(this, EventArgs.Empty);
        }

        // Let the empty parts of the bar act as the window caption (drag, double-click, system menu);
        // only the icon takes clicks itself.
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST)
            {
                Point p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                if (!IconRect.Contains(p)) { m.Result = (IntPtr)HTTRANSPARENT; return; }
            }
            base.WndProc(ref m);
        }
    }
}
