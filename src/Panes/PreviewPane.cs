// Orcl File Explorer: Preview pane: preview handlers, thumbnails and folder sizes.
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
    class PreviewPane : Panel
    {
        readonly Label header = new Label(), message = new Label();
        readonly Panel host = new Panel();
        readonly PictureBox picture = new PictureBox();
        readonly ListView sizes = new ListView();
        IPreviewHandler handler;
        string current = "";
        SizeJob shownJob;
        public SizeJob ShownJob { get { return shownJob; } }

        public PreviewPane()
        {
            Dock = DockStyle.Fill;
            header.Dock = DockStyle.Top;
            header.Height = Native.Px(24);
            header.Padding = new Padding(Native.Px(8), 0, Native.Px(8), 0);
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.AutoEllipsis = true;
            header.UseMnemonic = false;
            header.Text = "Preview";
            host.Dock = DockStyle.Fill;
            host.Resize += delegate { Layout2(); };
            picture.Dock = DockStyle.Fill;
            picture.SizeMode = PictureBoxSizeMode.Zoom;
            picture.Visible = false;
            message.Dock = DockStyle.Fill;
            message.TextAlign = ContentAlignment.MiddleCenter;
            message.Text = "Select a file to preview";
            sizes.Dock = DockStyle.Fill;
            sizes.View = View.Details;
            sizes.BorderStyle = BorderStyle.None;
            sizes.FullRowSelect = true;
            sizes.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            sizes.Columns.Add("Folder", Native.Px(170));
            sizes.Columns.Add("Size", Native.Px(80), HorizontalAlignment.Right);
            sizes.Columns.Add("%", Native.Px(44), HorizontalAlignment.Right);
            sizes.Columns.Add("Files", Native.Px(70), HorizontalAlignment.Right);
            sizes.Visible = false;
            sizes.HandleCreated += delegate { ThemeSizes(); };
            host.Controls.Add(sizes);
            host.Controls.Add(picture);
            host.Controls.Add(message);
            Controls.Add(host);
            Controls.Add(header);
        }

        public void ApplyTheme()
        {
            header.BackColor = Theme.Bar;
            header.ForeColor = Theme.TextDim;
            host.BackColor = picture.BackColor = message.BackColor = sizes.BackColor = Theme.Window;
            message.ForeColor = Theme.TextDim;
            sizes.ForeColor = Theme.Text;
            if (sizes.IsHandleCreated) ThemeSizes();
            string again = current;
            current = "";
            if (Visible) Show(again);
        }

        RECT HostRect()
        {
            RECT r = new RECT();
            r.right = host.ClientSize.Width;
            r.bottom = host.ClientSize.Height;
            return r;
        }

        void ThemeSizes()
        {
            Native.SetWindowTheme(sizes.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
            IntPtr header = Native.SendMessage(sizes.Handle, 0x101F /* LVM_GETHEADER */, IntPtr.Zero, IntPtr.Zero);
            if (header != IntPtr.Zero) Native.SetWindowTheme(header, Theme.Dark ? "DarkMode_ItemsView" : "ItemsView", null);
        }

        void Layout2()
        {
            if (handler == null) return;
            RECT r = HostRect();
            try { handler.SetRect(ref r); } catch { }
        }

        public void Show(string path)
        {
            if (path == null) path = "";
            if (path == current && shownJob == null) return;
            current = path;
            shownJob = null;
            sizes.Visible = false;
            Unload();
            header.Text = path.Length == 0 ? "Preview" : Path.GetFileName(path.TrimEnd('\\'));
            if (path.Length == 0) { ShowMessage("Select a file to preview"); return; }
            if (!Directory.Exists(path) && TryHandler(path)) return;
            StartThumbnail(path);
        }

        int thumbTicket;

        // Thumbnail extraction can be slow (video, large images, cloud files), so it runs off the UI thread;
        // a result that arrives after the selection changed is discarded.
        void StartThumbnail(string path)
        {
            int ticket = ++thumbTicket;
            SIZE s = new SIZE();
            s.cx = Math.Max(64, Math.Min(1024, host.ClientSize.Width));
            s.cy = Math.Max(64, Math.Min(1024, host.ClientSize.Height));
            ShowMessage("Loading preview…");
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                Bitmap bmp = null;
                IShellItem item = null;
                try
                {
                    item = Native.ItemFromPath(path);
                    IShellItemImageFactory fac = item as IShellItemImageFactory;
                    IntPtr hbmp;
                    if (fac != null && fac.GetImage(s, 0, out hbmp) == 0 && hbmp != IntPtr.Zero)
                    {
                        try { bmp = BitmapWithAlpha(hbmp); }
                        finally { Native.DeleteObject(hbmp); }
                    }
                }
                catch { }
                finally { if (item != null) try { Marshal.ReleaseComObject(item); } catch { } }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (ticket != thumbTicket || current != path) { if (bmp != null) bmp.Dispose(); return; }
                        if (bmp == null) { ShowMessage("No preview available"); return; }
                        Image old = picture.Image;
                        picture.Image = bmp;
                        if (old != null) old.Dispose();
                        message.Visible = false;
                        picture.Visible = true;
                    });
                }
                catch { if (bmp != null) bmp.Dispose(); }
            });
            th.SetApartmentState(System.Threading.ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
        }

        void ShowMessage(string text)
        {
            picture.Visible = false;
            message.Text = text;
            message.Visible = true;
        }

        bool TryHandler(string path)
        {
            string clsid = Native.PreviewHandlerFor(Path.GetExtension(path));
            if (clsid == null) return false;
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
                if (ws != null && Native.SHCreateStreamOnFileEx(path, 0x40, 0, false, IntPtr.Zero, out stream) == 0)
                {
                    ok = ws.Initialize(stream, 0) == 0;
                    Marshal.ReleaseComObject(stream); // the handler keeps its own reference if it needs one
                }
                if (!ok)
                {
                    IInitializeWithFile f = o as IInitializeWithFile;
                    if (f != null) ok = f.Initialize(path, 0) == 0;
                }
                if (!ok)
                {
                    IInitializeWithItem wi = o as IInitializeWithItem;
                    IShellItem item = wi != null ? Native.ItemFromPath(path) : null;
                    if (item != null) { ok = wi.Initialize(item, 0) == 0; Marshal.ReleaseComObject(item); }
                }
                if (!ok) { Marshal.ReleaseComObject(o); return false; }
                handler = (IPreviewHandler)o;
                IPreviewHandlerVisuals v = o as IPreviewHandlerVisuals;
                if (v != null)
                {
                    v.SetBackgroundColor(Native.ColorRef(Theme.Window));
                    v.SetTextColor(Native.ColorRef(Theme.Text));
                }
                message.Visible = picture.Visible = false;
                RECT r = HostRect();
                if (handler.SetWindow(host.Handle, ref r) != 0 || handler.DoPreview() != 0) { Unload(); return false; }
                return true;
            }
            catch
            {
                if (handler == null && o != null) try { Marshal.ReleaseComObject(o); } catch { }
                Unload();
                return false;
            }
        }

        // Image.FromHbitmap drops the alpha channel; keep it when the shell returns a transparent image.
        static Bitmap BitmapWithAlpha(IntPtr hbmp)
        {
            Bitmap rgb = Image.FromHbitmap(hbmp);
            if (Image.GetPixelFormatSize(rgb.PixelFormat) != 32) return rgb;
            int w = rgb.Width, h = rgb.Height, row = w * 4;
            Rectangle r = new Rectangle(0, 0, w, h);
            byte[] bytes = new byte[row * h];
            // Copy row by row: the stride can be negative (bottom-up bitmaps), so one big copy could read past the image.
            System.Drawing.Imaging.BitmapData d = rgb.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try { for (int y = 0; y < h; y++) Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), bytes, y * row, row); }
            finally { rgb.UnlockBits(d); }
            bool anyAlpha = false;
            for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) return rgb;
            Bitmap argb = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData a = argb.LockBits(r, System.Drawing.Imaging.ImageLockMode.WriteOnly, argb.PixelFormat);
            try { for (int y = 0; y < h; y++) Marshal.Copy(bytes, y * row, IntPtr.Add(a.Scan0, y * a.Stride), row); }
            finally { argb.UnlockBits(a); }
            rgb.Dispose();
            return argb;
        }
        public void Unload()
        {
            thumbTicket++; // any thumbnail still being made is no longer wanted
            if (handler != null)
            {
                try { handler.Unload(); } catch { }
                try { Marshal.FinalReleaseComObject(handler); } catch { }
                handler = null;
            }
            Image old = picture.Image;
            picture.Image = null;
            if (old != null) old.Dispose();
        }

        public void Clear()
        {
            Show(null);
        }

        // Shows a folder's subfolders with their sizes, largest first; called again as the scan progresses.
        public void ShowSizes(SizeJob job)
        {
            if (shownJob != job)
            {
                Unload();
                shownJob = job;
                current = job.Root;
                picture.Visible = message.Visible = false;
                sizes.Visible = true;
            }
            long total = job.TotalBytes;
            string state = job.Failure != null ? "stopped" : job.Finished ? Util.FormatBytes(total) + " in " + job.TotalFiles.ToString("N0") + " files" : "calculating… " + Util.FormatBytes(total);
            if (job.Errors > 0) state += "  (at least: " + job.Errors + (job.Errors == 1 ? " folder" : " folders") + " couldn't be read)";
            header.Text = Path.GetFileName(job.Root.TrimEnd('\\')) + "  ·  " + state;
            if (header.Text.StartsWith("  ")) header.Text = job.Root + "  ·  " + state;
            sizes.BeginUpdate();
            sizes.Items.Clear();
            foreach (SizeEntry e in job.Snapshot())
            {
                ListViewItem it = new ListViewItem(e.Name);
                it.SubItems.Add(e.Done ? Util.FormatBytes(e.Bytes) : (e.Bytes > 0 ? Util.FormatBytes(e.Bytes) + "…" : "…"));
                it.SubItems.Add(total > 0 ? (100.0 * e.Bytes / total).ToString("0") : "");
                it.SubItems.Add(e.Files.ToString("N0"));
                it.ForeColor = e.Done ? Theme.Text : Theme.TextDim;
                it.ToolTipText = e.Path;
                sizes.Items.Add(it);
            }
            sizes.EndUpdate();
        }
    }
}
