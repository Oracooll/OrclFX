// OrclFX: the Space Viewer's picture view: fit to the window or zoom (mouse wheel, arrows, 1 for 100 %), drag to
// move around, and an info box with the photo's details. Touch: pinch to zoom, a finger moves a zoomed-in picture,
// a swipe steps to the next or previous picture, two fingers tapped fit it to the window.
// Smooth zoom works like Windows' Photos app: the view glides to its new size over a few frames (each frame waits
// for the screen's refresh), the graphics card draws the picture when it can (D2DRenderer; GDI+ otherwise), the
// picture is kept in a few sizes so each frame scales the one nearest to the size on screen, and frames drawn
// while moving use quick scaling while the still picture gets the sharp one.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    class ImageView : Control
    {
        // The picture in a few sizes, smallest first: the screen-sized copy, and once zoomed in past it, the full
        // picture (and half of it, for the zooms in between).
        readonly List<Bitmap> levels = new List<Bitmap>();
        Size original;         // the picture's own (upright) size
        bool fit = true;       // fit to the window (else zoom and offset apply)
        float zoom = 1f;       // screen pixels per picture pixel
        PointF offset;         // where the picture's top left corner is, in this control
        bool fullAsked;        // the full picture was asked for (bigger ones are capped: don't ask again)
        string[] info;
        public bool ShowInfo;
        public string Message;  // shown instead of a picture (loading, can't show …)
        public bool Enlarge;    // fit also enlarges pictures smaller than the window

        // Moving the picture by dragging (with the buttons the settings allow); a press that doesn't move is a click.
        MouseButtons pressed = MouseButtons.None;
        Point pressAt, dragFrom;
        int pressClicks;
        PointF offsetFrom;
        bool dragging;
        int wheelRest;          // wheel turned less than a notch (Previous / next picture)

        // The glide: from (animZ, animAt) to the target view, over AnimMs.
        const double AnimMs = 190;
        bool animating;
        float animZ;
        PointF animAt;
        readonly Stopwatch clock = Stopwatch.StartNew();
        double animStart;
        double lastFrameCost;

        // Drawing with the graphics card.
        D2DRenderer gpu;
        bool gpuTried;
        string gpuStatus = "not started yet";
        Bitmap infoLayer, messageLayer;
        string infoKey, messageKey;

        // Raised when the zoom goes past the screen-sized copy: the owner loads the full picture.
        public event Action WantsFullSize;
        public event Action ZoomChanged;
        public event Action<int> WheelStepped;          // the wheel set to step: +1 next, -1 previous
        public event Action<MouseButtons> Clicked;       // pressed and let go without moving
        public event Action DoubleClicked;               // left double-click
        public event Action<int> Swiped;                 // touch: a swipe left (+1, next) or right (-1, previous)

        public ImageView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, false);
        }

        Bitmap Largest { get { return levels.Count == 0 ? null : levels[levels.Count - 1]; } }
        public bool HasImage { get { return levels.Count > 0; } }
        public Size Original { get { return original; } }
        public bool IsFit { get { return fit; } }
        public float Zoom { get { return fit ? FitZoom : zoom; } }
        // The bitmap being drawn holds fewer pixels than the picture at this zoom.
        public bool NeedsFullSize { get { Bitmap b = Largest; return b != null && b.Width < original.Width && Zoom * original.Width > b.Width * 1.02f; } }
        // What draws the picture, for the settings screen.
        public string DrawingStatus { get { return gpu != null && !gpu.Failed ? "the graphics card (Direct2D)" : "the processor (GDI+)" + (ViewerSettings.Gpu ? " – graphics card: " + gpuStatus : ""); } }

        float FitZoom
        {
            get
            {
                if (original.Width <= 0 || original.Height <= 0) return 1;
                float f = Math.Min((float)ClientSize.Width / original.Width, (float)ClientSize.Height / original.Height);
                return Enlarge ? f : Math.Min(1f, f);
            }
        }

        PointF Centred(float z) { return new PointF((ClientSize.Width - original.Width * z) / 2, (ClientSize.Height - original.Height * z) / 2); }

        // ---- the picture

        void DropLevels()
        {
            foreach (Bitmap b in levels) { if (gpu != null) gpu.Forget(b); b.Dispose(); }
            levels.Clear();
        }

        // A new picture (the old bitmaps are disposed); fit to the window.
        public void SetImage(Bitmap b, Size originalSize, string[] details)
        {
            if (levels.Count != 1 || levels[0] != b) { DropLevels(); levels.Add(b); }
            EndDrag();
            TouchReset();
            original = originalSize;
            info = details;
            Message = null;
            fit = true;
            animating = false;
            fullAsked = false;
            Invalidate();
            if (ZoomChanged != null) ZoomChanged();
        }

        // The full picture (and half of it, or null) join the screen-sized copy, keeping the zoom and position.
        public void SetFullSize(Bitmap full, Bitmap half)
        {
            if (levels.Count == 0) { full.Dispose(); if (half != null) half.Dispose(); return; }
            Bitmap screen = levels[0];
            for (int i = levels.Count - 1; i >= 1; i--) { if (gpu != null) gpu.Forget(levels[i]); levels[i].Dispose(); levels.RemoveAt(i); }
            if (half != null && half.Width > screen.Width) levels.Add(half); else if (half != null) half.Dispose();
            if (full.Width > screen.Width) levels.Add(full); else full.Dispose();
            Invalidate();
        }

        public void Clear(string message)
        {
            DropLevels();
            EndDrag();
            TouchReset();
            info = null;
            Message = message;
            animating = false;
            Invalidate();
        }

        // Smallest copy that still has a pixel for every screen pixel at this zoom.
        Bitmap LevelFor(float z)
        {
            float want = original.Width * z * 0.98f;
            foreach (Bitmap b in levels) if (b.Width >= want) return b;
            return Largest;
        }

        // ---- what is on screen, and the glide

        // The view being shown now (part way through a glide, or the target); true while gliding.
        bool ShownView(out float z, out PointF at)
        {
            z = Zoom;
            at = fit ? Centred(z) : offset;
            if (!animating) return false;
            // The glide's time starts with its first frame (a late first frame doesn't make it jump ahead).
            if (animStart < 0) animStart = clock.Elapsed.TotalMilliseconds;
            double t = (clock.Elapsed.TotalMilliseconds - animStart) / AnimMs;
            if (t >= 1) { animating = false; return false; }
            double e = 1 - Math.Pow(1 - Math.Max(0, t), 3); // ease out: quick start, gentle landing
            float z0 = animZ, z1 = z;
            float zt = (float)(z0 * Math.Pow(z1 / z0, e));
            at = new PointF(Between(animAt.X, at.X, z0, z1, zt, e), Between(animAt.Y, at.Y, z0, z1, zt, e));
            z = zt;
            return true;
        }

        // Along one axis: the picture point that stays put between the two views stays put on the way too.
        static float Between(float a0, float a1, float z0, float z1, float z, double e)
        {
            if (Math.Abs(z1 - z0) < 1e-5f) return (float)(a0 + (a1 - a0) * e);
            float p = (a0 - a1) / (z1 - z0);
            return a0 + p * z0 - p * z;
        }

        // Called before the target changes: the glide starts from what's on screen now.
        void Glide()
        {
            if (!ViewerSettings.Smooth || !IsHandleCreated || !Visible || !HasImage) { animating = false; return; }
            float z;
            PointF at;
            ShownView(out z, out at);
            animZ = z;
            animAt = at;
            animStart = -1;
            animating = true;
        }

        // Zooms by factor, keeping the picture point under the given screen point in place. glide: smoothly (when
        // the settings say so); a pinch follows the fingers directly.
        public void ZoomBy(float factor, Point around, bool glide = true, bool clamp = true)
        {
            if (!HasImage) return;
            float z = Zoom, nz = Math.Max(Math.Min(FitZoom, 0.05f), Math.Min(16f, z * factor));
            if (Math.Abs(nz - z) < 0.0001f) return;
            PointF at = fit ? Centred(z) : offset;
            float px = (around.X - at.X) / z, py = (around.Y - at.Y) / z;
            if (glide) Glide(); else animating = false;
            zoom = nz;
            fit = false;
            offset = new PointF(around.X - px * nz, around.Y - py * nz);
            if (clamp) Clamp();
            // Zoomed while dragging: the drag goes on from here (else the next move would put back the old place).
            if (dragging) { offsetFrom = offset; dragFrom = PointToClient(Cursor.Position); }
            Invalidate();
            Changed();
        }

        // A new picture (or none) ends a drag in progress.
        void EndDrag()
        {
            if (pressed == MouseButtons.None && !dragging) return;
            pressed = MouseButtons.None;
            dragging = false;
            Capture = false;
        }

        public Point Centre { get { return new Point(ClientSize.Width / 2, ClientSize.Height / 2); } }

        // 100 % at the point (or the centre), or back to fit when already there.
        public void ToggleActualSize(Point around)
        {
            if (!HasImage) return;
            if (!fit && Math.Abs(zoom - 1f) < 0.001f) { FitToWindow(); return; }
            if (Math.Abs(Zoom - 1f) < 0.001f) { Glide(); fit = false; zoom = 1f; offset = Centred(1f); Clamp(); Invalidate(); Changed(); return; }
            ZoomBy(1f / Zoom, around);
        }

        public void FitToWindow()
        {
            if (!fit) Glide();
            fit = true;
            Invalidate();
            Changed();
        }

        void Changed()
        {
            if (ZoomChanged != null) ZoomChanged();
            if (NeedsFullSize && !fullAsked && WantsFullSize != null) { fullAsked = true; WantsFullSize(); }
        }

        // A picture smaller than the window stays centred; a bigger one can't be dragged off screen.
        void Clamp()
        {
            float w = original.Width * zoom, h = original.Height * zoom;
            offset.X = w <= ClientSize.Width ? (ClientSize.Width - w) / 2 : Math.Min(0, Math.Max(ClientSize.Width - w, offset.X));
            offset.Y = h <= ClientSize.Height ? (ClientSize.Height - h) / 2 : Math.Min(0, Math.Max(ClientSize.Height - h, offset.Y));
        }

        // ---- drawing

        protected override void OnPaint(PaintEventArgs e)
        {
            double started = clock.Elapsed.TotalMilliseconds;
            float z;
            PointF at;
            bool moving = ShownView(out z, out at);
            bool fast = moving || dragging || touching;
            if (!PaintGpu(z, at, fast)) PaintGdi(e.Graphics, z, at, fast);
            lastFrameCost = clock.Elapsed.TotalMilliseconds - started;
            // While gliding, the next frame waits until Windows has put this one on screen (60, 120 … times a
            // second). (Waiting before drawing could wait long: with nothing changing, Windows doesn't redraw.)
            double flushed = 0;
            if (moving && lastFrameCost < 12) { double f0 = clock.Elapsed.TotalMilliseconds; Native.DwmFlush(); flushed = clock.Elapsed.TotalMilliseconds - f0; }
            base.OnPaint(e);
            if (Tracing) Program.Trace("image view frame at " + started.ToString("0") + ": " + lastFrameCost.ToString("0.0") + " ms, waited " + flushed.ToString("0.0") + " ms" + (moving ? " gliding" : "") + (HasImage ? ", from " + LevelFor(z).Width + " wide" : "") + (gpu != null && !gpu.Failed ? " (graphics card)" : " (GDI+)"));
            // The next frame: a repaint, which waits for the mouse and keys first (a wheel turned while gliding
            // changes where the glide goes at once).
            if (moving) Invalidate();
        }

        static readonly bool Tracing = Environment.GetEnvironmentVariable("DUALPANE_TEST_FRAMES") != null;

        // The part of the picture on screen: where it goes and which pixels of the bitmap fill it.
        bool Placement(Bitmap image, float z, PointF at, out RectangleF vis, out RectangleF src, out bool nearest, bool fast)
        {
            RectangleF dest = new RectangleF(at.X, at.Y, original.Width * z, original.Height * z);
            // Only the visible part is drawn: at high zoom that is a small piece of the bitmap.
            vis = RectangleF.Intersect(dest, ClientRectangle);
            src = RectangleF.Empty;
            nearest = false;
            if (vis.Width <= 0 || vis.Height <= 0) return false;
            float k = image.Width / (float)original.Width; // bitmap pixels per picture pixel
            src = new RectangleF((vis.X - dest.X) / z * k, (vis.Y - dest.Y) / z * k, vis.Width / z * k, vis.Height / z * k);
            bool exact = Math.Abs(vis.Width - src.Width) < 0.5f && Math.Abs(vis.Height - src.Height) < 0.5f;
            nearest = exact || z / k >= 2.5f;
            return true;
        }

        void PaintGdi(Graphics screen, float z, PointF at, bool fast)
        {
            using (BufferedGraphics buffer = BufferedGraphicsManager.Current.Allocate(screen, ClientRectangle))
            {
                Graphics g = buffer.Graphics;
                g.Clear(BackColor);
                if (!HasImage)
                {
                    if (Message != null)
                        TextRenderer.DrawText(g, Message, Font, ClientRectangle, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
                else
                {
                    Bitmap image = LevelFor(z);
                    RectangleF vis, src;
                    bool nearest;
                    if (Placement(image, z, at, out vis, out src, out nearest, fast))
                    {
                        // Moving: nearest pixel (GDI+'s smoothing takes 50-100 ms a frame, this under 10); still: sharp.
                        g.InterpolationMode = nearest || fast ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(image, vis, src, GraphicsUnit.Pixel); // (blended: transparent pictures show the background)
                    }
                    if (ShowInfo && info != null && info.Length > 0) DrawInfo(g);
                }
                buffer.Render(screen);
            }
        }

        void DrawInfo(Graphics g)
        {
            int pad = Native.Px(10), lineH = TextRenderer.MeasureText("Ag", Font).Height + Native.Px(2), w = 0;
            foreach (string s in info) w = Math.Max(w, TextRenderer.MeasureText(s, Font).Width);
            string zoomLine = ZoomLine();
            w = Math.Max(w, TextRenderer.MeasureText(zoomLine, Font).Width);
            Rectangle box = new Rectangle(Native.Px(12), Native.Px(12), w + pad * 2, lineH * (info.Length + 1) + pad * 2);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(185, 20, 20, 20))) g.FillRectangle(b, box);
            int y = box.Y + pad;
            for (int i = 0; i <= info.Length; i++)
            {
                string s = i < info.Length ? info[i] : zoomLine;
                TextRenderer.DrawText(g, s, Font, new Point(box.X + pad, y), i == 0 ? Color.White : Color.FromArgb(215, 215, 215), TextFormatFlags.NoPrefix);
                y += lineH;
            }
        }

        string ZoomLine() { return "Zoom " + Math.Round(Zoom * 100) + " %" + (fit ? " (fit)" : ""); }

        // ---- drawing with the graphics card

        // False when GDI+ has to draw this frame.
        bool PaintGpu(float z, PointF at, bool fast)
        {
            if (!ViewerSettings.Gpu) { if (gpu != null) { gpu.Dispose(); gpu = null; gpuTried = false; gpuStatus = "turned off"; } return false; }
            if (gpu == null)
            {
                if (gpuTried) return false;
                gpuTried = true;
                string problem;
                gpu = D2DRenderer.Create(Handle, ClientSize, out problem);
                gpuStatus = gpu != null ? "in use" : problem;
                Program.Trace("image view draws with " + (gpu != null ? "the graphics card (Direct2D)" : "GDI+: " + problem));
                if (gpu == null) return false;
            }
            if (gpu.Failed) { gpuStatus = gpu.Problem; return false; }
            List<D2DRenderer.Layer> layers = new List<D2DRenderer.Layer>();
            if (!HasImage)
            {
                if (Message != null)
                {
                    Bitmap m = MessageLayer();
                    D2DRenderer.Layer l = new D2DRenderer.Layer();
                    l.Bitmap = m;
                    l.Src = new RectangleF(0, 0, m.Width, m.Height);
                    l.Dest = new RectangleF((ClientSize.Width - m.Width) / 2, (ClientSize.Height - m.Height) / 2, m.Width, m.Height);
                    layers.Add(l);
                }
            }
            else
            {
                Bitmap image = LevelFor(z);
                if (!gpu.CanHold(image)) return false; // bigger than the card takes (in one piece, or in its memory)
                RectangleF vis, src;
                bool nearest;
                if (Placement(image, z, at, out vis, out src, out nearest, fast))
                {
                    D2DRenderer.Layer l = new D2DRenderer.Layer();
                    l.Bitmap = image;
                    l.Dest = vis;
                    l.Src = src;
                    l.Nearest = nearest;
                    layers.Add(l);
                }
                if (ShowInfo && info != null && info.Length > 0)
                {
                    Bitmap ib = InfoLayer();
                    D2DRenderer.Layer l = new D2DRenderer.Layer();
                    l.Bitmap = ib;
                    l.Src = new RectangleF(0, 0, ib.Width, ib.Height);
                    l.Dest = new RectangleF(Native.Px(12), Native.Px(12), ib.Width, ib.Height);
                    l.Nearest = true;
                    layers.Add(l);
                }
            }
            if (gpu.Draw(BackColor, layers)) return true;
            if (gpu.Failed) gpuStatus = gpu.Problem;
            return false;
        }

        // The info box as a picture of its own (drawn with GDI+, shown by the graphics card).
        Bitmap InfoLayer()
        {
            string key = string.Join("\n", info) + "\n" + ZoomLine();
            if (infoLayer != null && key == infoKey) return infoLayer;
            DropLayer(ref infoLayer);
            infoKey = key;
            int pad = Native.Px(10), lineH = TextRenderer.MeasureText("Ag", Font).Height + Native.Px(2), w = 0;
            string[] lines = key.Split('\n');
            foreach (string s in lines) w = Math.Max(w, TextRenderer.MeasureText(s, Font).Width);
            infoLayer = new Bitmap(w + pad * 2, lineH * lines.Length + pad * 2, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(infoLayer))
            {
                g.Clear(Color.FromArgb(185, 20, 20, 20));
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                int y = pad;
                for (int i = 0; i < lines.Length; i++)
                {
                    using (SolidBrush b = new SolidBrush(i == 0 ? Color.White : Color.FromArgb(215, 215, 215)))
                        g.DrawString(lines[i], Font, b, pad, y, StringFormat.GenericTypographic);
                    y += lineH;
                }
            }
            return infoLayer;
        }

        Bitmap MessageLayer()
        {
            string key = Message + "|" + BackColor.ToArgb() + "|" + ClientSize.Width;
            if (messageLayer != null && key == messageKey) return messageLayer;
            DropLayer(ref messageLayer);
            messageKey = key;
            Size room = new Size(Math.Max(50, ClientSize.Width - Native.Px(40)), Math.Max(20, ClientSize.Height));
            Size s = TextRenderer.MeasureText(Message, Font, room, TextFormatFlags.WordBreak);
            messageLayer = new Bitmap(Math.Max(1, s.Width + 4), Math.Max(1, s.Height + 4), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(messageLayer))
            {
                g.Clear(BackColor);
                TextRenderer.DrawText(g, Message, Font, new Rectangle(0, 0, messageLayer.Width, messageLayer.Height), Theme.TextDim, BackColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
            return messageLayer;
        }

        void DropLayer(ref Bitmap b)
        {
            if (b == null) return;
            if (gpu != null) gpu.Forget(b);
            b.Dispose();
            b = null;
        }

        // The drawing settings changed: the graphics card is tried again (or let go).
        public void SettingsChanged()
        {
            if (gpu != null && (!ViewerSettings.Gpu || gpu.Failed)) { gpu.Dispose(); gpu = null; }
            gpuTried = false;
            if (!ViewerSettings.Smooth) animating = false;
            Invalidate();
        }

        // ---- size

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (gpu != null && !gpu.Failed) gpu.Resize(ClientSize);
            animating = false;
            if (!fit) Clamp();
            Invalidate();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (gpu != null) { gpu.Dispose(); gpu = null; gpuTried = false; }
            TouchReset();
            base.OnHandleDestroyed(e);
        }

        // ---- mouse

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int mode = (ModifierKeys & Keys.Control) != 0 ? ViewerSettings.WheelZoomPointer : ViewerSettings.Wheel;
            if (mode == ViewerSettings.WheelStep)
            {
                wheelRest += e.Delta;
                int steps = wheelRest / 120;
                if (steps == 0) return;
                wheelRest -= steps * 120;
                if (WheelStepped != null) WheelStepped(-steps);
                return;
            }
            ZoomBy((float)Math.Pow(ViewerSettings.ZoomFactor, e.Delta / 120.0), mode == ViewerSettings.WheelZoomCentre ? Centre : e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (pressed != MouseButtons.None) return;
            pressed = e.Button;
            pressClicks = e.Clicks;
            pressAt = e.Location;
            Capture = true;
        }

        bool CanDrag(MouseButtons b) { return HasImage && !fit && (ViewerSettings.DragButtons & b) != 0; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressed != MouseButtons.None && !dragging && CanDrag(pressed))
            {
                Size slop = SystemInformation.DragSize;
                if (Math.Abs(e.X - pressAt.X) > slop.Width / 2 || Math.Abs(e.Y - pressAt.Y) > slop.Height / 2)
                {
                    // A glide in progress stops where it is: the picture follows the mouse from there.
                    if (animating)
                    {
                        float z;
                        PointF at;
                        ShownView(out z, out at);
                        animating = false;
                        zoom = z;
                        offset = at;
                        Clamp();
                    }
                    dragging = true;
                    dragFrom = pressAt; // from where it was pressed: the picture keeps up with the mouse from the start
                    offsetFrom = offset;
                    Cursor = Cursors.SizeAll;
                }
            }
            if (!dragging) { Cursor = HasImage && !fit && (ViewerSettings.DragButtons & MouseButtons.Left) != 0 ? Cursors.Hand : Cursors.Default; return; }
            offset = new PointF(offsetFrom.X + e.X - dragFrom.X, offsetFrom.Y + e.Y - dragFrom.Y);
            Clamp();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != pressed) return;
            bool wasDrag = dragging;
            MouseButtons b = pressed;
            pressed = MouseButtons.None;
            dragging = false;
            Capture = false;
            Cursor = HasImage && !fit && (ViewerSettings.DragButtons & MouseButtons.Left) != 0 ? Cursors.Hand : Cursors.Default;
            if (wasDrag) { Invalidate(); return; } // the still picture drawn sharp
            Size slop = SystemInformation.DragSize;
            if (Math.Abs(e.X - pressAt.X) > slop.Width || Math.Abs(e.Y - pressAt.Y) > slop.Height) return;
            // The second press of a double-click comes as one with Clicks = 2. (Not WinForms' own double-click
            // event: it only comes when this window is the one under the pointer on screen.)
            if (b == MouseButtons.Left && pressClicks == 2) { if (DoubleClicked != null) DoubleClicked(); }
            else if (Clicked != null) Clicked(b);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && pressed != MouseButtons.None) { pressed = MouseButtons.None; if (dragging) { dragging = false; Invalidate(); } }
        }

        // ---- touch

        int lastDistance;           // pinch: the fingers' distance at the last step
        Point lastPan, panStart;
        bool pinching, panning, panPicture, swipeDecided, pinchedThisTime;
        bool touching { get { return pinching || panning; } }

        // A gesture's start or end (Windows' GID_BEGIN / GID_END, around every pan and pinch): nothing left over.
        void TouchReset()
        {
            pinching = panning = panPicture = pinchedThisTime = false;
            lastDistance = 0;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Gestures.WM_GESTURENOTIFY) { Gestures.Configure(Handle); base.WndProc(ref m); return; }
            Gestures.Info g;
            if (m.Msg == Gestures.WM_GESTURE && Gestures.Read(ref m, out g))
            {
                bool used = false;
                try { used = Touch(g); }
                finally { if (used) Gestures.Done(ref m); }
                if (used) return;
            }
            base.WndProc(ref m);
        }

        // True when the gesture was used.
        internal bool Touch(Gestures.Info g)
        {
            Point at = PointToClient(g.Screen);
            switch (g.Id)
            {
                case Gestures.GID_BEGIN:
                    TouchReset();
                    return false; // (Windows closes it)
                case Gestures.GID_END:
                    if (touching) Invalidate(); // the still picture drawn sharp
                    TouchReset();
                    return false;
                case Gestures.GID_ZOOM:
                    pinchedThisTime = true;
                    if (g.Begins || lastDistance <= 0) { lastDistance = (int)g.Argument; pinching = true; animating = false; return true; }
                    if (g.Ends) pinching = false;
                    if (!HasImage) { lastDistance = (int)g.Argument; return true; }
                    // While pinching the picture follows the fingers freely (kept inside the window, the point between
                    // them would drift); it settles when they lift.
                    if (lastDistance > 0 && g.Argument > 0) ZoomBy((float)g.Argument / lastDistance, at, false, false);
                    lastDistance = (int)g.Argument;
                    if (g.Ends)
                    {
                        // Pinched smaller than the window: back to fit.
                        if (!fit && zoom < FitZoom) FitToWindow();
                        else if (!fit) { Glide(); Clamp(); Invalidate(); }
                        else Invalidate();
                    }
                    return true;
                case Gestures.GID_PAN:
                    if (g.Begins)
                    {
                        panStart = lastPan = at;
                        panning = true;
                        swipeDecided = false;
                        animating = false;
                        // Zoomed in: the finger moves the picture. Fitted: a swipe steps through the folder.
                        panPicture = HasImage && !fit;
                        return true;
                    }
                    if (panPicture)
                    {
                        offset = new PointF(offset.X + at.X - lastPan.X, offset.Y + at.Y - lastPan.Y);
                        Clamp();
                        Invalidate();
                    }
                    // (Not a two-finger pan, and not during a pinch: zooming with fingers drifting sideways isn't a swipe.)
                    else if (!swipeDecided && (g.Inertia || g.Ends) && g.Argument == 0 && !pinchedThisTime)
                    {
                        // Decided when the finger lifts (the first inertia step is where it lifted).
                        swipeDecided = true;
                        int dx = at.X - panStart.X, dy = at.Y - panStart.Y;
                        if (Math.Abs(dx) > Math.Max(Native.Px(50), ClientSize.Width / 12) && Math.Abs(dx) > Math.Abs(dy) * 1.5 && Swiped != null)
                            Swiped(dx < 0 ? 1 : -1);
                    }
                    lastPan = at;
                    if (g.Ends) { panning = false; Invalidate(); }
                    return true;
                case Gestures.GID_TWOFINGERTAP:
                    FitToWindow();
                    return true;
            }
            return false;
        }

        // ---- test hooks

        internal PointF TestOffset { get { return fit ? Centred(Zoom) : offset; } }
        // The picture's point (in its own pixels) at a point of this control.
        internal PointF TestPictureAt(Point c) { float z = Zoom; PointF at = TestOffset; return new PointF((c.X - at.X) / z, (c.Y - at.Y) / z); }
        internal string TestView
        {
            get
            {
                List<string> l = new List<string>();
                foreach (Bitmap b in levels) l.Add(b.Width.ToString());
                return "offset " + Math.Round(TestOffset.X) + "," + Math.Round(TestOffset.Y) + ", sizes " + string.Join("/", l.ToArray()) + ", " + (gpu != null && !gpu.Failed ? "graphics card" : "GDI+");
            }
        }

        // Glides through a zoom like a wheel notch and reports the frames drawn and the time between them.
        internal void TestGlide(Action<string> report)
        {
            int frames = 0;
            double first = -1, last = 0, worst = 0, prev = -1;
            PaintEventHandler count = delegate
            {
                double now = clock.Elapsed.TotalMilliseconds;
                if (first < 0) first = now;
                if (prev >= 0) worst = Math.Max(worst, now - prev);
                prev = last = now;
                frames++;
            };
            Paint += count;
            ZoomBy(ViewerSettings.ZoomFactor * ViewerSettings.ZoomFactor, Centre);
            Timer t = new Timer();
            t.Interval = 600;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                Paint -= count;
                report(frames + " frames in " + (last - first).ToString("0") + " ms, longest gap " + worst.ToString("0.0") + " ms, " + DrawingStatus + ", zoom " + Math.Round(Zoom * 100) + " %");
            };
            t.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DropLevels();
                DropLayer(ref infoLayer);
                DropLayer(ref messageLayer);
                if (gpu != null) { gpu.Dispose(); gpu = null; }
            }
            base.Dispose(disposing);
        }
    }
}
