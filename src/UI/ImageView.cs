// Orcl File Explorer: the Space Viewer's picture view: fit to the window or zoom (mouse wheel at the pointer, 1 for
// 100 %), drag to move around, and an info box with the photo's details.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    class ImageView : Control
    {
        Bitmap image;          // what is drawn: a screen-sized copy, or the full picture once zoomed in
        Size original;         // the picture's own (upright) size
        bool fit = true;       // fit to the window (else zoom and offset apply)
        float zoom = 1f;       // screen pixels per picture pixel
        PointF offset;         // where the picture's top left corner is, in this control
        Point dragFrom;
        PointF offsetFrom;
        bool dragging;
        bool fullAsked;        // the full picture was asked for (bigger ones are capped: don't ask again)
        string[] info;
        public bool ShowInfo;
        public string Message;  // shown instead of a picture (loading, can't show …)

        // Raised when the zoom goes past the screen-sized copy: the owner loads the full picture.
        public event Action WantsFullSize;
        public event Action ZoomChanged;

        public ImageView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        public bool HasImage { get { return image != null; } }
        public Size Original { get { return original; } }
        public bool IsFit { get { return fit; } }
        public float Zoom { get { return fit ? FitZoom : zoom; } }
        // The bitmap being drawn holds fewer pixels than the picture at this zoom.
        public bool NeedsFullSize { get { return image != null && image.Width < original.Width && Zoom * original.Width > image.Width * 1.02f; } }

        float FitZoom
        {
            get
            {
                if (original.Width <= 0 || original.Height <= 0) return 1;
                return Math.Min(1f, Math.Min((float)ClientSize.Width / original.Width, (float)ClientSize.Height / original.Height));
            }
        }

        // A new picture (the old bitmap is disposed); fit to the window.
        public void SetImage(Bitmap b, Size originalSize, string[] details)
        {
            Bitmap old = image;
            image = b;
            original = originalSize;
            info = details;
            Message = null;
            fit = true;
            fullAsked = false;
            if (old != null && old != b) old.Dispose();
            Invalidate();
            if (ZoomChanged != null) ZoomChanged();
        }

        // The full picture replaces the screen-sized copy, keeping the zoom and position.
        public void SetFullSize(Bitmap b)
        {
            Bitmap old = image;
            image = b;
            if (old != null && old != b) old.Dispose();
            Invalidate();
        }

        public void Clear(string message)
        {
            Bitmap old = image;
            image = null;
            info = null;
            Message = message;
            if (old != null) old.Dispose();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (image == null)
            {
                if (Message != null)
                    TextRenderer.DrawText(g, Message, Font, ClientRectangle, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            float z = Zoom;
            PointF at = fit ? new PointF((ClientSize.Width - original.Width * z) / 2, (ClientSize.Height - original.Height * z) / 2) : offset;
            RectangleF dest = new RectangleF(at.X, at.Y, original.Width * z, original.Height * z);
            // Only the visible part is drawn: at high zoom that is a small piece of the bitmap.
            RectangleF vis = RectangleF.Intersect(dest, ClientRectangle);
            if (vis.Width > 0 && vis.Height > 0)
            {
                float k = image.Width / (float)original.Width; // bitmap pixels per picture pixel
                RectangleF src = new RectangleF((vis.X - dest.X) / z * k, (vis.Y - dest.Y) / z * k, vis.Width / z * k, vis.Height / z * k);
                bool exact = Math.Abs(vis.Width - src.Width) < 0.5f && Math.Abs(vis.Height - src.Height) < 0.5f;
                g.InterpolationMode = exact ? InterpolationMode.NearestNeighbor : z * k >= 2.5f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(image, vis, src, GraphicsUnit.Pixel);
            }
            if (ShowInfo && info != null && info.Length > 0) DrawInfo(g);
        }

        void DrawInfo(Graphics g)
        {
            int pad = Native.Px(10), lineH = TextRenderer.MeasureText("Ag", Font).Height + Native.Px(2), w = 0;
            foreach (string s in info) w = Math.Max(w, TextRenderer.MeasureText(s, Font).Width);
            string zoomLine = "Zoom " + Math.Round(Zoom * 100) + " %" + (fit ? " (fit)" : "");
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

        // Zooms by factor, keeping the picture point under the given screen point in place.
        public void ZoomBy(float factor, Point around)
        {
            if (image == null) return;
            float z = Zoom, nz = Math.Max(Math.Min(FitZoom, 0.05f), Math.Min(16f, z * factor));
            if (Math.Abs(nz - z) < 0.0001f) return;
            PointF at = fit ? new PointF((ClientSize.Width - original.Width * z) / 2, (ClientSize.Height - original.Height * z) / 2) : offset;
            float px = (around.X - at.X) / z, py = (around.Y - at.Y) / z;
            zoom = nz;
            fit = false;
            offset = new PointF(around.X - px * nz, around.Y - py * nz);
            Clamp();
            Invalidate();
            Changed();
        }

        // 100 % at the point (or the centre), or back to fit when already there.
        public void ToggleActualSize(Point around)
        {
            if (image == null) return;
            if (!fit && Math.Abs(zoom - 1f) < 0.001f) { FitToWindow(); return; }
            ZoomBy(1f / Zoom, around);
            if (fit) { fit = false; zoom = 1f; Clamp(); Invalidate(); Changed(); }
        }

        public void FitToWindow()
        {
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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!fit) Clamp();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ZoomBy(e.Delta > 0 ? 1.25f : 0.8f, e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) ToggleActualSize(e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || fit || image == null) return;
            dragging = true;
            dragFrom = e.Location;
            offsetFrom = offset;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) { Cursor = fit || image == null ? Cursors.Default : Cursors.Hand; return; }
            offset = new PointF(offsetFrom.X + e.X - dragFrom.X, offsetFrom.Y + e.Y - dragFrom.Y);
            Clamp();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            Cursor = fit || image == null ? Cursors.Default : Cursors.Hand;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && image != null) { image.Dispose(); image = null; }
            base.Dispose(disposing);
        }
    }
}
