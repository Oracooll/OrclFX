// Orcl File Explorer: Background scan that totals the size of a folder's subfolders.
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
    class SizeEntry
    {
        public string Name, Path;
        public long Bytes, Files;
        public bool Done;
    }

    class SizeJob
    {
        public const long MaxEntries = 2000000;
        public const int MaxSeconds = 90;

        public readonly string Root;
        public readonly List<SizeEntry> Entries = new List<SizeEntry>();
        public volatile bool Cancel;
        public bool Finished;
        public string Failure;   // set when the scan stopped for a "critical" reason
        public int Errors;       // folders that couldn't be read: the totals are then a lower bound
        public DateTime FinishedAt;
        public string Skipped;   // set instead of scanning, e.g. when the folder really lives on a network share
        // Called from the scan thread as the totals change (at most four times a second, plus at the start
        // and the end); the receiver moves to its own thread if it needs to.
        readonly Action<SizeJob> updated;
        readonly long entryLimit;
        readonly int secondsLimit;
        long scanned;
        Stopwatch clock;

        SizeJob(string root, Action<SizeJob> updated, long entryLimit, int secondsLimit)
        {
            Root = root;
            this.updated = updated;
            this.entryLimit = entryLimit;
            this.secondsLimit = secondsLimit;
        }

        public long TotalBytes { get { long t = 0; lock (Entries) foreach (SizeEntry e in Entries) t += e.Bytes; return t; } }
        public long TotalFiles { get { long t = 0; lock (Entries) foreach (SizeEntry e in Entries) t += e.Files; return t; } }

        public static SizeJob Start(string root, Action<SizeJob> updated)
        {
            return Start(root, updated, MaxEntries, MaxSeconds);
        }

        // Lower limits are for tests.
        internal static SizeJob Start(string root, Action<SizeJob> updated, long entryLimit, int secondsLimit)
        {
            SizeJob j = new SizeJob(root, updated, entryLimit, secondsLimit);
            System.Threading.Thread th = new System.Threading.Thread(j.Run);
            th.IsBackground = true;
            th.Priority = System.Threading.ThreadPriority.BelowNormal;
            th.Start();
            return j;
        }

        void Run()
        {
            // Background mode lowers this thread's disk and CPU priority so browsing stays responsive.
            Native.SetThreadPriority(Native.GetCurrentThread(), 0x00010000 /* THREAD_MODE_BACKGROUND_BEGIN */);
            clock = Stopwatch.StartNew();
            try
            {
                // A local link (symbolic link, junction, or a folder inside one) can lead to a network share.
                // The check runs here, off the UI thread, because resolving it can wait on the network.
                if (IsOnNetwork(Root))
                {
                    Skipped = "not calculated on network locations";
                    Finished = true;
                    FinishedAt = DateTime.Now;
                    Notify(true);
                    return;
                }
                SizeEntry here = new SizeEntry();
                here.Name = "(files in this folder)";
                here.Path = Root;
                List<SizeEntry> subs = new List<SizeEntry>();
                Enumerate(Root, delegate(Native.WIN32_FIND_DATA d, bool dir)
                {
                    if (dir)
                    {
                        SizeEntry e = new SizeEntry();
                        e.Name = d.cFileName;
                        e.Path = System.IO.Path.Combine(Root, d.cFileName);
                        subs.Add(e);
                    }
                    else { here.Bytes += Size(d); here.Files++; }
                });
                here.Done = true;
                lock (Entries) { Entries.AddRange(subs); if (here.Files > 0) Entries.Add(here); }
                Notify(true);
                foreach (SizeEntry e in subs)
                {
                    if (Cancel || Failure != null) break;
                    ScanTree(e);
                    lock (Entries) e.Done = true;
                    Notify(false);
                }
            }
            catch (Exception ex) { if (Failure == null && !Cancel) Failure = ex.Message; }
            Finished = true;
            FinishedAt = DateTime.Now;
            Notify(true);
        }

        // Whether the folder is on a network share once every link on the way is followed.
        internal static bool IsOnNetwork(string path)
        {
            if (path.StartsWith(@"\\")) return true;
            // FILE_FLAG_BACKUP_SEMANTICS opens a folder; no access rights are needed to read its final path.
            IntPtr h = Native.CreateFile(@"\\?\" + path.TrimEnd('\\') + @"\", 0, 7 /* share all */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0x02000000, IntPtr.Zero);
            if (h == (IntPtr)(-1)) return false; // can't be opened: the scan reports it as unreadable
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                uint n = Native.GetFinalPathNameByHandle(h, sb, (uint)sb.Capacity, 0);
                if (n == 0 || n >= sb.Capacity) return false;
                string final = sb.ToString();
                if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return true;
                if (final.StartsWith(@"\\?\")) final = final.Substring(4);
                return final.Length >= 2 && final[1] == ':' && Native.GetDriveType(final.Substring(0, 2) + @"\") == 4 /* DRIVE_REMOTE */;
            }
            finally { Native.CloseHandle(h); }
        }

        static long Size(Native.WIN32_FIND_DATA d) { return ((long)d.nFileSizeHigh << 32) | d.nFileSizeLow; }

        void ScanTree(SizeEntry e)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(e.Path);
            while (stack.Count > 0 && !Cancel && Failure == null)
            {
                string dir = stack.Pop();
                long bytes = 0, files = 0;
                Enumerate(dir, delegate(Native.WIN32_FIND_DATA d, bool isDir)
                {
                    if (isDir) stack.Push(System.IO.Path.Combine(dir, d.cFileName));
                    else { bytes += Size(d); files++; }
                });
                // Readers lock Entries; update the live totals under the same lock.
                lock (Entries) { e.Bytes += bytes; e.Files += files; }
                Notify(false);
            }
        }

        delegate void Visit(Native.WIN32_FIND_DATA d, bool isDir);

        void Enumerate(string dir, Visit visit)
        {
            if (OverBudget()) return;
            string pattern = (dir.StartsWith(@"\\") ? dir : @"\\?\" + dir).TrimEnd('\\') + @"\*";
            Native.WIN32_FIND_DATA d;
            // FindExInfoBasic, FIND_FIRST_EX_LARGE_FETCH. Enumerating never downloads OneDrive files.
            IntPtr h = Native.FindFirstFileEx(pattern, 1, out d, 0, IntPtr.Zero, 2);
            if (h == (IntPtr)(-1))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 2 && err != 18) System.Threading.Interlocked.Increment(ref Errors); // not "no files"
                return;
            }
            try
            {
                do
                {
                    if (Cancel) return;
                    if (d.cFileName == "." || d.cFileName == "..") continue;
                    bool isDir = (d.dwFileAttributes & 0x10) != 0;
                    // Skip junctions and symbolic links so nothing is counted twice or loops. Other reparse
                    // points (OneDrive and other cloud folders) are ordinary folders and are scanned.
                    const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003, IO_REPARSE_TAG_SYMLINK = 0xA000000C;
                    if (isDir && (d.dwFileAttributes & 0x400) != 0 &&
                        (d.dwReserved0 == IO_REPARSE_TAG_MOUNT_POINT || d.dwReserved0 == IO_REPARSE_TAG_SYMLINK)) continue;
                    // The limits are checked for every item, so one huge folder can't run past them.
                    if (OverBudget()) return;
                    visit(d, isDir);
                }
                while (Native.FindNextFile(h, out d));
                if (Marshal.GetLastWin32Error() != 18 /* ERROR_NO_MORE_FILES */) System.Threading.Interlocked.Increment(ref Errors);
            }
            finally { Native.FindClose(h); }
        }

        bool OverBudget()
        {
            if (Failure != null) return true;
            if (++scanned > entryLimit)
            {
                Failure = "more than " + (entryLimit >= 1000000 ? (entryLimit / 1000000) + " million" : entryLimit.ToString("N0")) + " items to scan";
                return true;
            }
            if ((scanned & 255) == 0 && clock.Elapsed.TotalSeconds > secondsLimit) { Failure = "the scan took longer than " + secondsLimit + " seconds"; return true; }
            return false;
        }

        int lastNotifyTick = Environment.TickCount - 1000;

        // At most four UI updates a second, plus the forced ones at the start and the end.
        void Notify(bool force)
        {
            if (Cancel) return;
            if (!force && unchecked(Environment.TickCount - lastNotifyTick) < 250) return;
            lastNotifyTick = Environment.TickCount;
            try { if (updated != null) updated(this); } catch { }
        }

        // Copies the values first: the scan keeps updating the live entries while we sort.
        public List<SizeEntry> Snapshot()
        {
            List<SizeEntry> copy = new List<SizeEntry>();
            lock (Entries)
                foreach (SizeEntry e in Entries)
                {
                    SizeEntry s = new SizeEntry();
                    s.Name = e.Name; s.Path = e.Path; s.Bytes = e.Bytes; s.Files = e.Files; s.Done = e.Done;
                    copy.Add(s);
                }
            copy.Sort(delegate(SizeEntry a, SizeEntry b) { return b.Bytes.CompareTo(a.Bytes); });
            return copy;
        }
    }
}
