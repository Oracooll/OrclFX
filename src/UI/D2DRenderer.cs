// OrclFX: draws the Space Viewer's picture with the graphics card (Direct2D, part of Windows since 7): the picture
// is kept in video memory and the card scales it for each frame, which is what makes zooming glide. Only the few
// Direct2D calls the viewer needs, called through their COM interfaces' tables. When the card can't do it (no
// hardware drawing, a remote session, a driver problem), Create returns null or a call fails, and ImageView draws
// with GDI+ instead.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OrclFileExplorer
{
    sealed class D2DRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct PixelFormatD { public int Format, Alpha; }
        [StructLayout(LayoutKind.Sequential)] struct SizeU { public uint W, H; }
        [StructLayout(LayoutKind.Sequential)] public struct RectF { public float L, T, R, B; public RectF(RectangleF r) { L = r.Left; T = r.Top; R = r.Right; B = r.Bottom; } }
        [StructLayout(LayoutKind.Sequential)] struct ColorF { public float R, G, B, A; }
        [StructLayout(LayoutKind.Sequential)]
        struct RenderTargetProps { public int Type; public PixelFormatD Pixel; public float DpiX, DpiY; public int Usage, MinLevel; }
        [StructLayout(LayoutKind.Sequential)]
        struct HwndProps { public IntPtr Hwnd; public SizeU Size; public int Present; }
        [StructLayout(LayoutKind.Sequential)]
        struct BitmapProps { public PixelFormatD Pixel; public float DpiX, DpiY; }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateHwndTargetFn(IntPtr self, ref RenderTargetProps rt, ref HwndProps hwnd, out IntPtr target);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateBitmapFn(IntPtr self, SizeU size, IntPtr data, uint pitch, ref BitmapProps props, out IntPtr bitmap);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DrawBitmapFn(IntPtr self, IntPtr bitmap, ref RectF dest, float opacity, int interpolation, ref RectF src);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void ClearFn(IntPtr self, ref ColorF color);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void BeginDrawFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EndDrawFn(IntPtr self, IntPtr tag1, IntPtr tag2);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint MaxBitmapFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ResizeFn(IntPtr self, ref SizeU size);

        [DllImport("d2d1.dll")] static extern int D2D1CreateFactory(int type, ref Guid iid, IntPtr options, out IntPtr factory);

        const int ID2D1Factory_CreateHwndRenderTarget = 14;
        const int RT_CreateBitmap = 4, RT_DrawBitmap = 26, RT_Clear = 47, RT_BeginDraw = 48, RT_EndDraw = 49, RT_GetMaximumBitmapSize = 55, Hwnd_Resize = 58;
        const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);

        static T Method<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr table = Marshal.ReadIntPtr(obj);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, slot * IntPtr.Size), typeof(T));
        }

        static void Release(IntPtr obj) { if (obj != IntPtr.Zero) Method<ReleaseFn>(obj, 2)(obj); }

        IntPtr factory, target;
        readonly IntPtr hwnd;
        Size size;
        CreateBitmapFn createBitmap;
        DrawBitmapFn drawBitmap;
        ClearFn clear;
        BeginDrawFn beginDraw;
        EndDrawFn endDraw;
        ResizeFn resize;
        uint maxBitmap;
        // The pictures already in video memory, by the GDI+ bitmap they came from.
        readonly Dictionary<Bitmap, IntPtr> uploaded = new Dictionary<Bitmap, IntPtr>();

        public bool Failed { get; private set; }
        int remakeTries;
        public string Problem { get; private set; }

        D2DRenderer(IntPtr hwnd) { this.hwnd = hwnd; }

        // A renderer for the window, or null (with the reason) when the graphics card can't draw it.
        public static D2DRenderer Create(IntPtr hwnd, Size size, out string problem)
        {
            problem = null;
            if (System.Windows.Forms.SystemInformation.TerminalServerSession) { problem = "remote session"; return null; }
            D2DRenderer r = new D2DRenderer(hwnd);
            try
            {
                Guid iid = new Guid("06152247-6f50-465a-9245-118bfd3b6007"); // ID2D1Factory
                int hr = D2D1CreateFactory(0, ref iid, IntPtr.Zero, out r.factory); // single-threaded
                if (hr < 0) { problem = "Direct2D unavailable (0x" + hr.ToString("X8") + ")"; return null; }
                r.size = size;
                if (!r.MakeTarget(out problem)) { r.Dispose(); return null; }
                return r;
            }
            catch (Exception ex) { problem = ex.Message; r.Dispose(); return null; }
        }

        bool MakeTarget(out string problem)
        {
            problem = null;
            RenderTargetProps rt = new RenderTargetProps();
            rt.Type = 2; // hardware only: without the graphics card GDI+ is the better choice
            rt.Pixel.Format = 87; // DXGI_FORMAT_B8G8R8A8_UNORM
            rt.Pixel.Alpha = 3;   // ignore (the window is opaque)
            rt.DpiX = rt.DpiY = 96; // units are pixels
            HwndProps hp = new HwndProps();
            hp.Hwnd = hwnd;
            hp.Size.W = (uint)Math.Max(1, size.Width);
            hp.Size.H = (uint)Math.Max(1, size.Height);
            // Not waiting for the screen here (that took two or more refreshes a frame): ImageView paces the frames
            // itself with DwmFlush, one each time Windows puts the screen together.
            hp.Present = 2;
            int hr = Method<CreateHwndTargetFn>(factory, ID2D1Factory_CreateHwndRenderTarget)(factory, ref rt, ref hp, out target);
            if (hr < 0 || target == IntPtr.Zero) { problem = "no hardware drawing (0x" + hr.ToString("X8") + ")"; target = IntPtr.Zero; return false; }
            createBitmap = Method<CreateBitmapFn>(target, RT_CreateBitmap);
            drawBitmap = Method<DrawBitmapFn>(target, RT_DrawBitmap);
            clear = Method<ClearFn>(target, RT_Clear);
            beginDraw = Method<BeginDrawFn>(target, RT_BeginDraw);
            endDraw = Method<EndDrawFn>(target, RT_EndDraw);
            resize = Method<ResizeFn>(target, Hwnd_Resize);
            maxBitmap = Method<MaxBitmapFn>(target, RT_GetMaximumBitmapSize)(target);
            return true;
        }

        void DropTarget()
        {
            foreach (IntPtr b in uploaded.Values) Release(b);
            uploaded.Clear();
            refused.Clear();
            Release(target);
            target = IntPtr.Zero;
        }

        public void Resize(Size s)
        {
            size = s;
            if (target == IntPtr.Zero) return;
            SizeU u = new SizeU();
            u.W = (uint)Math.Max(1, s.Width);
            u.H = (uint)Math.Max(1, s.Height);
            resize(target, ref u);
        }

        // Pictures the card refused (out of video memory, say): GDI+ draws those.
        readonly HashSet<Bitmap> refused = new HashSet<Bitmap>();

        // Fits in one video-memory picture, and wasn't refused (bigger ones are drawn by GDI+).
        public bool CanHold(Bitmap b) { return b.Width <= maxBitmap && b.Height <= maxBitmap && !refused.Contains(b); }

        IntPtr Upload(Bitmap b)
        {
            IntPtr d;
            if (uploaded.TryGetValue(b, out d)) return d;
            DateTime began = DateTime.UtcNow;
            Rectangle all = new Rectangle(0, 0, b.Width, b.Height);
            BitmapData bits = b.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                BitmapProps bp = new BitmapProps();
                bp.Pixel.Format = 87;
                bp.Pixel.Alpha = 1; // premultiplied
                bp.DpiX = bp.DpiY = 96;
                SizeU s = new SizeU();
                s.W = (uint)b.Width;
                s.H = (uint)b.Height;
                int hr = createBitmap(target, s, bits.Scan0, (uint)bits.Stride, ref bp, out d);
                if (hr < 0) throw new ExternalException("Direct2D couldn't take the picture", hr);
            }
            finally { b.UnlockBits(bits); }
            uploaded[b] = d;
            Program.Trace("direct2d: picture " + b.Width + "x" + b.Height + " taken in " + (DateTime.UtcNow - began).TotalMilliseconds.ToString("0") + " ms");
            return d;
        }

        // A bitmap that's gone (disposed by the viewer) leaves video memory.
        public void Forget(Bitmap b)
        {
            IntPtr d;
            if (b != null && uploaded.TryGetValue(b, out d)) { Release(d); uploaded.Remove(b); }
            if (b != null) refused.Remove(b);
        }

        public void ForgetAllBut(ICollection<Bitmap> keep)
        {
            List<Bitmap> gone = new List<Bitmap>();
            foreach (Bitmap b in uploaded.Keys) if (!keep.Contains(b)) gone.Add(b);
            foreach (Bitmap b in gone) Forget(b);
        }

        public struct Layer
        {
            public Bitmap Bitmap;
            public RectangleF Dest, Src;
            public bool Nearest;
        }

        static bool DeviceGone(int hr) { return hr == D2DERR_RECREATE_TARGET || hr == unchecked((int)0x887A0005) || hr == unchecked((int)0x887A0007); }

        // One frame: the background, then each layer (the picture, the info box …). False when GDI+ has to draw
        // this frame (Failed: and the ones after it).
        public bool Draw(Color back, IList<Layer> layers)
        {
            if (Failed) return false;
            try
            {
                if (target == IntPtr.Zero)
                {
                    // Again after the card was reset: it may still be coming back (just after waking up, during a
                    // driver update), so a few frames' tries before giving up on it.
                    string p;
                    if (!MakeTarget(out p)) { if (++remakeTries > 30) Fail(p); return false; }
                }
                remakeTries = 0;
                // The pictures into video memory first (outside BeginDraw/EndDraw).
                foreach (Layer l in layers)
                {
                    if (refused.Contains(l.Bitmap)) return false; // (not tried again every frame)
                    if (uploaded.ContainsKey(l.Bitmap)) continue;
                    try { Upload(l.Bitmap); }
                    catch (Exception ex)
                    {
                        int code = ex is ExternalException ? ((ExternalException)ex).ErrorCode : ex.HResult;
                        if (DeviceGone(code)) { Program.Trace("direct2d: device lost, recreating"); DropTarget(); return false; }
                        // Just this picture (too big for the card's memory, say): GDI+ draws it.
                        refused.Add(l.Bitmap);
                        Program.Trace("direct2d: picture " + l.Bitmap.Width + "x" + l.Bitmap.Height + " refused (0x" + code.ToString("X8") + "); GDI+ draws it");
                        return false;
                    }
                }
                beginDraw(target);
                ColorF c = new ColorF();
                c.R = back.R / 255f; c.G = back.G / 255f; c.B = back.B / 255f; c.A = 1;
                clear(target, ref c);
                foreach (Layer l in layers)
                {
                    IntPtr bmp = Upload(l.Bitmap);
                    RectF dest = new RectF(l.Dest), src = new RectF(l.Src);
                    drawBitmap(target, bmp, ref dest, 1f, l.Nearest ? 0 : 1, ref src);
                }
                int hr = endDraw(target, IntPtr.Zero, IntPtr.Zero);
                if (DeviceGone(hr))
                {
                    // The graphics card was reset (driver update, sleep …): GDI+ draws this frame, and everything is
                    // made again for the next one.
                    Program.Trace("direct2d: target lost, recreating");
                    DropTarget();
                    return false;
                }
                if (hr < 0) { Fail("drawing failed (0x" + hr.ToString("X8") + ")"); return false; }
                return true;
            }
            catch (Exception ex) { Fail(ex.Message); return false; }
        }

        void Fail(string why)
        {
            Failed = true;
            Problem = why;
            Program.Trace("direct2d: " + why + "; GDI+ draws from now on");
            try { DropTarget(); } catch { }
        }

        public void Dispose()
        {
            try { DropTarget(); } catch { }
            Release(factory);
            factory = IntPtr.Zero;
        }
    }
}
