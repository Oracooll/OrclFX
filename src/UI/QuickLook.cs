// Orcl File Explorer: the Space Viewer (Space in a file list): a large view of the selected file next to a pane with
// thumbnails of every picture in the folder. Either pane can be turned off from the title bar (viewer only, or
// thumbnails only). Pictures can be zoomed (wheel, 1 = 100 %), moved, shown full screen (F), turned without losing
// quality ([ and ]), deleted to the Recycle Bin (Del) and described (I). Arrows step through the folder; Space or
// Esc closes. While it's open it gets those keys wherever the keyboard focus is in the app (MainForm.FilterMessage).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    class QuickLook : Form
    {
        // Remembered between uses (and in the settings file).
        public static bool ShowThumbs = true, ShowViewer = true, ShowInfo;
        public static int ThumbSize = 160, ThumbWidth = 360; // ThumbWidth in 96-dpi pixels

        readonly MainForm main;
        readonly BrowserTab tab;
        readonly ViewerBar bar;
        readonly Panel body = new Panel(), viewerHost = new Panel(), splitter = new Panel();
        readonly ImageView image = new ImageView();
        readonly PreviewPane preview = new PreviewPane();
        readonly ThumbGrid thumbs = new ThumbGrid();
        readonly System.Windows.Forms.Timer refocus = new System.Windows.Forms.Timer();
        readonly PictureLoader loader;
        List<string> files;            // the folder's files in the order shown
        string path;
        string onScreen;               // the file actually shown (path may still be loading): Del and turning act on it
        bool fullScreen;
        FormWindowState stateBefore;
        Rectangle boundsBefore;

        public QuickLook(MainForm main, BrowserTab tab, string path)
        {
            this.main = main;
            this.tab = tab;
            this.path = path;
            Text = "Space Viewer";
            Icon = main.Icon;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Font = main.Font;
            Rectangle area = Screen.FromControl(main).WorkingArea;
            Size = new Size(area.Width * 4 / 5, area.Height * 5 / 6);
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            MinimumSize = new Size(Native.Px(480), Native.Px(320));
            BackColor = Theme.Window;

            bar = new ViewerBar(this);
            image.Dock = preview.Dock = DockStyle.Fill;
            image.BackColor = Theme.Dark ? Color.FromArgb(12, 12, 12) : Color.FromArgb(235, 235, 235);
            image.Font = Font;
            image.ShowInfo = ShowInfo;
            image.WantsFullSize += delegate { loader.LoadFull(this.path); };
            image.ZoomChanged += delegate { UpdateTitle(); };
            preview.Visible = false;
            viewerHost.Controls.Add(image);
            viewerHost.Controls.Add(preview);
            thumbs.BackColor = Theme.Window;
            thumbs.Font = Font;
            thumbs.ThumbSize = ThumbSize;
            thumbs.ItemClicked += delegate(string p) { Go(p); };
            thumbs.ThumbSizeChanged += delegate(int s) { ThumbSize = s; main.StateChanged(); };
            splitter.Width = Native.Px(5);
            splitter.BackColor = Theme.Border;
            splitter.Cursor = Cursors.VSplit;
            int dragX = 0, startW = 0;
            splitter.MouseDown += delegate(object s, MouseEventArgs e) { dragX = Cursor.Position.X; startW = thumbs.Width; splitter.Capture = true; };
            splitter.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!splitter.Capture) return;
                thumbs.Width = Math.Max(Native.Px(120), Math.Min(body.Width - Native.Px(200), startW - (Cursor.Position.X - dragX)));
            };
            splitter.MouseUp += delegate { splitter.Capture = false; ThumbWidth = (int)Math.Round(thumbs.Width * 96.0 / Native.Px(96)); main.StateChanged(); };
            body.Dock = DockStyle.Fill;
            body.Controls.Add(viewerHost);
            body.Controls.Add(splitter);
            body.Controls.Add(thumbs);
            Controls.Add(body);
            Controls.Add(bar);
            preview.ApplyTheme();
            ApplyPanes();

            loader = new PictureLoader(this);
            HandleCreated += delegate
            {
                int dark = Theme.Dark ? 1 : 0;
                Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020); // frame changed
            };
            // Windows' preview handlers (PDF, Office …) run in another process and may take the keyboard focus
            // when they load: take it back shortly after each file.
            refocus.Interval = 400;
            refocus.Tick += delegate { refocus.Stop(); TakeFocus(); };
            preview.Loaded += delegate { TakeFocus(); refocus.Stop(); refocus.Start(); };
            FormClosing += delegate { refocus.Stop(); loader.Quit(); preview.Shutdown(); };
            Shown += delegate
            {
                ShowFile(this.path);
                // The folder's list after the first picture is on screen (a big folder takes a moment).
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    files = tab.Created ? tab.FilePaths(50000) : new List<string>();
                    if (files.FindIndex(Same(this.path)) < 0) files.Insert(0, this.path);
                    thumbs.SetItems(files.FindAll(Pictures.IsImageFile), this.path);
                    UpdateTitle();
                    if (Pictures.IsPictureKind(this.path)) Preload();
                });
            };
        }

        static Predicate<string> Same(string p) { return delegate(string x) { return string.Equals(x, p, StringComparison.OrdinalIgnoreCase); }; }

        public string ShownPath { get { return path; } }

        // ---- panes

        void ApplyPanes()
        {
            if (!ShowThumbs && !ShowViewer) ShowViewer = true;
            body.SuspendLayout();
            thumbs.Visible = ShowThumbs;
            viewerHost.Visible = ShowViewer;
            splitter.Visible = ShowThumbs && ShowViewer;
            thumbs.Dock = ShowViewer ? DockStyle.Right : DockStyle.Fill;
            if (ShowViewer) thumbs.Width = Math.Max(Native.Px(120), Native.Px(ThumbWidth));
            splitter.Dock = DockStyle.Right;
            viewerHost.Dock = DockStyle.Fill;
            body.ResumeLayout(true);
            bar.ThumbsButton.Checked = ShowThumbs;
            bar.ViewerButton.Checked = ShowViewer;
            bar.InfoButton.Checked = ShowInfo;
        }

        public void TogglePane(bool thumbsPane)
        {
            if (thumbsPane) ShowThumbs = !ShowThumbs; else ShowViewer = !ShowViewer;
            // At least one pane stays: turning off the last one shows the other.
            if (!ShowThumbs && !ShowViewer) { if (thumbsPane) ShowViewer = true; else ShowThumbs = true; }
            ApplyPanes();
            if (ShowViewer) ShowFile(path);
            else { preview.Unload(); image.Clear(null); }
            UpdateTitle();
            main.StateChanged();
        }

        public void ToggleInfo()
        {
            ShowInfo = image.ShowInfo = !ShowInfo;
            bar.InfoButton.Checked = ShowInfo;
            image.Invalidate();
            main.StateChanged();
        }

        // ---- showing a file

        public void ShowFile(string p)
        {
            path = p;
            thumbs.Select(p);
            UpdateTitle();
            Program.Trace("quick look shows " + p);
            if (!ShowViewer) return;
            if (Pictures.IsPictureKind(p))
            {
                preview.Unload();
                preview.Visible = false;
                image.Visible = true;
                Pictures.PictureData d = loader.CachedCopy(p);
                if (d != null) { image.SetImage(d.Bitmap, d.Original, d.Info); onScreen = p; }
                else loader.Load(p); // shown when it arrives (the previous picture stays until then)
                Preload();
            }
            else
            {
                image.Visible = false;
                image.Clear(null);
                preview.Visible = true;
                preview.Show(p);
                onScreen = p;
            }
        }

        // The neighbours are decoded in the background, so stepping to them is instant.
        void Preload()
        {
            int i = files == null ? -1 : files.FindIndex(Same(path));
            if (i < 0) return;
            List<string> next = new List<string>();
            for (int k = i + 1; k < files.Count && next.Count < 2; k++) if (Pictures.IsPictureKind(files[k])) next.Add(files[k]);
            for (int k = i - 1; k >= 0; k--) if (Pictures.IsPictureKind(files[k])) { next.Add(files[k]); break; }
            loader.Preload(next);
        }

        // d's bitmap is this window's own copy: used or disposed here.
        internal void Loaded(string p, Pictures.PictureData d, bool full)
        {
            if (IsDisposed || p != path || !ShowViewer || !image.Visible) { if (d != null) d.Bitmap.Dispose(); return; }
            if (d == null)
            {
                if (full) return;
                // Not a picture this app can read after all: Windows' preview instead.
                image.Visible = false;
                preview.Visible = true;
                preview.Show(p);
                onScreen = p;
                return;
            }
            if (full) image.SetFullSize(d.Bitmap);
            else { image.SetImage(d.Bitmap, d.Original, d.Info); onScreen = p; }
            Program.Trace("quick look picture " + Path.GetFileName(p) + (full ? " (full size) " : " ") + d.Bitmap.Width + "x" + d.Bitmap.Height);
        }

        void UpdateTitle()
        {
            int i = files == null ? -1 : files.FindIndex(Same(path));
            string t = Path.GetFileName(path.TrimEnd('\\'));
            if (i >= 0) t += "   " + (i + 1) + " / " + files.Count;
            if (ShowViewer && image.Visible && image.HasImage) t += "   " + Math.Round(image.Zoom * 100) + " %";
            Text = t;
            bar.Invalidate();
        }

        // Goes to a file: shows it and selects it in the file list.
        void Go(string p)
        {
            if (p == null) { System.Media.SystemSounds.Beep.Play(); return; }
            if (tab.Created) tab.SelectPath(p, 0x1 | 0x4 | 0x8 | 0x10);
            ShowFile(p);
        }

        public void Step(int d)
        {
            if (!ShowViewer)
            {
                // Thumbnails only: the arrows move around the grid.
                Go(thumbs.Neighbour(Pictures.IsImageFile(path) ? path : null, d));
                return;
            }
            int i = files == null ? -1 : files.FindIndex(Same(path));
            int n = i + d;
            Go(files != null && n >= 0 && n < files.Count ? files[n] : null);
        }

        // ---- delete and turn

        // The file on screen, if it's the current one and a file (not a folder, not one still loading).
        string ActOn()
        {
            // With the viewer: the picture really on screen. Thumbnails only: the thumbnail that is selected.
            bool shown = ShowViewer ? onScreen != null && string.Equals(onScreen, path, StringComparison.OrdinalIgnoreCase)
                                    : string.Equals(thumbs.Selected, path, StringComparison.OrdinalIgnoreCase);
            if (!shown) { System.Media.SystemSounds.Beep.Play(); return null; }
            if (Directory.Exists(path) || !File.Exists(path)) { System.Media.SystemSounds.Beep.Play(); return null; }
            return path;
        }

        public void DeleteCurrent()
        {
            string gone = ActOn();
            if (gone == null) return;
            int i = files == null ? -1 : files.FindIndex(Same(gone));
            preview.Unload(); // a preview handler may hold the file open
            if (!Native.ShellRecycle(Handle, gone) || File.Exists(gone))
            {
                main.Notice("⚠ " + Path.GetFileName(gone) + " wasn't deleted.");
                return;
            }
            main.Notice(Path.GetFileName(gone) + " was moved to the Recycle Bin.");
            Program.Trace("quick look deleted " + gone);
            loader.Forget(gone);
            thumbs.Remove(gone);
            if (i >= 0) files.RemoveAt(i);
            if (files == null || files.Count == 0) { Close(); return; }
            if (!ShowViewer)
            {
                // Thumbnails only: the next picture (or the one before, at the end).
                string next = null;
                for (int k = Math.Max(0, i); k < files.Count && next == null; k++) if (Pictures.IsImageFile(files[k])) next = files[k];
                for (int k = Math.Min(i, files.Count) - 1; k >= 0 && next == null; k--) if (Pictures.IsImageFile(files[k])) next = files[k];
                if (next == null) { Close(); return; }
                Go(next);
                return;
            }
            Go(files[Math.Min(Math.Max(0, i), files.Count - 1)]);
        }

        public void Turn(bool cw)
        {
            if (ActOn() == null) return;
            if (!Pictures.IsPictureKind(path)) { System.Media.SystemSounds.Beep.Play(); return; }
            string problem = Rotation.Turn(path, cw);
            if (problem != null) { main.Notice("⚠ " + problem); Program.Trace("quick look turn: " + problem); return; }
            Program.Trace("quick look turned " + Path.GetFileName(path) + (cw ? " right" : " left"));
            loader.Forget(path);
            thumbs.Refresh(path);
            ShowFile(path);
        }

        // ---- full screen

        public void ToggleFullScreen()
        {
            fullScreen = !fullScreen;
            if (fullScreen)
            {
                stateBefore = WindowState;
                boundsBefore = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                bar.Visible = false;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = boundsBefore;
                WindowState = stateBefore;
                bar.Visible = true;
            }
            bar.FullButton.Checked = fullScreen;
            TakeFocus();
        }

        // ---- keys

        void TakeFocus()
        {
            if (IsDisposed || !Visible || Form.ActiveForm != this) return; // never from another app
            Native.SetFocus(Handle);
        }

        // A key pressed anywhere in the app while the viewer is open (target: the window it was sent to). True when
        // the viewer used it. Typing in the text preview's Search box is left alone.
        public bool HandleKey(Keys key, IntPtr target, bool repeat)
        {
            if ((Control.ModifierKeys & (Keys.Control | Keys.Alt)) != 0) return false;
            // Anything editable gets its keys (only the read-only text preview is the viewer's).
            TextBox box = Control.FromHandle(target) as TextBox;
            if (box != null ? !box.ReadOnly : target != IntPtr.Zero && Native.ClassName(target).IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            // A held key repeats: never for the ones that change files.
            if (repeat && (key == Keys.Delete || key == Keys.OemOpenBrackets || key == Keys.OemCloseBrackets)) return true;
            bool shift = (Control.ModifierKeys & Keys.Shift) != 0;
            Point centre = new Point(image.Width / 2, image.Height / 2);
            bool picture = ShowViewer && image.Visible && image.HasImage;
            switch (key)
            {
                case Keys.Escape: if (fullScreen) ToggleFullScreen(); else Close(); return true;
                case Keys.Space: if (shift) return false; Close(); return true;
                case Keys.Right: Step(1); return true;
                case Keys.Left: Step(-1); return true;
                case Keys.Down: Step(ShowViewer ? 1 : thumbs.Columns); return true;
                case Keys.Up: Step(ShowViewer ? -1 : -thumbs.Columns); return true;
                case Keys.Home:
                case Keys.End:
                    {
                        IList<string> list = ShowViewer ? (IList<string>)files : thumbs.Items;
                        if (list != null && list.Count > 0) Go(list[key == Keys.Home ? 0 : list.Count - 1]);
                        return true;
                    }
                case Keys.Enter: case Keys.F: ToggleFullScreen(); return true;
                case Keys.I: ToggleInfo(); return true;
                case Keys.T: TogglePane(true); return true;
                case Keys.V: TogglePane(false); return true;
                case Keys.Delete: DeleteCurrent(); return true;
                case Keys.OemOpenBrackets: Turn(false); return true;
                case Keys.OemCloseBrackets: Turn(true); return true;
                case Keys.D1: case Keys.NumPad1: if (picture) image.ToggleActualSize(centre); return true;
                case Keys.D0: case Keys.NumPad0: if (picture) image.FitToWindow(); return true;
                case Keys.Oemplus: case Keys.Add: if (picture) image.ZoomBy(1.25f, centre); return true;
                case Keys.OemMinus: case Keys.Subtract: if (picture) image.ZoomBy(0.8f, centre); return true;
            }
            return false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Normally MainForm's message filter has handled these already; this is the fallback.
            if ((keyData & (Keys.Control | Keys.Alt)) == 0 && HandleKey(keyData & Keys.KeyCode, msg.HWnd, ((long)msg.LParam & 0x40000000) != 0)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- frame: our own title bar instead of Windows' caption (as in the main window)

        protected override void WndProc(ref Message m)
        {
            const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero && FormBorderStyle != FormBorderStyle.None)
            {
                RECT before = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                base.WndProc(ref m);
                RECT after = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                after.top = before.top + (WindowState == FormWindowState.Maximized ? Native.GetSystemMetrics(33) + Native.GetSystemMetrics(92) : 0);
                Marshal.StructureToPtr(after, m.LParam, false);
                return;
            }
            if (m.Msg == WM_NCHITTEST && FormBorderStyle != FormBorderStyle.None)
            {
                base.WndProc(ref m);
                if ((int)m.Result == 1)
                {
                    int lp = unchecked((int)(long)m.LParam);
                    Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    int edge = Native.Px(6);
                    if (WindowState != FormWindowState.Maximized && p.Y < edge)
                        m.Result = (IntPtr)(p.X < edge * 2 ? 13 : p.X > ClientSize.Width - edge * 2 ? 14 : 12);
                    else if (bar.Visible && p.Y < bar.Bottom)
                        m.Result = (IntPtr)2; // HTCAPTION
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            refocus.Dispose();
            preview.Dispose();
            thumbs.Dispose();
            image.Dispose();
            main.QuickLookClosed(this);
        }

        // ---- the title bar

        class ViewerBar : Control
        {
            readonly QuickLook form;
            public readonly GlyphButton ThumbsButton, ViewerButton, InfoButton, FullButton;

            public ViewerBar(QuickLook f)
            {
                form = f;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                Dock = DockStyle.Top;
                Height = Native.Px(34);
                // Docked from the right in the order made: close first.
                GlyphButton close = Button("", "Close (Space or Esc)", 46);
                close.HoverBack = Color.FromArgb(196, 43, 28);
                close.HoverFore = Color.White;
                close.Click += delegate { form.Close(); };
                GlyphButton max = Button("", "Maximize", 46);
                max.Click += delegate { form.WindowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; };
                form.Resize += delegate { max.Glyph = form.WindowState == FormWindowState.Maximized ? "" : ""; };
                GlyphButton min = Button("", "Minimize", 46);
                min.Click += delegate { form.WindowState = FormWindowState.Minimized; };
                Gap();
                FullButton = Button("", "Full screen (F or Enter; Esc to leave)", 36);
                FullButton.Click += delegate { form.ToggleFullScreen(); };
                GlyphButton del = Button("", "Move to the Recycle Bin (Del)", 36);
                del.Click += delegate { form.DeleteCurrent(); };
                GlyphButton right = Button("", "Turn right ( ] ) without losing quality", 36);
                right.Painter = delegate(Graphics g, Rectangle r, Color c) { DrawTurn(g, r, c, true); };
                right.Click += delegate { form.Turn(true); };
                GlyphButton left = Button("", "Turn left ( [ ) without losing quality", 36);
                left.Painter = delegate(Graphics g, Rectangle r, Color c) { DrawTurn(g, r, c, false); };
                left.Click += delegate { form.Turn(false); };
                InfoButton = Button("", "Picture details (I)", 36);
                InfoButton.Click += delegate { form.ToggleInfo(); };
                Gap();
                ThumbsButton = Button("", "Thumbnail pane (T). Ctrl+mouse wheel over it makes the thumbnails bigger or smaller", 36);
                ThumbsButton.Painter = delegate(Graphics g, Rectangle r, Color c) { DrawPane(g, r, c, true); };
                ThumbsButton.Click += delegate { form.TogglePane(true); };
                ViewerButton = Button("", "Viewer pane (V)", 36);
                ViewerButton.Painter = delegate(Graphics g, Rectangle r, Color c) { DrawPane(g, r, c, false); };
                ViewerButton.Click += delegate { form.TogglePane(false); };
                form.Activated += delegate { Invalidate(); };
                form.Deactivate += delegate { Invalidate(); };
            }

            GlyphButton Button(string glyph, string tip, int width)
            {
                GlyphButton b = new GlyphButton(glyph, tip, DockStyle.Right);
                b.Width = Native.Px(width);
                Controls.Add(b);
                b.BringToFront();
                return b;
            }

            void Gap()
            {
                Spacer p = new Spacer();
                p.Dock = DockStyle.Right;
                p.Width = Native.Px(14);
                Controls.Add(p);
                p.BringToFront();
            }

            class Spacer : Control
            {
                public Spacer() { SetStyle(ControlStyles.Selectable, false); }
                protected override void OnPaint(PaintEventArgs e)
                {
                    e.Graphics.Clear(Theme.Bar);
                    using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
                }
                protected override void WndProc(ref Message m)
                {
                    if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; } // part of the caption
                    base.WndProc(ref m);
                }
            }

            // Window pictograms: viewer = a frame mostly filled; thumbnails = a frame with a column of small squares.
            static void DrawPane(Graphics g, Rectangle r, Color c, bool thumbsPane)
            {
                using (Pen p = new Pen(c))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(150, c)))
                {
                    g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                    int col = r.Width * 6 / 16;
                    if (thumbsPane)
                    {
                        int s = Math.Max(2, (col - 3) / 2);
                        for (int y = r.Y + 2; y + s <= r.Bottom - 2; y += s + 1)
                        {
                            g.FillRectangle(b, r.Right - col, y, s, s);
                            g.FillRectangle(b, r.Right - col + s + 1, y, s, s);
                        }
                    }
                    else g.FillRectangle(b, r.X + 2, r.Y + 2, r.Width - col - 3, r.Height - 4);
                }
            }

            static void DrawTurn(Graphics g, Rectangle r, Color c, bool cw)
            {
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(c, Math.Max(1.4f, Native.Px(1) * 1.4f)))
                {
                    RectangleF a = new RectangleF(r.X + r.Width / 2f - r.Height / 2f + 1, r.Y + 1, r.Height - 2, r.Height - 2);
                    float start = cw ? 160 : 20, sweep = cw ? 250 : -250;
                    g.DrawArc(p, a, start, sweep);
                    // The arrow head where the arc ends.
                    double ang = (start + sweep) * Math.PI / 180, tangent = ang + (cw ? Math.PI / 2 : -Math.PI / 2);
                    float ex = a.X + a.Width / 2 + (float)Math.Cos(ang) * a.Width / 2, ey = a.Y + a.Height / 2 + (float)Math.Sin(ang) * a.Height / 2;
                    float h = Native.Px(4);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        double back = tangent + Math.PI + s * 0.6;
                        g.DrawLine(p, ex, ey, ex + (float)Math.Cos(back) * h, ey + (float)Math.Sin(back) * h);
                    }
                }
                g.SmoothingMode = old;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.Bar);
                int s = Native.Px(16), x = Native.Px(12);
                if (form.Icon != null) g.DrawIcon(form.Icon, new Rectangle(x, (Height - s) / 2, s, s));
                int right = Width;
                foreach (Control c in Controls) if (c.Visible) right = Math.Min(right, c.Left);
                TextRenderer.DrawText(g, form.Text, Font, new Rectangle(x + s + Native.Px(10), 0, Math.Max(0, right - x - s - Native.Px(20)), Height),
                    Form.ActiveForm == form ? Theme.Text : Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; } // HTTRANSPARENT: the form treats it as its caption
                base.WndProc(ref m);
            }
        }
    }

    // Decodes pictures for the Space Viewer in the background: the one asked for first, then its neighbours. A few
    // screen-sized pictures are kept; the full-size one is made only when zooming in past it.
    class PictureLoader
    {
        readonly QuickLook owner;
        readonly object gate = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Dictionary<string, Pictures.PictureData> cache = new Dictionary<string, Pictures.PictureData>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> order = new List<string>();  // oldest first
        string want, full;
        int epoch;   // bumped by Forget: a decode that started before it is out of date
        List<string> preload = new List<string>();
        volatile bool quit;
        readonly Size screenBox;

        public PictureLoader(QuickLook owner)
        {
            this.owner = owner;
            Rectangle s = Screen.FromControl(owner).Bounds;
            screenBox = new Size(Math.Max(s.Width, s.Height), Math.Max(s.Width, s.Height));
            Thread thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Start();
        }

        // A copy of a kept picture for the window (the kept one may be dropped by this thread at any time).
        public Pictures.PictureData CachedCopy(string p)
        {
            lock (gate)
            {
                Pictures.PictureData d;
                return cache.TryGetValue(p, out d) ? CopyOf(d) : null;
            }
        }

        static Pictures.PictureData CopyOf(Pictures.PictureData d)
        {
            Pictures.PictureData c = new Pictures.PictureData();
            c.Bitmap = new Bitmap(d.Bitmap);
            c.Original = d.Original;
            c.Info = d.Info;
            return c;
        }

        Size OriginalOf(string p)
        {
            lock (gate)
            {
                Pictures.PictureData d;
                return cache.TryGetValue(p, out d) ? d.Original : screenBox;
            }
        }

        public void Load(string p) { lock (gate) { want = p; full = null; } wake.Set(); }
        public void LoadFull(string p) { lock (gate) full = p; wake.Set(); }
        public void Preload(List<string> ps) { lock (gate) preload = ps; wake.Set(); }
        public void Quit() { quit = true; wake.Set(); }

        public void Forget(string p)
        {
            lock (gate)
            {
                Pictures.PictureData d;
                if (cache.TryGetValue(p, out d)) { d.Bitmap.Dispose(); cache.Remove(p); order.Remove(p); }
                epoch++;
            }
        }

        void Run()
        {
            while (!quit)
            {
                wake.WaitOne();
                while (!quit)
                {
                    string p;
                    bool isFull = false;
                    int started;
                    lock (gate)
                    {
                        started = epoch;
                        if (want != null) { p = want; want = null; }
                        else if (full != null) { p = full; full = null; isFull = true; }
                        else
                        {
                            p = null;
                            while (preload.Count > 0 && p == null) { string x = preload[0]; preload.RemoveAt(0); if (!cache.ContainsKey(x)) p = x; }
                            if (p == null) break;
                        }
                    }
                    Pictures.PictureData d = null;
                    try
                    {
                        if (isFull)
                        {
                            // Up to about 60 megapixels: bigger pictures are shown a little below 100 %.
                            Size o = OriginalOf(p);
                            double k = Math.Min(1.0, Math.Sqrt(60e6 / Math.Max(1.0, (double)o.Width * o.Height)));
                            d = Pictures.LoadEx(p, new Size((int)(o.Width * k), (int)(o.Height * k)), false);
                            lock (gate) if (d != null && epoch != started) { d.Bitmap.Dispose(); continue; } // turned meanwhile
                        }
                        else
                        {
                            d = CachedCopy(p);
                            if (d == null)
                            {
                                Pictures.PictureData made = Pictures.LoadEx(p, screenBox, true);
                                if (made != null)
                                    lock (gate)
                                    {
                                        if (epoch != started) { made.Bitmap.Dispose(); continue; } // the file changed meanwhile
                                        d = CopyOf(made);
                                        Keep(p, made);
                                    }
                            }
                        }
                    }
                    catch { d = null; }
                    // d is a copy the window owns from here on.
                    if (quit) { if (d != null) d.Bitmap.Dispose(); return; }
                    Pictures.PictureData result = d;
                    bool f = isFull;
                    try { owner.BeginInvoke((MethodInvoker)delegate { owner.Loaded(p, result, f); }); }
                    catch { if (result != null) result.Bitmap.Dispose(); return; }
                }
            }
        }

        void Keep(string p, Pictures.PictureData d)
        {
            lock (gate)
            {
                cache[p] = d;
                order.Remove(p);
                order.Add(p);
                while (order.Count > 5)
                {
                    Pictures.PictureData old;
                    if (cache.TryGetValue(order[0], out old)) { old.Bitmap.Dispose(); cache.Remove(order[0]); }
                    order.RemoveAt(0);
                }
            }
        }
    }
}
