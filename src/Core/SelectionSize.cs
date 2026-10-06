// Orcl File Explorer: background total of the selected files and folders, for the status bar. No UI; covered by
// tests\.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace OrclFileExplorer
{
    class SelectionSize
    {
        public const long MaxEntries = 2000000;
        public const int MaxSeconds = 60;

        long bytes, files;
        public long Bytes { get { return Interlocked.Read(ref bytes); } }
        public long Files { get { return Interlocked.Read(ref files); } }
        public volatile bool Cancel, Finished;
        // Some of it couldn't be counted (unreadable folders, folders on a network share, drives, the limits):
        // the total is then a lower bound.
        public volatile bool Partial;
        readonly List<string> paths;
        readonly Action<SelectionSize> updated;
        readonly long entryLimit;
        readonly int secondsLimit;
        long scanned;
        System.Diagnostics.Stopwatch clock;
        int lastNotify = Environment.TickCount - 1000;

        SelectionSize(List<string> paths, Action<SelectionSize> updated, long entryLimit, int secondsLimit)
        {
            this.paths = paths;
            this.updated = updated;
            this.entryLimit = entryLimit;
            this.secondsLimit = secondsLimit;
        }

        public static SelectionSize Start(List<string> paths, Action<SelectionSize> updated)
        {
            return Start(paths, updated, MaxEntries, MaxSeconds);
        }

        // Lower limits are for tests.
        internal static SelectionSize Start(List<string> paths, Action<SelectionSize> updated, long entryLimit, int secondsLimit)
        {
            SelectionSize s = new SelectionSize(paths, updated, entryLimit, secondsLimit);
            Thread th = new Thread(s.Run);
            th.IsBackground = true;
            th.Priority = ThreadPriority.BelowNormal;
            th.Start();
            return s;
        }

        void Run()
        {
            Native.SetThreadPriority(Native.GetCurrentThread(), 0x00010000 /* THREAD_MODE_BACKGROUND_BEGIN */);
            clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                foreach (string p in paths)
                {
                    if (Cancel || Over()) break;
                    if (p == null) continue;
                    FileAttributes a;
                    try { a = File.GetAttributes(p); }
                    catch { Partial = true; continue; }
                    if ((a & FileAttributes.Directory) == 0)
                    {
                        try { Interlocked.Add(ref bytes, new FileInfo(p).Length); Interlocked.Increment(ref files); }
                        catch { Partial = true; }
                    }
                    // A whole drive, or a folder that is really on a network share (also through a link), isn't scanned.
                    else if (Path.GetPathRoot(p).TrimEnd('\\').Length == p.TrimEnd('\\').Length || SizeJob.IsOnNetwork(p)) Partial = true;
                    // Junctions and symbolic links to folders: the folder lives elsewhere, as in Explorer.
                    else if ((a & FileAttributes.ReparsePoint) != 0 && IsLink(p)) { }
                    else ScanTree(p);
                    Notify(false);
                }
            }
            catch { Partial = true; }
            Finished = true;
            Notify(true);
        }

        static bool IsLink(string dir)
        {
            Native.WIN32_FIND_DATA d;
            IntPtr h = Native.FindFirstFileEx((dir.StartsWith(@"\\") ? dir : @"\\?\" + dir).TrimEnd('\\'), 1, out d, 0, IntPtr.Zero, 0);
            if (h == (IntPtr)(-1)) return false;
            Native.FindClose(h);
            return d.dwReserved0 == 0xA0000003 || d.dwReserved0 == 0xA000000C;
        }

        bool Over()
        {
            if (scanned > entryLimit || clock.Elapsed.TotalSeconds > secondsLimit) { Partial = true; return true; }
            return false;
        }

        void ScanTree(string root)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0 && !Cancel)
            {
                string dir = stack.Pop();
                string pattern = (dir.StartsWith(@"\\") ? dir : @"\\?\" + dir).TrimEnd('\\') + @"\*";
                Native.WIN32_FIND_DATA d;
                IntPtr h = Native.FindFirstFileEx(pattern, 1, out d, 0, IntPtr.Zero, 2); // never downloads cloud files
                if (h == (IntPtr)(-1))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 2 && err != 18) Partial = true;
                    continue;
                }
                try
                {
                    do
                    {
                        if (Cancel) return;
                        if (d.cFileName == "." || d.cFileName == "..") continue;
                        if ((++scanned & 255) == 0 && Over()) return;
                        bool isDir = (d.dwFileAttributes & 0x10) != 0;
                        if (isDir)
                        {
                            if ((d.dwFileAttributes & 0x400) != 0 && (d.dwReserved0 == 0xA0000003 || d.dwReserved0 == 0xA000000C)) continue;
                            stack.Push(Path.Combine(dir, d.cFileName));
                        }
                        else
                        {
                            Interlocked.Add(ref bytes, ((long)d.nFileSizeHigh << 32) | d.nFileSizeLow);
                            Interlocked.Increment(ref files);
                        }
                    }
                    while (Native.FindNextFile(h, out d));
                }
                finally { Native.FindClose(h); }
                Notify(false);
            }
        }

        // At most four updates a second, plus the last one.
        void Notify(bool force)
        {
            if (Cancel) return;
            if (!force && unchecked(Environment.TickCount - lastNotify) < 250) return;
            lastNotify = Environment.TickCount;
            try { if (updated != null) updated(this); } catch { }
        }

        // "1.2 GB", "at least 1.2 GB", "1.2 GB…" while counting.
        public string Text
        {
            get { return (Partial ? "at least " : "") + Util.FormatBytes(Bytes) + (Finished ? "" : "…"); }
        }
    }
}
