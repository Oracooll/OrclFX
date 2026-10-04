// Orcl File Explorer: finding files and folders by name in a folder and all its subfolders. No UI; covered by tests\.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace OrclFileExplorer
{
    // What the user typed in Find, as a test on names. Several patterns can be separated with ";".
    // A pattern with * or ? is a wildcard match on the whole name (*.pdf, report??.docx); anything else
    // matches when the name contains it (report finds "Q3 Report.xlsx"). Case never matters.
    class FindPattern
    {
        readonly List<Regex> wildcards = new List<Regex>();
        readonly List<string> parts = new List<string>();

        public FindPattern(string text)
        {
            foreach (string raw in (text ?? "").Split(';'))
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                if (p.IndexOfAny(new char[] { '*', '?' }) >= 0)
                    wildcards.Add(new Regex("^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                else parts.Add(p);
            }
        }

        public bool IsEmpty { get { return wildcards.Count == 0 && parts.Count == 0; } }

        public bool Matches(string name)
        {
            foreach (string p in parts) if (name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (Regex r in wildcards) if (r.IsMatch(name)) return true;
            return false;
        }
    }

    // Searches a folder tree on a background thread and reports what it finds in batches, so the results can
    // appear while it runs. Junctions and symbolic links are not followed (no loops, nothing found twice).
    class FileSearch
    {
        public const int MaxResults = 20000;

        public readonly string Root;
        public readonly FindPattern Pattern;
        public volatile bool Cancel;
        public bool Finished;
        public bool Truncated;      // stopped at MaxResults
        public int Found, Folders, Errors;
        readonly Action<FileSearch, List<string>> found;   // called from the search thread with each batch
        readonly int maxResults;

        FileSearch(string root, FindPattern pattern, Action<FileSearch, List<string>> found, int maxResults)
        {
            Root = root.TrimEnd('\\') + (root.EndsWith(":") || root.EndsWith(":\\") ? "\\" : "");
            Pattern = pattern;
            this.found = found;
            this.maxResults = maxResults;
        }

        public static FileSearch Start(string root, string text, Action<FileSearch, List<string>> found)
        {
            return Start(root, text, found, MaxResults);
        }

        internal static FileSearch Start(string root, string text, Action<FileSearch, List<string>> found, int maxResults)
        {
            FileSearch s = new FileSearch(root, new FindPattern(text), found, maxResults);
            Thread th = new Thread(s.Run);
            th.IsBackground = true;
            th.Priority = ThreadPriority.BelowNormal;
            th.Start();
            return s;
        }

        List<string> batch = new List<string>();
        int lastReport = Environment.TickCount;

        void Run()
        {
            try
            {
                Stack<string> stack = new Stack<string>();
                stack.Push(Root);
                while (stack.Count > 0 && !Cancel && !Truncated)
                {
                    string dir = stack.Pop();
                    Folders++;
                    List<string> subs = new List<string>();
                    Enumerate(dir, delegate(string name, bool isDir)
                    {
                        string full = Path.Combine(dir, name);
                        if (isDir) subs.Add(full);
                        if (Pattern.Matches(name)) Add(full);
                    });
                    // Visit subfolders in name order (the stack reverses them).
                    for (int i = subs.Count - 1; i >= 0; i--) stack.Push(subs[i]);
                    if (unchecked(Environment.TickCount - lastReport) >= 150) Report();
                }
            }
            catch { Errors++; }
            Finished = true;
            Report();
        }

        void Add(string path)
        {
            if (Found >= maxResults) { Truncated = true; return; }
            Found++;
            batch.Add(path);
        }

        void Report()
        {
            lastReport = Environment.TickCount;
            if (Cancel) return;
            List<string> b = batch;
            batch = new List<string>();
            try { if (found != null) found(this, b); } catch { }
        }

        delegate void Visit(string name, bool isDir);

        void Enumerate(string dir, Visit visit)
        {
            string pattern = (dir.StartsWith(@"\\") ? dir : @"\\?\" + dir).TrimEnd('\\') + @"\*";
            Native.WIN32_FIND_DATA d;
            IntPtr h = Native.FindFirstFileEx(pattern, 1, out d, 0, IntPtr.Zero, 2); // basic info, large fetch
            if (h == (IntPtr)(-1))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 2 && err != 18) Errors++;
                return;
            }
            try
            {
                do
                {
                    if (Cancel || Truncated) return;
                    if (d.cFileName == "." || d.cFileName == "..") continue;
                    bool isDir = (d.dwFileAttributes & 0x10) != 0;
                    const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003, IO_REPARSE_TAG_SYMLINK = 0xA000000C;
                    bool link = (d.dwFileAttributes & 0x400) != 0 &&
                        (d.dwReserved0 == IO_REPARSE_TAG_MOUNT_POINT || d.dwReserved0 == IO_REPARSE_TAG_SYMLINK);
                    if (isDir && link)
                    {
                        // The link itself can match; what's behind it isn't searched.
                        if (Pattern.Matches(d.cFileName)) Add(Path.Combine(dir, d.cFileName));
                        continue;
                    }
                    visit(d.cFileName, isDir);
                }
                while (Native.FindNextFile(h, out d));
            }
            finally { Native.FindClose(h); }
        }
    }
}
