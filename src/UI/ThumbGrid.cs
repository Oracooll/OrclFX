// OrclFX: the Space Viewer's thumbnail pane: every picture of the folder as a grid. Ctrl+mouse wheel
// makes the thumbnails bigger or smaller (64-512 pixels); they are made in the background, the visible ones first,
// and only a limited number is kept in memory.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    class ThumbGrid : Control
    {
        public static readonly int[] Sizes = { 64, 80, 96, 128, 160, 192, 256, 320, 384, 448, 512 };
        const long MemoryBudget = 320L * 1024 * 1024;

        List<string> items = new List<string>();
        int selected = -1;
        int thumbSize = 160;
        int scroll;                 // pixels scrolled down
        readonly VScrollBar bar = new VScrollBar();
        // Thumbnails made so far, with the size they were made at (an older size is drawn scaled until the new one
        // arrives).
        readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> cacheSize = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The background maker: one STA thread taking the newest wanted list.
        readonly object gate = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        List<string> wanted = new List<string>();
        int wantedSize;
        int generation;
        readonly Dictionary<string, int> versions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // bumped when a picture changes
        volatile bool quit;
        Thread worker;

        public event Action<string> ItemClicked;
        public event Action<int> ThumbSizeChanged;

        public ThumbGrid()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            bar.Dock = DockStyle.Right;
            bar.Scroll += delegate { SetScroll(bar.Value); };
            bar.HandleCreated += delegate { Native.SetWindowTheme(bar.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null); };
            Controls.Add(bar);
        }

        public int ThumbSize
        {
            get { return thumbSize; }
            set
            {
                int v = Math.Max(Sizes[0], Math.Min(Sizes[Sizes.Length - 1], value));
                if (v == thumbSize) return;
                // Keep the first visible row's item in view.
                int first = Columns > 0 ? scroll / CellH * Columns : 0;
                thumbSize = v;
                UpdateBar();
                if (Columns > 0) SetScroll(first / Columns * CellH);
                Invalidate();
                RequestVisible();
                if (ThumbSizeChanged != null) ThumbSizeChanged(v);
            }
        }

        public IList<string> Items { get { return items; } }
        public string Selected { get { return selected >= 0 && selected < items.Count ? items[selected] : null; } }

        public void SetItems(List<string> list, string select)
        {
            items = list;
            selected = select == null ? -1 : items.FindIndex(delegate(string p) { return string.Equals(p, select, StringComparison.OrdinalIgnoreCase); });
            scroll = 0;
            UpdateBar();
            EnsureVisible(selected);
            Invalidate();
            RequestVisible();
        }

        // Marks the item (if it is in the grid) and scrolls to it.
        public void Select(string path)
        {
            int i = path == null ? -1 : items.FindIndex(delegate(string p) { return string.Equals(p, path, StringComparison.OrdinalIgnoreCase); });
            if (i == selected) return;
            selected = i;
            EnsureVisible(i);
            Invalidate();
        }

        public void Remove(string path)
        {
            int i = items.FindIndex(delegate(string p) { return string.Equals(p, path, StringComparison.OrdinalIgnoreCase); });
            if (i < 0) return;
            items.RemoveAt(i);
            Forget(path);
            if (selected > i) selected--;
            else if (selected == i) selected = -1;
            UpdateBar();
            Invalidate();
            RequestVisible();
        }

        // The picture changed (turned): its thumbnail is made again.
        public void Refresh(string path)
        {
            lock (gate) { int v; versions.TryGetValue(path, out v); versions[path] = v + 1; } // one being made now is out of date
            Forget(path);
            failed.Remove(path);
            Invalidate();
            RequestVisible();
        }

        void Forget(string path)
        {
            Bitmap b;
            if (cache.TryGetValue(path, out b)) { b.Dispose(); cache.Remove(path); cacheSize.Remove(path); }
        }

        // The item d places away in the grid (d = ±1 for left/right, ±Columns for up/down); null at the ends.
        // clamp: stop at the first or last item (Page Up / Page Down) instead of null.
        public string Neighbour(string from, int d, bool clamp)
        {
            int i = from == null ? -1 : items.FindIndex(delegate(string p) { return string.Equals(p, from, StringComparison.OrdinalIgnoreCase); });
            if (i < 0) return items.Count > 0 ? items[0] : null;
            int n = i + d;
            if (clamp) n = Math.Max(0, Math.Min(items.Count - 1, n));
            return n >= 0 && n < items.Count && n != i ? items[n] : null;
        }

        public int VisibleRows { get { return Math.Max(1, ClientSize.Height / CellH); } }

        // ---- layout

        int Gap { get { return Native.Px(6); } }
        int LabelH { get { return TextRenderer.MeasureText("Ag", Font).Height + Native.Px(2); } }
        int CellW { get { return thumbSize + Gap * 2; } }
        int CellH { get { return thumbSize + LabelH + Gap * 2; } }
        int AreaW { get { return Math.Max(1, ClientSize.Width - (bar.Visible ? bar.Width : 0)); } }
        public int Columns { get { return Math.Max(1, AreaW / CellW); } }
        int Rows { get { return (items.Count + Columns - 1) / Columns; } }
        int Left0 { get { return Math.Max(0, (AreaW - Columns * CellW) / 2); } }

        void UpdateBar()
        {
            int total = Rows * CellH;
            bool need = total > ClientSize.Height;
            if (bar.Visible != need) bar.Visible = need;
            bar.Minimum = 0;
            bar.LargeChange = Math.Max(1, ClientSize.Height);
            bar.SmallChange = Math.Max(1, CellH / 3);
            bar.Maximum = Math.Max(0, total - 1);
            SetScroll(scroll);
        }

        void SetScroll(int v)
        {
            int max = Math.Max(0, Rows * CellH - ClientSize.Height);
            v = Math.Max(0, Math.Min(max, v));
            if (bar.Visible && bar.Value != v) bar.Value = Math.Min(v, bar.Maximum);
            if (v == scroll) return;
            scroll = v;
            Invalidate();
            RequestVisible();
        }

        void EnsureVisible(int i)
        {
            if (i < 0) return;
            int top = i / Columns * CellH;
            if (top < scroll) SetScroll(top);
            else if (top + CellH > scroll + ClientSize.Height) SetScroll(top + CellH - ClientSize.Height);
        }

        Rectangle CellRect(int i)
        {
            return new Rectangle(Left0 + i % Columns * CellW, i / Columns * CellH - scroll, CellW, CellH);
        }

        int HitTest(Point p)
        {
            if (p.X >= AreaW) return -1;
            int col = (p.X - Left0) / CellW, row = (p.Y + scroll) / CellH;
            if (p.X < Left0 || col >= Columns) return -1;
            int i = row * Columns + col;
            return i >= 0 && i < items.Count ? i : -1;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateBar();
            EnsureVisible(selected);
            RequestVisible();
        }

        // ---- painting

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (items.Count == 0)
            {
                TextRenderer.DrawText(g, "No pictures in this folder", Font, ClientRectangle, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            int first = scroll / CellH * Columns, last = Math.Min(items.Count - 1, ((scroll + ClientSize.Height) / CellH + 1) * Columns - 1);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            for (int i = first; i <= last; i++)
            {
                Rectangle c = CellRect(i);
                if (i == selected)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, c.X + 1, c.Y + 1, c.Width - 2, c.Height - 2);
                    using (Pen p = new Pen(Theme.Accent, Native.Px(2))) g.DrawRectangle(p, c.X + 2, c.Y + 2, c.Width - 4, c.Height - 4);
                }
                Rectangle box = new Rectangle(c.X + Gap, c.Y + Gap, thumbSize, thumbSize);
                Bitmap bmp;
                if (cache.TryGetValue(items[i], out bmp))
                {
                    Size s = Pictures.Fit(bmp.Size, box.Size);
                    if (bmp.Width > s.Width || bmp.Height > s.Height || cacheSize[items[i]] != thumbSize)
                    {
                        // Made at another size: scaled to this one (and never bigger than the box).
                        double k = Math.Min((double)box.Width / bmp.Width, (double)box.Height / bmp.Height);
                        if (cacheSize[items[i]] < thumbSize) k = Math.Min(k, (double)thumbSize / cacheSize[items[i]]);
                        s = new Size(Math.Max(1, (int)(bmp.Width * k)), Math.Max(1, (int)(bmp.Height * k)));
                    }
                    g.DrawImage(bmp, new Rectangle(box.X + (box.Width - s.Width) / 2, box.Y + (box.Height - s.Height) / 2, s.Width, s.Height));
                }
                else
                    using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, box.X + box.Width / 4, box.Y + box.Height / 4, box.Width / 2, box.Height / 2);
                TextRenderer.DrawText(g, Path.GetFileName(items[i]), Font, new Rectangle(c.X + Native.Px(2), box.Bottom + Native.Px(2), c.Width - Native.Px(4), LabelH),
                    i == selected ? Theme.Text : Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        // ---- input

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int i = HitTest(e.Location);
            if (i < 0) return;
            selected = i;
            Invalidate();
            if (ItemClicked != null) ItemClicked(items[i]);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0)
            {
                int k = Array.IndexOf(Sizes, thumbSize);
                if (k < 0) k = 4;
                ThumbSize = Sizes[Math.Max(0, Math.Min(Sizes.Length - 1, k + (e.Delta > 0 ? 1 : -1)))];
                return;
            }
            SetScroll(scroll - e.Delta * CellH / 240);
        }

        // ---- making thumbnails

        void RequestVisible()
        {
            if (!IsHandleCreated || items.Count == 0) return;
            int first = scroll / CellH * Columns, last = Math.Min(items.Count - 1, ((scroll + ClientSize.Height) / CellH + 1) * Columns - 1);
            // The visible ones, then a screenful below and above.
            int page = Math.Max(Columns, last - first + 1);
            List<string> want = new List<string>();
            for (int i = first; i <= last; i++) Add(want, i);
            for (int i = last + 1; i <= Math.Min(items.Count - 1, last + page); i++) Add(want, i);
            for (int i = first - 1; i >= Math.Max(0, first - page); i--) Add(want, i);
            Trim(Math.Max(0, first - page), Math.Min(items.Count - 1, last + page));
            lock (gate) { wanted = want; wantedSize = thumbSize; generation++; }
            if (want.Count == 0) return;
            if (worker == null)
            {
                worker = new Thread(Run);
                worker.IsBackground = true;
                worker.SetApartmentState(ApartmentState.STA);
                worker.Priority = ThreadPriority.BelowNormal;
                worker.Start();
            }
            wake.Set();
        }

        void Add(List<string> want, int i)
        {
            string p = items[i];
            int have;
            if (failed.Contains(p) || cacheSize.TryGetValue(p, out have) && have == thumbSize) return;
            want.Add(p);
        }

        // Keeps memory bounded: thumbnails far from the visible part go first.
        void Trim(int keepFrom, int keepTo)
        {
            long bytes = 0;
            foreach (Bitmap b in cache.Values) bytes += (long)b.Width * b.Height * 4;
            if (bytes <= MemoryBudget) return;
            HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = keepFrom; i <= keepTo; i++) keep.Add(items[i]);
            foreach (string p in new List<string>(cache.Keys))
                if (!keep.Contains(p))
                {
                    Bitmap b = cache[p];
                    bytes -= (long)b.Width * b.Height * 4;
                    b.Dispose();
                    cache.Remove(p);
                    cacheSize.Remove(p);
                    if (bytes <= MemoryBudget * 3 / 4) break;
                }
        }

        void Run()
        {
            while (!quit)
            {
                wake.WaitOne();
                while (!quit)
                {
                    string path;
                    int size, version;
                    lock (gate)
                    {
                        if (wanted.Count == 0) break;
                        path = wanted[0];
                        wanted.RemoveAt(0);
                        size = wantedSize;
                        versions.TryGetValue(path, out version);
                    }
                    Bitmap b = null;
                    try { b = PreviewWorker.ThumbnailFor(path, size); } catch { }
                    if (quit) { if (b != null) b.Dispose(); return; }
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (IsDisposed) { if (b != null) b.Dispose(); return; }
                            int now;
                            lock (gate) versions.TryGetValue(path, out now);
                            if (now != version) { if (b != null) b.Dispose(); RequestVisible(); return; } // turned meanwhile
                            if (b == null) { failed.Add(path); return; }
                            int have;
                            if (cacheSize.TryGetValue(path, out have) && have == thumbSize && size != thumbSize) { b.Dispose(); return; }
                            Forget(path);
                            cache[path] = b;
                            cacheSize[path] = size;
                            int i = items.FindIndex(delegate(string p) { return string.Equals(p, path, StringComparison.OrdinalIgnoreCase); });
                            if (i >= 0) Invalidate(CellRect(i));
                        });
                    }
                    catch { if (b != null) b.Dispose(); return; }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                quit = true;
                wake.Set();
                foreach (Bitmap b in cache.Values) b.Dispose();
                cache.Clear();
                cacheSize.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
