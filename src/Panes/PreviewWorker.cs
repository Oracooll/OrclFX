// OrclFX: runs preview handlers and thumbnail extraction on their own STA thread.
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace OrclFileExplorer
{
    // Everything that can block while previewing (file and network checks, preview handlers, thumbnails)
    // runs here, so a slow or hung handler, file or drive never freezes the window. One thread, and only
    // the newest request matters: a request still waiting when a newer one arrives is dropped. If the
    // thread gets stuck, PreviewPane abandons it and starts a fresh one; an abandoned worker cleans up
    // and exits whenever its stuck call returns.
    class PreviewWorker
    {
        public class Request
        {
            public int Ticket;
            public string Path = "";
            public IntPtr Host;        // window the handler draws into
            public RECT Rect;
            public SIZE ThumbSize;
            public uint Back, Text;    // theme colours for handlers that support them
            public bool ThumbOnly;     // only ask again for the real thumbnail (the shell gave its icon before)
        }

        public class Result
        {
            public PreviewWorker From;
            public int Ticket;
            public bool Handler;       // a preview handler is showing the file
            public Bitmap Thumbnail;   // otherwise the thumbnail, or null
            public string Text, TextNote; // or the file as text (see TextPreview), with a description
            public bool IsIcon;        // the thumbnail is only the file's icon: the real one may come later
            public bool ThumbOnly;     // the answer to a ThumbOnly request
        }

        readonly object gate = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Action<Result> done;
        readonly Thread thread;
        Request pending;
        bool rectPending, quit;
        RECT newRect;
        long busySince = long.MaxValue;   // UTC ticks while working on a request
        volatile bool abandoned;
        IPreviewHandler handler;          // used only on the worker thread

        public PreviewWorker(Action<Result> done)
        {
            this.done = done;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public bool IsAlive { get { return thread.IsAlive; } }
        public bool HasWaitingRequest { get { lock (gate) return pending != null; } }

        // How long the current request has been running, or zero when idle.
        public TimeSpan Busy
        {
            get
            {
                long since = Interlocked.Read(ref busySince);
                return since == long.MaxValue ? TimeSpan.Zero : TimeSpan.FromTicks(DateTime.UtcNow.Ticks - since);
            }
        }

        public void Submit(Request r) { lock (gate) pending = r; wake.Set(); }

        public void SetRect(RECT r)
        {
            lock (gate) { newRect = r; rectPending = true; }
            wake.Set();
        }

        // Stops after the current call: the handler is unloaded and the thread exits.
        public void Quit() { lock (gate) { quit = true; pending = null; } wake.Set(); }
        public void Abandon() { abandoned = true; Quit(); }
        public bool WaitForExit(int ms) { return thread.Join(ms); }

        void Run()
        {
            while (true)
            {
                wake.WaitOne();
                while (true)
                {
                    Request r;
                    bool doRect, stop;
                    RECT rect;
                    lock (gate)
                    {
                        r = pending; pending = null;
                        doRect = rectPending; rectPending = false; rect = newRect;
                        stop = quit;
                    }
                    if (stop) { UnloadHandler(); return; }
                    if (r != null)
                    {
                        Interlocked.Exchange(ref busySince, DateTime.UtcNow.Ticks);
                        try { Process(r); } catch { }
                        Interlocked.Exchange(ref busySince, long.MaxValue);
                        continue;
                    }
                    if (doRect && handler != null) try { handler.SetRect(ref rect); } catch { }
                    break;
                }
            }
        }

        bool Superseded() { lock (gate) return pending != null || quit; }

        void Process(Request r)
        {
            UnloadHandler();
            if (r.Path.Length == 0 || Superseded()) return;
            Result res = new Result();
            res.From = this;
            res.Ticket = r.Ticket;
            if (r.ThumbOnly)
            {
                res.ThumbOnly = true;
                res.Thumbnail = ShellImage(r.Path, r.ThumbSize, SIIGBF_THUMBNAILONLY);
                if (abandoned) { if (res.Thumbnail != null) res.Thumbnail.Dispose(); return; }
                done(res);
                return;
            }
            bool isFile = !Directory.Exists(r.Path);
            // Pictures are decoded here; plain text first too (searchable, follows the theme); other files as text
            // only when no handler shows them.
            if (isFile && Pictures.IsPictureKind(r.Path) && (res.Thumbnail = LoadPicture(r)) != null) { }
            else if (isFile && TextPreview.IsPlainTextKind(r.Path) && ReadText(r, res)) { }
            else if (isFile && TryHandler(r)) res.Handler = true;
            else if (Superseded()) return;
            else if (isFile && ReadText(r, res)) { }
            else
            {
                // The shell's thumbnail; when it isn't ready yet the shell gives the file's icon instead, and the
                // pane asks again shortly (the shell makes the thumbnail meanwhile).
                res.Thumbnail = ShellImage(r.Path, r.ThumbSize, SIIGBF_THUMBNAILONLY);
                if (res.Thumbnail == null && !Superseded())
                {
                    res.Thumbnail = ShellImage(r.Path, r.ThumbSize, 0);
                    res.IsIcon = isFile && res.Thumbnail != null;
                }
            }
            if (abandoned) { if (res.Thumbnail != null) res.Thumbnail.Dispose(); return; }
            done(res);
        }

        static bool ReadText(Request r, Result res)
        {
            try { return TextPreview.TryRead(r.Path, out res.Text, out res.TextNote); }
            catch { return false; }
        }

        bool TryHandler(Request r)
        {
            string clsid = Native.PreviewHandlerFor(Path.GetExtension(r.Path));
            if (clsid == null || Superseded()) return false;
            object o = null;
            try
            {
                // Out of process only, like File Explorer (prevhost.exe): a misbehaving handler can't crash or
                // run inside the app. Handlers that only work in process get the thumbnail instead.
                o = Native.CreateComObject(new Guid(clsid), 0x4 /* CLSCTX_LOCAL_SERVER */);
                if (o == null) return false;
                bool ok = false;
                IInitializeWithStream ws = o as IInitializeWithStream;
                System.Runtime.InteropServices.ComTypes.IStream stream;
                // STGM_READ | STGM_SHARE_DENY_NONE
                if (ws != null && Native.SHCreateStreamOnFileEx(r.Path, 0x40, 0, false, IntPtr.Zero, out stream) == 0)
                {
                    try { ok = ws.Initialize(stream, 0) == 0; }
                    finally { Marshal.ReleaseComObject(stream); } // the handler keeps its own reference if it needs one
                }
                if (!ok)
                {
                    IInitializeWithFile f = o as IInitializeWithFile;
                    if (f != null) ok = f.Initialize(r.Path, 0) == 0;
                }
                if (!ok)
                {
                    IInitializeWithItem wi = o as IInitializeWithItem;
                    IShellItem item = wi != null ? Native.ItemFromPath(r.Path) : null;
                    if (item != null)
                    {
                        try { ok = wi.Initialize(item, 0) == 0; }
                        finally { Marshal.ReleaseComObject(item); }
                    }
                }
                if (!ok || Superseded()) return false;
                handler = (IPreviewHandler)o;
                o = null;
                IPreviewHandlerVisuals v = handler as IPreviewHandlerVisuals;
                if (v != null)
                {
                    v.SetBackgroundColor(r.Back);
                    v.SetTextColor(r.Text);
                }
                RECT rect = r.Rect;
                lock (gate) if (rectPending) { rect = newRect; rectPending = false; }
                if (handler.SetWindow(r.Host, ref rect) != 0 || handler.DoPreview() != 0) { UnloadHandler(); return false; }
                return true;
            }
            catch
            {
                UnloadHandler();
                return false;
            }
            finally
            {
                if (o != null) try { Marshal.FinalReleaseComObject(o); } catch { }
            }
        }

        void UnloadHandler()
        {
            if (handler == null) return;
            try { handler.Unload(); } catch { }
            try { Marshal.FinalReleaseComObject(handler); } catch { }
            handler = null;
        }

        // A thumbnail of at most size x size for the Space Viewer's thumbnail pane (call on an STA thread): the
        // shell's, else the picture decoded here, else the file's icon.
        public static Bitmap ThumbnailFor(string path, int size)
        {
            SIZE s = new SIZE();
            s.cx = s.cy = size;
            Bitmap b = ShellImage(path, s, SIIGBF_THUMBNAILONLY);
            if (b == null && Pictures.IsPictureKind(path))
                try { b = Pictures.Load(path, new Size(size, size)); } catch { }
            return b ?? ShellImage(path, s, 0);
        }

        static Bitmap LoadPicture(Request r)
        {
            try { return Pictures.Load(r.Path, new Size(r.ThumbSize.cx, r.ThumbSize.cy)); }
            catch { return null; } // not a picture GDI+ can read: the shell's thumbnail instead
        }

        const int SIIGBF_THUMBNAILONLY = 0x8;

        // flags: 0 = the thumbnail, or the icon when there's none; SIIGBF_THUMBNAILONLY = no icon instead.
        static Bitmap ShellImage(string path, SIZE size, int flags)
        {
            IShellItem item = null;
            try
            {
                item = Native.ItemFromPath(path);
                IShellItemImageFactory fac = item as IShellItemImageFactory;
                IntPtr hbmp;
                if (fac != null && fac.GetImage(size, (uint)flags, out hbmp) == 0 && hbmp != IntPtr.Zero)
                {
                    try { return PreviewPane.BitmapWithAlpha(hbmp); }
                    finally { Native.DeleteObject(hbmp); }
                }
            }
            catch { }
            finally { if (item != null) try { Marshal.ReleaseComObject(item); } catch { } }
            return null;
        }
    }
}
