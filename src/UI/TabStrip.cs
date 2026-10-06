// Orcl File Explorer: Owner-drawn tab strip of a file pane.
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
                if (t.Color != 0)
                {
                    // A colour label: a light tint of the tab and a bar along its bottom edge.
                    Color c = Theme.TabColor(t.Color);
                    Rectangle inner = new Rectangle(body.X + 1, body.Y + (isActive ? Native.Px(2) : 0), body.Width - 2, body.Height - (isActive ? Native.Px(2) : 0));
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(Theme.Dark ? 48 : 38, c))) g.FillRectangle(b, inner);
                    using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, body.X + 1, Height - Native.Px(3), body.Width - 2, Native.Px(3));
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

        // The tab under p. The rectangles are from the last paint; a tab closed since then (a quick second click
        // while one closes) must not give an index past the end of the tabs.
        int HitTest(Point p)
        {
            for (int i = 0; i < rects.Count && i < pane.Tabs.Count; i++) if (rects[i].Contains(p)) return i;
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
            if (drag >= 0 && (MouseButtons & MouseButtons.Left) != 0 && i >= 0 && i != drag && drag < rects.Count && drag < pane.Tabs.Count)
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
}
