// Orcl File Explorer: Flat owner-drawn button used in the title bar and pane headers.
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
}
