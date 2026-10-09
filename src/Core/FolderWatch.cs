// OrclFX: watches the folders shown in the panes and tells the shell about each change right away, so the file
// lists update at once (as in xplorer2). Without it an embedded Explorer view learns about changes made by other
// programs only through the shell's own notifications, which arrive a second or more later.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OrclFileExplorer
{
    class FolderWatch : IDisposable
    {
        readonly Dictionary<string, Watcher> watchers = new Dictionary<string, Watcher>(StringComparer.OrdinalIgnoreCase);

        // Watches exactly these folders (local disks only: a share may be slow to answer or go away).
        public void WatchOnly(IEnumerable<string> folders)
        {
            HashSet<string> want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in folders)
                if (!string.IsNullOrEmpty(f) && PathParts.Split(f).Count > 0 && !Util.IsNetworkPath(f)) want.Add(f.TrimEnd('\\') + "\\");
            foreach (string f in new List<string>(watchers.Keys))
                if (!want.Contains(f)) { watchers[f].Dispose(); watchers.Remove(f); }
            foreach (string f in want)
                if (!watchers.ContainsKey(f))
                {
                    Watcher w = Watcher.Start(f);
                    if (w != null) watchers[f] = w;
                }
        }

        public void Dispose()
        {
            foreach (Watcher w in watchers.Values) w.Dispose();
            watchers.Clear();
        }

        class Watcher : IDisposable
        {
            FileSystemWatcher fsw;
            readonly string folder;
            readonly object gate = new object();
            readonly List<KeyValuePair<int, string[]>> pending = new List<KeyValuePair<int, string[]>>();
            Timer flush;

            Watcher(string folder) { this.folder = folder; }

            public static Watcher Start(string folder)
            {
                try
                {
                    if (!Directory.Exists(folder)) return null;
                    Watcher w = new Watcher(folder);
                    FileSystemWatcher fsw = new FileSystemWatcher(folder);
                    fsw.IncludeSubdirectories = false;
                    fsw.InternalBufferSize = 64 * 1024;
                    fsw.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
                    fsw.Created += delegate(object s, FileSystemEventArgs e) { w.Add(Directory.Exists(e.FullPath) ? SHCNE_MKDIR : SHCNE_CREATE, e.FullPath, null); };
                    fsw.Deleted += delegate(object s, FileSystemEventArgs e) { w.Add(SHCNE_DELETE | SHCNE_RMDIR, e.FullPath, null); };
                    fsw.Changed += delegate(object s, FileSystemEventArgs e) { w.Add(SHCNE_UPDATEITEM, e.FullPath, null); };
                    fsw.Renamed += delegate(object s, RenamedEventArgs e)
                    {
                        w.Add(Directory.Exists(e.FullPath) ? SHCNE_RENAMEFOLDER : SHCNE_RENAMEITEM, e.OldFullPath, e.FullPath);
                    };
                    fsw.Error += delegate { w.Add(SHCNE_UPDATEDIR, w.folder, null); }; // too many at once: the whole folder
                    fsw.EnableRaisingEvents = true;
                    w.fsw = fsw;
                    w.flush = new Timer(delegate { w.Flush(); }, null, Timeout.Infinite, Timeout.Infinite);
                    return w;
                }
                catch { return null; }
            }

            const int SHCNE_RENAMEITEM = 0x1, SHCNE_CREATE = 0x2, SHCNE_DELETE = 0x4, SHCNE_MKDIR = 0x8, SHCNE_RMDIR = 0x10,
                SHCNE_UPDATEDIR = 0x1000, SHCNE_UPDATEITEM = 0x2000, SHCNE_RENAMEFOLDER = 0x20000;

            // Changes are collected for 50 ms (a copy brings many at once), then passed on together.
            void Add(int ev, string a, string b)
            {
                lock (gate)
                {
                    pending.Add(new KeyValuePair<int, string[]>(ev, new string[] { a, b }));
                    if (pending.Count == 1 && flush != null) flush.Change(50, Timeout.Infinite);
                }
            }

            void Flush()
            {
                List<KeyValuePair<int, string[]>> batch;
                lock (gate) { batch = new List<KeyValuePair<int, string[]>>(pending); pending.Clear(); }
                try
                {
                    if (batch.Count > 40) { Notify(SHCNE_UPDATEDIR, folder, null); return; } // a big copy: read the folder again
                    foreach (KeyValuePair<int, string[]> c in batch)
                    {
                        int ev = c.Key;
                        if (ev == (SHCNE_DELETE | SHCNE_RMDIR)) { Notify(SHCNE_DELETE, c.Value[0], null); Notify(SHCNE_RMDIR, c.Value[0], null); }
                        else Notify(ev, c.Value[0], c.Value[1]);
                    }
                }
                catch { }
            }

            static void Notify(int ev, string a, string b)
            {
                // SHCNF_PATHW | SHCNF_FLUSHNOWAIT: delivered at once, without waiting for the views.
                Native.SHChangeNotify(ev, 0x0005 | 0x3000, a, b);
            }

            public void Dispose()
            {
                try { if (fsw != null) { fsw.EnableRaisingEvents = false; fsw.Dispose(); } } catch { }
                try { if (flush != null) flush.Dispose(); } catch { }
                fsw = null;
                flush = null;
            }
        }
    }
}
