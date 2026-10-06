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
        // Text and code: the file as text, with a search box above it.
        readonly Panel textPanel = new Panel(), findRow = new Panel();
        readonly TextBox textBox = new TextBox(), findText = new TextBox();
        readonly Label findLabel = new Label(), findCount = new Label();
        List<int> hits = new List<int>();
        int hit = -1;
        string current = "";
        // The preview work runs on a PreviewWorker thread. A worker stuck on one file is abandoned (it exits
        // once its call returns) and a fresh one takes over; abandoned ones still running are counted.
        PreviewWorker worker;
        readonly System.Collections.Generic.List<PreviewWorker> abandoned = new System.Collections.Generic.List<PreviewWorker>();
        readonly Timer watchdog = new Timer();
        bool showingHandler;
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
            BuildTextView();
            host.Controls.Add(textPanel);
            host.Controls.Add(sizes);
            host.Controls.Add(picture);
            host.Controls.Add(message);
            Controls.Add(host);
            Controls.Add(header);
            watchdog.Interval = 1000;
            watchdog.Tick += delegate { CheckWorker(); };
        }

        void BuildTextView()
        {
            textPanel.Dock = DockStyle.Fill;
            textPanel.Visible = false;
            textBox.Dock = DockStyle.Fill;
            textBox.Multiline = true;
            textBox.ReadOnly = true;
            textBox.WordWrap = false;
            textBox.ScrollBars = ScrollBars.Both;
            textBox.BorderStyle = BorderStyle.None;
            textBox.HideSelection = false;
            textBox.MaxLength = 0;
            textBox.Font = new Font("Consolas", 9.75f);
            textBox.HandleCreated += delegate { ThemeText(); };
            textBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.F) { e.SuppressKeyPress = true; findText.Focus(); findText.SelectAll(); }
                else if (e.KeyCode == Keys.F3) { e.SuppressKeyPress = true; Step(e.Shift ? -1 : 1); }
            };
            findRow.Dock = DockStyle.Top;
            findRow.Height = Native.Px(32);
            findRow.Padding = new Padding(Native.Px(8), Native.Px(5), Native.Px(8), Native.Px(5));
            findLabel.Text = "Search";
            findLabel.Dock = DockStyle.Left;
            findLabel.Width = Native.Px(52);
            findLabel.TextAlign = ContentAlignment.MiddleLeft;
            findCount.Dock = DockStyle.Right;
            findCount.Width = Native.Px(84);
            findCount.TextAlign = ContentAlignment.MiddleRight;
            findText.Dock = DockStyle.Fill;
            findText.BorderStyle = BorderStyle.FixedSingle;
            findText.TextChanged += delegate { Search(); };
            findText.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F3) { e.SuppressKeyPress = true; Step(e.Shift ? -1 : 1); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; findText.Text = ""; textBox.Focus(); }
            };
            findRow.Controls.Add(findText);
            findRow.Controls.Add(findCount);
            findRow.Controls.Add(findLabel);
            textPanel.Controls.Add(textBox);
            textPanel.Controls.Add(findRow);
        }

        void ThemeText()
        {
            Native.SetWindowTheme(textBox.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
        }

        void ShowText(string text, string note)
        {
            textBox.Text = text;
            textBox.Select(0, 0);
            header.Text = Path.GetFileName(current) + "  ·  " + note;
            message.Visible = picture.Visible = false;
            textPanel.Visible = true;
            textPanel.BringToFront();
            Search(); // the search term stays from file to file
        }

        // Finds every match of the search box's text; the first one at or after the caret is selected.
        void Search()
        {
            hits = TextPreview.FindAll(textBox.Text, findText.Text);
            hit = -1;
            if (hits.Count > 0)
            {
                hit = 0;
                int caret = textBox.SelectionStart;
                for (int i = 0; i < hits.Count; i++) if (hits[i] >= caret) { hit = i; break; }
                ShowHit();
            }
            UpdateCount();
        }

        void Step(int d)
        {
            if (hits.Count == 0) { if (findText.Text.Length > 0) SystemSounds.Beep.Play(); return; }
            hit = (hit + d + hits.Count) % hits.Count;
            ShowHit();
            UpdateCount();
        }

        void ShowHit()
        {
            // Scroll to the match's line from its start (so lines stay readable from the left edge), then sideways
            // only if the match itself is out of sight.
            int at = hits[hit], len = findText.Text.Length;
            int lineStart = textBox.GetFirstCharIndexFromLine(textBox.GetLineFromCharIndex(at));
            textBox.Select(Math.Max(0, lineStart), 0);
            textBox.ScrollToCaret();
            textBox.Select(at, len);
            int end = Math.Min(textBox.TextLength - 1, at + len);
            if (end >= 0 && textBox.GetPositionFromCharIndex(end).X > textBox.ClientSize.Width - Native.Px(20)) textBox.ScrollToCaret();
        }

        // Test hook: searches once the preview has loaded, and traces what is shown.
        public void TestSearch(string what)
        {
            Timer t = new Timer();
            t.Interval = 2500;
            t.Tick += delegate
            {
                t.Stop();
                findText.Text = what;
                Program.Trace("preview: header “" + header.Text + "”, text shown " + textPanel.Visible + ", " + textBox.TextLength + " chars, search: " + findCount.Text +
                    ", selected “" + textBox.SelectedText + "” at " + textBox.SelectionStart);
            };
            t.Start();
        }

        void UpdateCount()
        {
            findCount.Text = findText.Text.Length == 0 ? "" : hits.Count == 0 ? "no match" : (hit + 1).ToString("N0") + " of " + hits.Count.ToString("N0");
        }

        public void ApplyTheme()
        {
            header.BackColor = Theme.Bar;
            header.ForeColor = Theme.TextDim;
            host.BackColor = picture.BackColor = message.BackColor = sizes.BackColor = Theme.Window;
            message.ForeColor = Theme.TextDim;
            sizes.ForeColor = Theme.Text;
            if (sizes.IsHandleCreated) ThemeSizes();
            textPanel.BackColor = findRow.BackColor = findLabel.BackColor = findCount.BackColor = Theme.Bar;
            findLabel.ForeColor = findCount.ForeColor = Theme.TextDim;
            textBox.BackColor = Theme.Window;
            textBox.ForeColor = Theme.Text;
            findText.BackColor = Theme.Input;
            findText.ForeColor = Theme.Text;
            if (textBox.IsHandleCreated) ThemeText();
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
            if (!showingHandler || worker == null) return;
            worker.SetRect(HostRect());
        }

        public void Show(string path)
        {
            if (path == null) path = "";
            if (path == current && shownJob == null) return;
            current = path;
            shownJob = null;
            sizes.Visible = false;
            header.Text = path.Length == 0 ? "Preview" : Path.GetFileName(path.TrimEnd('\\'));
            if (path.Length == 0) { Unload(); ShowMessage("Select a file to preview"); return; }
            Submit(path);
            ShowMessage("Loading preview…");
        }

        int thumbTicket;
        string stuckFor;

        // Hands the newest selection to the worker; anything still running for an older one is no longer wanted.
        void Submit(string path)
        {
            ClearShown();
            PreviewWorker.Request r = new PreviewWorker.Request();
            r.Ticket = ++thumbTicket;
            r.Path = path;
            if (path.Length > 0)
            {
                r.Host = host.Handle;
                r.Rect = HostRect();
                r.ThumbSize.cx = Math.Max(64, Math.Min(1024, host.ClientSize.Width));
                r.ThumbSize.cy = Math.Max(64, Math.Min(1024, host.ClientSize.Height));
                r.Back = Native.ColorRef(Theme.Window);
                r.Text = Native.ColorRef(Theme.Text);
            }
            if (worker == null)
            {
                if (path.Length == 0) return;
                abandoned.RemoveAll(delegate(PreviewWorker w) { return !w.IsAlive; });
                // Several stuck previews at once: stop starting new ones until one of them recovers.
                if (abandoned.Count >= 3) { ShowMessage("Previews have stopped responding"); return; }
                worker = new PreviewWorker(delegate(PreviewWorker.Result res)
                {
                    try { BeginInvoke((MethodInvoker)delegate { Delivered(res); }); }
                    catch { if (res.Thumbnail != null) res.Thumbnail.Dispose(); }
                });
            }
            worker.Submit(r);
            stuckFor = null;
            watchdog.Start();
        }

        void Delivered(PreviewWorker.Result res)
        {
            if (res.From != worker || res.Ticket != thumbTicket) { if (res.Thumbnail != null) res.Thumbnail.Dispose(); return; }
            if (res.Handler)
            {
                showingHandler = true;
                message.Visible = picture.Visible = false;
                Layout2();
                return;
            }
            if (res.Text != null) { ShowText(res.Text, res.TextNote); return; }
            if (res.Thumbnail == null) { ShowMessage("No preview available"); return; }
            Image old = picture.Image;
            picture.Image = res.Thumbnail;
            if (old != null) old.Dispose();
            message.Visible = false;
            picture.Visible = true;
        }

        // Once a second while previews are in use: replace a worker that's stuck.
        void CheckWorker()
        {
            if (worker == null) { watchdog.Stop(); return; }
            TimeSpan busy = worker.Busy;
            // The user has moved on but the worker is still busy with an older file: let a fresh one take over.
            if (busy.TotalSeconds > 8 && worker.HasWaitingRequest)
            {
                PreviewWorker stuck = worker;
                stuck.Abandon();
                abandoned.Add(stuck);
                worker = null;
                string again = current;
                current = "";
                Show(again);
                return;
            }
            if (busy.TotalSeconds > 30 && stuckFor != current && message.Visible)
            {
                stuckFor = current;
                ShowMessage("The preview isn't responding");
            }
        }

        void ShowMessage(string text)
        {
            picture.Visible = false;
            HideText();
            message.Text = text;
            message.Visible = true;
        }

        // Hides what's shown for the previous selection: the picture, and any preview handler window
        // (the handler itself is unloaded by the worker, which may take a moment).
        void HideText()
        {
            if (!textPanel.Visible) return;
            textPanel.Visible = false;
            textBox.Text = ""; // a big file's text isn't kept around
        }

        void ClearShown()
        {
            showingHandler = false;
            HideText();
            Image old = picture.Image;
            picture.Image = null;
            if (old != null) old.Dispose();
            if (!host.IsHandleCreated) return;
            for (IntPtr h = Native.GetWindow(host.Handle, 5 /* GW_CHILD */); h != IntPtr.Zero; h = Native.GetWindow(h, 2 /* GW_HWNDNEXT */))
                if (h != picture.Handle && h != message.Handle && h != sizes.Handle && h != textPanel.Handle) Native.ShowWindowAsync(h, 0); // a hung handler can't block
        }

        // Image.FromHbitmap drops the alpha channel; keep it when the shell returns a transparent image.
        internal static Bitmap BitmapWithAlpha(IntPtr hbmp)
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
        // Stops the current preview (handler or thumbnail).
        public void Unload()
        {
            if (worker != null) Submit("");
            else { ++thumbTicket; ClearShown(); }
        }

        // For closing: unload the handler, waiting at most two seconds for a stuck one.
        public void Shutdown()
        {
            watchdog.Stop();
            ++thumbTicket;
            if (worker == null) return;
            worker.Quit();
            if (!worker.WaitForExit(2000) && host.IsHandleCreated)
            {
                // The handler is stuck: ask its window to close without waiting for it, so closing can't hang on it.
                for (IntPtr h = Native.GetWindow(host.Handle, 5 /* GW_CHILD */); h != IntPtr.Zero; h = Native.GetWindow(h, 2 /* GW_HWNDNEXT */))
                    if (h != picture.Handle && h != message.Handle && h != sizes.Handle && h != textPanel.Handle) { Native.ShowWindowAsync(h, 0); Native.PostMessage(h, 0x10 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero); }
            }
            worker = null;
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
                HideText();
                sizes.Visible = true;
            }
            long total = job.TotalBytes;
            string state = job.Failure != null ? "stopped" : job.Finished ? Util.FormatBytes(total) + " in " + job.TotalFiles.ToString("N0") + " files" : "calculating… " + Util.FormatBytes(total);
            if (job.Skipped != null) state = job.Skipped;
            else if (job.Errors > 0) state += "  (at least: " + job.Errors + (job.Errors == 1 ? " folder" : " folders") + " couldn't be read)";
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
