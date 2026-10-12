// OrclFX: test hook (DUALPANE_TEST_SV step "sim"): a user session in the Space Viewer played with real window
// messages posted to it (mouse wheel, drags, clicks, double-clicks, keys; nothing touches the real mouse or keyboard),
// checking after each step what the viewer shows. Each line of the trace says "sim ok" or "sim FAIL".
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OrclFileExplorer
{
    partial class QuickLook
    {
        const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203,
            WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_MOUSEWHEEL = 0x20A;

        static IntPtr XY(int x, int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }

        void Post(int msg, int wparam, Point client)
        {
            Native.PostMessage(image.Handle, msg, (IntPtr)wparam, XY(client.X, client.Y));
        }

        void Wheel(int notches, Point client)
        {
            Point s = image.PointToScreen(client);
            Native.PostMessage(image.Handle, WM_MOUSEWHEEL, (IntPtr)((notches * 120) << 16), XY(s.X, s.Y));
        }

        void Key(Keys k)
        {
            Native.PostMessage(image.Handle, WM_KEYDOWN, (IntPtr)(int)k, (IntPtr)1);
            Native.PostMessage(image.Handle, WM_KEYUP, (IntPtr)(int)k, unchecked((IntPtr)(int)0xC0000001));
        }

        // A drag with the button (down, a few moves, up).
        void Drag(MouseButtons b, Point from, Size by)
        {
            int down = b == MouseButtons.Left ? WM_LBUTTONDOWN : b == MouseButtons.Right ? WM_RBUTTONDOWN : WM_MBUTTONDOWN;
            int up = b == MouseButtons.Left ? WM_LBUTTONUP : b == MouseButtons.Right ? WM_RBUTTONUP : WM_MBUTTONUP;
            int flag = b == MouseButtons.Left ? 1 : b == MouseButtons.Right ? 2 : 0x10;
            Post(down, flag, from);
            for (int i = 1; i <= 5; i++) Post(WM_MOUSEMOVE, flag, new Point(from.X + by.Width * i / 5, from.Y + by.Height * i / 5));
            Post(up, 0, from + by);
        }

        void SimClick(MouseButtons b, Point at) { Drag(b, at, Size.Empty); }

        void SimDoubleClick(Point at)
        {
            Post(WM_LBUTTONDOWN, 1, at);
            Post(WM_LBUTTONUP, 0, at);
            Post(WM_LBUTTONDBLCLK, 1, at);
            Post(WM_LBUTTONUP, 0, at);
        }

        internal void TestSimulate()
        {
            List<KeyValuePair<string, Action>> steps = new List<KeyValuePair<string, Action>>();
            Func<Point> mid = delegate { return image.Centre; };
            Func<Point> spot = delegate { return new Point(image.Width * 11 / 20, image.Height * 9 / 20); };
            string firstFile = path;
            PointF under = PointF.Empty;
            Point probe = Point.Empty;
            float zoomBefore = 0;
            PointF offsetBefore = PointF.Empty;
            Action<string, Action> step = delegate(string what, Action a) { steps.Add(new KeyValuePair<string, Action>(what, a)); };
            Action<bool, string> check = delegate(bool ok, string what) { Program.Trace("sim " + (ok ? "ok" : "FAIL") + ": " + what + "  [" + TestState + ", " + image.TestView + "]"); };

            step("zoom in at the centre first (so the picture is bigger than the window both ways)", delegate { Wheel(3, mid()); });
            step("wheel up 2 notches at the pointer", delegate
            {
                probe = spot();
                under = image.TestPictureAt(probe);
                zoomBefore = image.Zoom;
                Wheel(2, probe);
            });
            step("check", delegate
            {
                PointF now = image.TestPictureAt(probe);
                check(image.Zoom > zoomBefore * 1.4f && Math.Abs(now.X - under.X) < 3 && Math.Abs(now.Y - under.Y) < 3, "zoomed in, the point under the pointer stayed put (" + under + " -> " + now + ")");
            });
            step("zoom in more at the centre (room to move around)", delegate { Wheel(3, mid()); });
            step("left drag by (-150, -80)", delegate { offsetBefore = image.TestOffset; Drag(MouseButtons.Left, mid(), new Size(-150, -80)); });
            step("check", delegate
            {
                PointF o = image.TestOffset;
                check(Math.Abs(o.X - offsetBefore.X + 150) < 2 && Math.Abs(o.Y - offsetBefore.Y + 80) < 2, "picture moved with the mouse");
            });
            step("right drag (not allowed by default)", delegate { offsetBefore = image.TestOffset; Drag(MouseButtons.Right, mid(), new Size(100, 60)); });
            step("check", delegate { check(image.TestOffset == offsetBefore, "right drag left it in place"); });
            step("allow right drag only, right drag by (100, 60)", delegate
            {
                ViewerSettings.DragButtons = MouseButtons.Right;
                offsetBefore = image.TestOffset;
                Drag(MouseButtons.Right, mid(), new Size(100, 60));
            });
            step("check", delegate
            {
                PointF o = image.TestOffset;
                check(Math.Abs(o.X - offsetBefore.X - 100) < 2 && Math.Abs(o.Y - offsetBefore.Y - 60) < 2, "right drag moved it");
            });
            step("left drag (now not allowed)", delegate { offsetBefore = image.TestOffset; Drag(MouseButtons.Left, mid(), new Size(-100, -60)); });
            step("check", delegate { check(image.TestOffset == offsetBefore, "left drag left it in place"); ViewerSettings.DragButtons = MouseButtons.Left; });
            step("key 0: fit", delegate { Key(Keys.D0); });
            step("check", delegate { check(image.IsFit, "fit"); });
            step("Up arrow twice", delegate { zoomBefore = image.Zoom; Key(Keys.Up); Key(Keys.Up); });
            step("check", delegate { check(Math.Abs(image.Zoom - zoomBefore * 1.5625f) < 0.01f, "zoomed in two steps at the centre"); });
            step("Down arrow twice", delegate { Key(Keys.Down); Key(Keys.Down); });
            step("check", delegate { check(Math.Abs(image.Zoom - zoomBefore) < 0.01f, "back to where it was"); });
            step("Right arrow", delegate { Key(Keys.Right); });
            step("check", delegate { check(!string.Equals(path, firstFile, StringComparison.OrdinalIgnoreCase), "next picture: " + System.IO.Path.GetFileName(path)); });
            step("Left arrow", delegate { Key(Keys.Left); });
            step("check", delegate { check(string.Equals(path, firstFile, StringComparison.OrdinalIgnoreCase), "previous picture: " + System.IO.Path.GetFileName(path)); });
            step("middle click: full screen", delegate { SimClick(MouseButtons.Middle, mid()); });
            step("check", delegate { check(fullScreen && !thumbs.Visible && image.IsFit, "full screen, no thumbnails, fitted"); });
            step("wheel down in full screen", delegate { Wheel(1, mid()); });
            step("check", delegate { check(fullScreen && !image.IsFit, "zoomed in full screen"); });
            step("middle drag (not allowed): a click only if it doesn't move", delegate { Drag(MouseButtons.Middle, mid(), new Size(80, 0)); });
            step("check", delegate { check(fullScreen, "a middle drag isn't a click: still full screen"); });
            step("middle click: leave full screen", delegate { SimClick(MouseButtons.Middle, mid()); });
            step("check", delegate { check(!fullScreen && thumbs.Visible == ShowThumbs, "back, thumbnails as before"); });
            step("double-click: full screen", delegate { SimDoubleClick(mid()); });
            step("check", delegate { check(fullScreen, "full screen"); });
            step("Esc", delegate { Key(Keys.Escape); });
            step("check", delegate { check(!fullScreen && !IsDisposed, "left full screen, viewer still open"); });
            step("full screen off by middle click is turned off; F, then middle click", delegate { ViewerSettings.ExitMiddle = false; Key(Keys.F); });
            step("middle click", delegate { SimClick(MouseButtons.Middle, mid()); });
            step("check", delegate { check(fullScreen, "middle click didn't leave full screen"); ViewerSettings.ExitMiddle = true; Key(Keys.Escape); });
            step("check", delegate { check(!fullScreen, "Esc left it"); });
            step("wheel set to step through pictures; wheel down", delegate { ViewerSettings.Wheel = ViewerSettings.WheelStep; zoomBefore = image.Zoom; Wheel(-1, mid()); });
            step("check", delegate { check(!string.Equals(path, firstFile, StringComparison.OrdinalIgnoreCase), "wheel went to the next picture: " + System.IO.Path.GetFileName(path)); });
            step("wheel set to zoom at the centre", delegate { ViewerSettings.Wheel = ViewerSettings.WheelZoomCentre; Key(Keys.D0); });
            step("wheel up at a corner", delegate { probe = mid(); under = image.TestPictureAt(probe); Wheel(1, new Point(20, 20)); });
            step("check", delegate
            {
                PointF now = image.TestPictureAt(probe);
                check(Math.Abs(now.X - under.X) < 3 && Math.Abs(now.Y - under.Y) < 3, "zoomed at the centre, not the pointer");
                ViewerSettings.Wheel = ViewerSettings.WheelZoomPointer;
            });
            step("double-click with full screen clicks off: 100 %", delegate { ViewerSettings.FullScreenClicks = false; Key(Keys.D0); });
            step("double-click", delegate { SimDoubleClick(mid()); });
            step("check", delegate { check(!fullScreen && Math.Abs(image.Zoom - 1) < 0.01f, "100 %"); ViewerSettings.FullScreenClicks = true; });
            step("info box, thumbnails pane off and on", delegate { Key(Keys.I); Key(Keys.T); });
            step("check", delegate { check(!thumbs.Visible && image.ShowInfo, "thumbnails off, info on"); Key(Keys.T); Key(Keys.I); });
            step("check", delegate { check(thumbs.Visible && !image.ShowInfo, "thumbnails on, info off"); });
            step("smooth zoom off, wheel", delegate { ViewerSettings.Smooth = false; zoomBefore = image.Zoom; Wheel(1, mid()); });
            step("check", delegate { check(image.Zoom > zoomBefore, "zoomed without gliding"); ViewerSettings.Smooth = true; });
            step("graphics card off", delegate { ViewerSettings.Gpu = false; image.SettingsChanged(); Wheel(1, mid()); });
            step("check", delegate { check(image.DrawingStatus.StartsWith("the processor"), "drawn by " + image.DrawingStatus); ViewerSettings.Gpu = true; image.SettingsChanged(); Wheel(-1, mid()); });
            step("check", delegate { check(image.DrawingStatus.StartsWith("the graphics card"), "drawn by " + image.DrawingStatus); });
            step("End, wrap on, Right", delegate { ViewerSettings.Wrap = true; Key(Keys.End); Key(Keys.Right); });
            step("check", delegate { check(files != null && string.Equals(path, files[0], StringComparison.OrdinalIgnoreCase), "after the last, the first"); ViewerSettings.Wrap = false; });

            step("mouse buttons step: right click", delegate { ViewerSettings.ClickSteps = true; Key(Keys.Home); });
            string beforeClick = null;
            step("right click", delegate { beforeClick = path; SimClick(MouseButtons.Right, mid()); });
            step("check", delegate { check(files.FindIndex(Same(path)) == files.FindIndex(Same(beforeClick)) + 1, "right click: next picture " + System.IO.Path.GetFileName(path)); });
            step("left click (waits for a possible double-click)", delegate { beforeClick = path; SimClick(MouseButtons.Left, mid()); });
            step("wait", delegate { });
            step("check", delegate { check(files.FindIndex(Same(path)) == files.FindIndex(Same(beforeClick)) - 1, "left click: previous picture " + System.IO.Path.GetFileName(path)); });
            step("double-click: full screen, not previous", delegate { beforeClick = path; SimDoubleClick(mid()); });
            step("wait", delegate { });
            step("check", delegate
            {
                check(fullScreen && string.Equals(path, beforeClick, StringComparison.OrdinalIgnoreCase), "full screen, same picture");
                Key(Keys.Escape);
                ViewerSettings.ClickSteps = false;
            });
            step("check", delegate { check(!fullScreen, "left full screen"); });
            step("double-click with full screen clicks off and mouse-button stepping on: 100 %, no step", delegate
            {
                ViewerSettings.ClickSteps = true; ViewerSettings.FullScreenClicks = false; image.FitToWindow();
                beforeClick = path; SimDoubleClick(mid());
            });
            step("wait", delegate { });
            step("check", delegate
            {
                check(string.Equals(path, beforeClick, StringComparison.OrdinalIgnoreCase) && Math.Abs(image.Zoom - 1) < 0.01f, "100 % on the same picture");
                ViewerSettings.ClickSteps = false; ViewerSettings.FullScreenClicks = true; image.FitToWindow();
            });

            // Touch (the gestures as Windows reports them, handed to the view's handler).
            Func<int, int, Point, int, Gestures.Info> gesture = delegate(int id, int flags, Point client, int arg)
            {
                Gestures.Info g = new Gestures.Info();
                g.Id = id; g.Flags = flags; g.Screen = image.PointToScreen(client); g.Argument = arg;
                return g;
            };
            step("touch: two-finger tap fits; pinch out (fingers 200 -> 300 px apart) at a point", delegate
            {
                image.Touch(gesture(Gestures.GID_TWOFINGERTAP, 0, mid(), 0));
                probe = spot();
                under = image.TestPictureAt(probe);
                zoomBefore = image.Zoom;
                image.Touch(gesture(Gestures.GID_BEGIN, 0, mid(), 0)); // (Windows starts every gesture so)
                image.Touch(gesture(Gestures.GID_ZOOM, Gestures.GF_BEGIN, probe, 200));
                image.Touch(gesture(Gestures.GID_ZOOM, 0, probe, 250));
                image.Touch(gesture(Gestures.GID_ZOOM, 0, probe, 300));
                PointF during = image.TestPictureAt(probe);
                Program.Trace("sim " + (Math.Abs(during.X - under.X) < 3 && Math.Abs(during.Y - under.Y) < 3 ? "ok" : "FAIL") + ": while pinching the point between the fingers stays put (" + under + " -> " + during + ")");
                image.Touch(gesture(Gestures.GID_ZOOM, Gestures.GF_END, probe, 300));
                image.Touch(gesture(Gestures.GID_END, 0, mid(), 0));
            });
            step("check", delegate
            {
                check(Math.Abs(image.Zoom - zoomBefore * 1.5f) < 0.001f, "pinch zoomed 1.5x (" + zoomBefore + " -> " + image.Zoom + "), settled inside the window");
            });
            step("touch: a finger moves the zoomed picture by (+40, +60)", delegate
            {
                offsetBefore = image.TestOffset;
                Point a = mid();
                image.Touch(gesture(Gestures.GID_BEGIN, 0, mid(), 0)); // (Windows starts every gesture so)
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_BEGIN, a, 0));
                image.Touch(gesture(Gestures.GID_PAN, 0, new Point(a.X + 20, a.Y + 30), 0));
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_END, new Point(a.X + 40, a.Y + 60), 0));
                image.Touch(gesture(Gestures.GID_END, 0, mid(), 0));
            });
            step("check", delegate
            {
                PointF o = image.TestOffset;
                check(Math.Abs(o.X - offsetBefore.X - 40) < 2 && Math.Abs(o.Y - offsetBefore.Y - 60) < 2, "picture followed the finger (" + offsetBefore + " -> " + o + ")");
            });
            step("touch: pinch in below fit springs back to fit", delegate
            {
                Point a = mid();
                image.Touch(gesture(Gestures.GID_BEGIN, 0, mid(), 0)); // (Windows starts every gesture so)
                image.Touch(gesture(Gestures.GID_ZOOM, Gestures.GF_BEGIN, a, 400));
                image.Touch(gesture(Gestures.GID_ZOOM, 0, a, 100));
                image.Touch(gesture(Gestures.GID_ZOOM, Gestures.GF_END, a, 60));
                image.Touch(gesture(Gestures.GID_END, 0, mid(), 0));
            });
            step("check", delegate { check(image.IsFit, "fitted again"); });
            string beforeSwipe = null;
            step("touch: swipe left", delegate
            {
                beforeSwipe = path;
                Point a = mid();
                image.Touch(gesture(Gestures.GID_BEGIN, 0, mid(), 0)); // (Windows starts every gesture so)
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_BEGIN, a, 0));
                image.Touch(gesture(Gestures.GID_PAN, 0, new Point(a.X - 150, a.Y + 10), 0));
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_INERTIA, new Point(a.X - 300, a.Y + 15), 0));
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_INERTIA, new Point(a.X - 400, a.Y + 15), 0));
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_END, new Point(a.X - 420, a.Y + 15), 0));
                image.Touch(gesture(Gestures.GID_END, 0, mid(), 0));
            });
            step("check", delegate
            {
                int i0 = files.FindIndex(Same(beforeSwipe)), i1 = files.FindIndex(Same(path));
                check(i1 == i0 + 1, "next picture (one step, not one per inertia step): " + System.IO.Path.GetFileName(path));
            });
            step("touch: swipe right", delegate
            {
                beforeSwipe = path;
                Point a = mid();
                image.Touch(gesture(Gestures.GID_BEGIN, 0, mid(), 0)); // (Windows starts every gesture so)
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_BEGIN, a, 0));
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_END, new Point(a.X + 300, a.Y - 20), 0));
                image.Touch(gesture(Gestures.GID_END, 0, mid(), 0));
            });
            step("check", delegate
            {
                int i0 = files.FindIndex(Same(beforeSwipe)), i1 = files.FindIndex(Same(path));
                check(i1 == i0 - 1, "previous picture: " + System.IO.Path.GetFileName(path));
            });
            step("touch: a short or mostly vertical swipe does nothing", delegate
            {
                beforeSwipe = path;
                Point a = mid();
                image.Touch(gesture(Gestures.GID_BEGIN, 0, mid(), 0)); // (Windows starts every gesture so)
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_BEGIN, a, 0));
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_END, new Point(a.X - 20, a.Y), 0));
                image.Touch(gesture(Gestures.GID_END, 0, mid(), 0));
                image.Touch(gesture(Gestures.GID_BEGIN, 0, mid(), 0)); // (Windows starts every gesture so)
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_BEGIN, a, 0));
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_END, new Point(a.X - 200, a.Y + 250), 0));
                image.Touch(gesture(Gestures.GID_END, 0, mid(), 0));
            });
            step("check", delegate { check(string.Equals(path, beforeSwipe, StringComparison.OrdinalIgnoreCase), "stayed on " + System.IO.Path.GetFileName(path)); });
            step("touch: a pinch on a fitted picture whose fingers drift sideways", delegate
            {
                beforeSwipe = path;
                image.FitToWindow();
                Point a = mid();
                image.Touch(gesture(Gestures.GID_BEGIN, 0, a, 0));
                image.Touch(gesture(Gestures.GID_ZOOM, Gestures.GF_BEGIN, a, 200));
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_BEGIN, a, 200));
                image.Touch(gesture(Gestures.GID_ZOOM, 0, new Point(a.X - 150, a.Y), 230));
                image.Touch(gesture(Gestures.GID_PAN, Gestures.GF_END, new Point(a.X - 300, a.Y), 0));
                image.Touch(gesture(Gestures.GID_ZOOM, Gestures.GF_END, new Point(a.X - 300, a.Y), 240));
                image.Touch(gesture(Gestures.GID_END, 0, a, 0));
            });
            step("check", delegate { check(string.Equals(path, beforeSwipe, StringComparison.OrdinalIgnoreCase) && !image.IsFit, "zoomed, not a swipe: still " + System.IO.Path.GetFileName(path)); });

            int at = 0;
            Timer t = new Timer();
            t.Interval = 450;
            t.Tick += delegate
            {
                if (IsDisposed || at >= steps.Count) { t.Stop(); t.Dispose(); Program.Trace("sim done"); return; }
                KeyValuePair<string, Action> s = steps[at++];
                if (s.Key != "check") Program.Trace("sim step: " + s.Key);
                try { s.Value(); }
                catch (Exception ex) { Program.Trace("sim FAIL: " + s.Key + ": " + ex); }
            };
            t.Start();
        }
    }
}
