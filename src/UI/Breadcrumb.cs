// OrclFX: the address bar's clickable path. Click a folder to open it (Ctrl+click or middle-click: in a
// new tab), click the arrow after it to pick one of its subfolders, click empty space to copy the address, and
// double-click empty space (or press Ctrl+L / F4) to type an address.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    class Breadcrumb : Control
    {
        readonly Pane pane;
        string address = "", title = "";
        List<KeyValuePair<string, string>> parts = new List<KeyValuePair<string, string>>();
        readonly List<Rectangle> textRects = new List<Rectangle>(), arrowRects = new List<Rectangle>();
        int firstShown;            // the parts before it don't fit and are listed by the « button
        Rectangle moreRect;
        int hoverText = -1, hoverArrow = -1;
        bool hoverMore;
        readonly ToolTip tip = new ToolTip();
        int openedAt = Environment.TickCount - 100000; // when a click last opened a folder

        public Breadcrumb(Pane p)
        {
            pane = p;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Default;
        }

        public void SetPath(string address, string title)
        {
            address = address ?? "";
            title = title ?? "";
            if (address == this.address && title == this.title) return;
            this.address = address;
            this.title = title;
            parts = PathParts.Split(address);
            // The old hit areas no longer match: nothing is clickable until the next paint lays the parts out.
            textRects.Clear();
            arrowRects.Clear();
            moreRect = Rectangle.Empty;
            firstShown = 0;
            hoverText = hoverArrow = -1;
            hoverMore = false;
            Invalidate();
        }

        Color TextColor { get { return pane.IsActivePane ? Theme.Text : Theme.TextDim; } }

        void DoLayout(Graphics g)
        {
            textRects.Clear();
            arrowRects.Clear();
            int pad = Native.Px(5), arrowW = Native.Px(16), x0 = Native.Px(4), more = Native.Px(24);
            int[] tw = new int[parts.Count];
            int total = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                tw[i] = TextRenderer.MeasureText(g, parts[i].Key, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + pad * 2;
                total += tw[i] + arrowW;
            }
            // Leave a little empty space at the end for clicking (copy) and double-clicking (type).
            int avail = Math.Max(0, Width - x0 - Native.Px(28));
            firstShown = 0;
            while (firstShown < parts.Count - 1 && total + (firstShown > 0 ? more : 0) > avail) { total -= tw[firstShown] + arrowW; firstShown++; }
            int x = x0;
            moreRect = Rectangle.Empty;
            if (firstShown > 0) { moreRect = new Rectangle(x, 0, more, Height); x += more; }
            for (int i = 0; i < parts.Count; i++)
            {
                if (i < firstShown) { textRects.Add(Rectangle.Empty); arrowRects.Add(Rectangle.Empty); continue; }
                int w = Math.Min(tw[i], Math.Max(Native.Px(30), Width - x - arrowW)); // a single long name is cut off
                textRects.Add(new Rectangle(x, 0, w, Height));
                x += w;
                arrowRects.Add(new Rectangle(x, 0, arrowW, Height));
                x += arrowW;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Color fg = TextColor;
            TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (parts.Count == 0)
            {
                // Not a folder on a disk or share (This PC, Find results ...): its name.
                TextRenderer.DrawText(g, title.Length > 0 ? title : address, Font, new Rectangle(Native.Px(4), 0, Width - Native.Px(8), Height), fg, f | TextFormatFlags.NoPadding);
                return;
            }
            DoLayout(g);
            using (SolidBrush hover = new SolidBrush(Theme.Hover))
            {
                if (!moreRect.IsEmpty)
                {
                    if (hoverMore) g.FillRectangle(hover, Inset(moreRect));
                    TextRenderer.DrawText(g, "«", Font, moreRect, fg, f | TextFormatFlags.HorizontalCenter);
                }
                for (int i = firstShown; i < parts.Count; i++)
                {
                    if (i == hoverText) g.FillRectangle(hover, Inset(textRects[i]));
                    if (i == hoverArrow) g.FillRectangle(hover, Inset(arrowRects[i]));
                    TextRenderer.DrawText(g, parts[i].Key, Font, textRects[i], fg, f | TextFormatFlags.HorizontalCenter);
                    TextRenderer.DrawText(g, "", Theme.SmallIconFont, arrowRects[i], Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        static Font bold;
        static Font BoldFont(Font f) { if (bold == null) bold = new Font(f, FontStyle.Bold); return bold; }

        Rectangle Inset(Rectangle r) { return new Rectangle(r.X, r.Y + Native.Px(1), r.Width, r.Height - Native.Px(2)); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int t = -1, a = -1;
            for (int i = firstShown; i < textRects.Count; i++)
            {
                if (textRects[i].Contains(e.Location)) t = i;
                if (arrowRects[i].Contains(e.Location)) a = i;
            }
            bool m = moreRect.Contains(e.Location);
            if (t != hoverText || a != hoverArrow || m != hoverMore)
            {
                hoverText = t; hoverArrow = a; hoverMore = m;
                Invalidate();
                if (t >= 0) tip.SetToolTip(this, parts[t].Value);
                else tip.SetToolTip(this, parts.Count > 0 ? "Click: copy the address · double-click: type an address (Ctrl+L)" : "Double-click: type an address (Ctrl+L)");
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverText = hoverArrow = -1; hoverMore = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Middle)
            {
                if (hoverText >= 0) pane.GoTo(parts[hoverText].Value, true);
                return;
            }
            if (e.Button != MouseButtons.Left || !ClientRectangle.Contains(e.Location)) return;
            // The second click of a double-click on a folder (which already opened it) does nothing more.
            if (unchecked(Environment.TickCount - openedAt) < SystemInformation.DoubleClickTime) return;
            if (!moreRect.IsEmpty && moreRect.Contains(e.Location)) { ShowHidden(); return; }
            for (int i = firstShown; i < textRects.Count; i++)
            {
                if (textRects[i].Contains(e.Location)) { openedAt = Environment.TickCount; pane.GoTo(parts[i].Value, (ModifierKeys & Keys.Control) != 0); return; }
                if (arrowRects[i].Contains(e.Location)) { ShowChildren(i); return; }
            }
            CopyAddress(e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || unchecked(Environment.TickCount - openedAt) < SystemInformation.DoubleClickTime) return;
            for (int i = firstShown; i < textRects.Count; i++) if (textRects[i].Contains(e.Location) || arrowRects[i].Contains(e.Location)) return;
            if (moreRect.Contains(e.Location)) return;
            pane.FocusAddress();
        }

        // Copies the folder's full path and says so in a small pop-up.
        void CopyAddress(Point at)
        {
            if (parts.Count == 0)
            {
                SystemSounds.Beep.Play();
                tip.Show("This location has no folder path to copy", this, at.X, Height + Native.Px(2), 1800);
                return;
            }
            try { Clipboard.SetDataObject(address, true, 5, 100); }
            catch (Exception ex) { tip.Show("Couldn't copy: " + ex.Message, this, at.X, Height + Native.Px(2), 2500); return; }
            Program.Trace("address copied: " + address);
            tip.Show("Copied: " + address, this, at.X, Height + Native.Px(2), 1800);
        }

        // The folders that don't fit, from the nearest one up.
        void ShowHidden()
        {
            ContextMenuStrip m = pane.Main.NewMenu();
            for (int i = firstShown - 1; i >= 0; i--)
            {
                string p = parts[i].Value;
                m.Items.Add(parts[i].Key, null, delegate { pane.GoTo(p, (ModifierKeys & Keys.Control) != 0); });
            }
            m.Show(this, new Point(moreRect.Left, Height));
        }

        // The subfolders of part i, read in the background (a slow drive or share mustn't freeze the window).
        void ShowChildren(int i)
        {
            string folder = parts[i].Value, next = i + 1 < parts.Count ? parts[i + 1].Value : null;
            Point at = new Point(arrowRects[i].Left, Height);
            bool hidden = Native.GetShowHidden();
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                List<string> dirs = new List<string>();
                string error = null;
                try
                {
                    foreach (string d in Directory.GetDirectories(folder))
                    {
                        if (!hidden)
                            try { if ((File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue; }
                            catch { continue; }
                        dirs.Add(d);
                    }
                    dirs.Sort(delegate(string a, string b) { return Native.CompareNatural(Path.GetFileName(a), Path.GetFileName(b)); });
                }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed || folder != (i < parts.Count ? parts[i].Value : null)) return; // moved on meanwhile
                        ContextMenuStrip m = pane.Main.NewMenu();
                        if (error != null) m.Items.Add(new ToolStripMenuItem(error) { Enabled = false });
                        else if (dirs.Count == 0) m.Items.Add(new ToolStripMenuItem("No subfolders") { Enabled = false });
                        int shown = 0;
                        foreach (string d in dirs)
                        {
                            if (++shown > 400) { m.Items.Add(new ToolStripMenuItem("… " + (dirs.Count - 400) + " more") { Enabled = false }); break; }
                            string path = d;
                            ToolStripMenuItem it = new ToolStripMenuItem(Path.GetFileName(d), null, delegate { pane.GoTo(path, (ModifierKeys & Keys.Control) != 0); });
                            if (next != null && Util.SameFolder(d, next)) it.Font = BoldFont(m.Font); // the one the path goes on into
                            m.Items.Add(it);
                        }
                        m.Show(this, at);
                    });
                }
                catch { }
            });
            th.IsBackground = true;
            th.Start();
        }
    }
}
