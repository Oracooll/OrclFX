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

        // Drives being ejected (a watched folder keeps the drive in use): not watched again for a while.
        readonly Dictionary<string, int> ejecting = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        IntPtr owner;

        // The window that hears about drives being removed (Windows asks it to let go of their folders).
        public void SetOwner(IntPtr hwnd) { owner = hwnd; }

        // Watches exactly these folders: on fixed local disks only (a share may be slow to answer or go away; a USB
        // stick, card or disc couldn't be ejected while it's watched).
        public void WatchOnly(IEnumerable<string> folders)
        {
            HashSet<string> want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in folders)
                if (!string.IsNullOrEmpty(f) && PathParts.Split(f).Count > 0 && Watchable(f)) want.Add(f.TrimEnd('\\') + "\\");
            foreach (string f in new List<string>(watchers.Keys))
                if (!want.Contains(f)) { watchers[f].Dispose(); watchers.Remove(f); }
            foreach (string f in want)
                if (!watchers.ContainsKey(f))
                {
                    Watcher w = Watcher.Start(f, owner);
                    if (w != null) watchers[f] = w;
                }
        }

        bool Watchable(string f)
        {
            if (Util.IsNetworkPath(f) || f.Length < 3 || f[1] != ':') return false;
            string root = f.Substring(0, 3);
            int t;
            if (ejecting.TryGetValue(root, out t))
            {
                if (unchecked(Environment.TickCount - t) < 60000) return false;
                ejecting.Remove(root);
            }
            uint type = Native.GetDriveType(root);
            return type == 3; // DRIVE_FIXED (not DRIVE_REMOVABLE, DRIVE_CDROM, a RAM disk …)
        }

        // Windows asks to remove the device under this handle (a USB disk being ejected): its folders let go.
        // True when the handle was one of ours.
        public bool DeviceQueryRemove(IntPtr handle)
        {
            foreach (KeyValuePair<string, Watcher> kv in new List<KeyValuePair<string, Watcher>>(watchers))
                if (kv.Value.Handle == handle)
                {
                    ejecting[kv.Key.Substring(0, 3)] = Environment.TickCount;
                    foreach (KeyValuePair<string, Watcher> other in new List<KeyValuePair<string, Watcher>>(watchers))
                        if (string.Equals(other.Key.Substring(0, 3), kv.Key.Substring(0, 3), StringComparison.OrdinalIgnoreCase)) { other.Value.Dispose(); watchers.Remove(other.Key); }
                    Program.Trace("folder watch: let go of " + kv.Key.Substring(0, 3) + " (being removed)");
                    return true;
                }
            return false;
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
            bool disposed;
            bool dirDue;            // a whole-folder re-read is waiting (they're at most one a second)
            int lastDir = Environment.TickCount - 100000;
            // A handle on the folder registered with Windows, so the owner window hears when its drive is removed.
            IntPtr dirHandle = new IntPtr(-1), notify;
            public IntPtr Handle { get { return dirHandle; } }

            Watcher(string folder) { this.folder = folder; }

            public static Watcher Start(string folder, IntPtr owner)
            {
                FileSystemWatcher fsw = null;
                Watcher w = null;
                try
                {
                    if (!Directory.Exists(folder)) return null;
                    w = new Watcher(folder);
                    fsw = new FileSystemWatcher(folder);
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
                    // (The timer first: a change may come the moment watching starts.)
                    w.flush = new Timer(delegate { w.Flush(); }, null, Timeout.Infinite, Timeout.Infinite);
                    fsw.EnableRaisingEvents = true;
                    w.fsw = fsw;
                    if (owner != IntPtr.Zero) w.Register(owner);
                    return w;
                }
                catch
                {
                    if (fsw != null) try { fsw.Dispose(); } catch { }
                    if (w != null && w.flush != null) try { w.flush.Dispose(); } catch { }
                    return null;
                }
            }

            void Register(IntPtr owner)
            {
                // FILE_LIST_DIRECTORY, all sharing, FILE_FLAG_BACKUP_SEMANTICS (a folder)
                dirHandle = Native.CreateFile(folder, 0x1, 0x7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
                if (dirHandle == new IntPtr(-1)) return;
                notify = Native.RegisterDeviceHandleNotification(owner, dirHandle);
            }

            const int SHCNE_RENAMEITEM = 0x1, SHCNE_CREATE = 0x2, SHCNE_DELETE = 0x4, SHCNE_MKDIR = 0x8, SHCNE_RMDIR = 0x10,
                SHCNE_UPDATEDIR = 0x1000, SHCNE_UPDATEITEM = 0x2000, SHCNE_RENAMEFOLDER = 0x20000;

            // Changes are collected for 50 ms (a copy brings many at once), then passed on together.
            void Add(int ev, string a, string b)
            {
                try
                {
                    lock (gate)
                    {
                        if (disposed) return;
                        pending.Add(new KeyValuePair<int, string[]>(ev, new string[] { a, b }));
                        if (pending.Count == 1) flush.Change(50, Timeout.Infinite);
                    }
                }
                catch { } // (a watcher thread: nothing may escape)
            }

            void Flush()
            {
                List<KeyValuePair<int, string[]>> batch;
                lock (gate) { if (disposed) return; batch = new List<KeyValuePair<int, string[]>>(pending); pending.Clear(); }
                try
                {
                    // Copies and moves made by this app: the shell reports their changes itself; the folder is read
                    // again a second after they end (other programs may have changed it meanwhile).
                    if (ProcessReference.Busy > 0)
                    {
                        lock (gate) { dirDue = true; if (!disposed) flush.Change(1000, Timeout.Infinite); }
                        return;
                    }
                    foreach (KeyValuePair<int, string[]> c in batch) if (c.Key == SHCNE_UPDATEDIR) dirDue = true; // (a watcher overflow)
                    if (batch.Count > 40 || dirDue)
                    {
                        // A big copy: the folder is read again, but at most once a second (each re-read is a full one).
                        int since = unchecked(Environment.TickCount - lastDir);
                        if (since < 1000)
                        {
                            lock (gate) { dirDue = true; if (!disposed) flush.Change(1000 - since, Timeout.Infinite); }
                            return;
                        }
                        dirDue = false;
                        lastDir = Environment.TickCount;
                        Notify(SHCNE_UPDATEDIR, folder, null);
                        return;
                    }
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
                lock (gate)
                {
                    if (disposed) return;
                    disposed = true;
                    try { flush.Dispose(); } catch { }
                }
                try { if (fsw != null) { fsw.EnableRaisingEvents = false; fsw.Dispose(); } } catch { }
                fsw = null;
                try { if (notify != IntPtr.Zero) Native.UnregisterDeviceNotification(notify); } catch { }
                notify = IntPtr.Zero;
                if (dirHandle != new IntPtr(-1)) { Native.CloseHandle(dirHandle); dirHandle = new IntPtr(-1); }
            }
        }
    }
}
