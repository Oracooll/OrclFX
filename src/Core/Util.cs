// Orcl File Explorer: path, file and formatting helpers with no UI (covered by tests\).
using System;
using System.IO;
using System.Text;

namespace OrclFileExplorer
{
    static class Util
    {
        // If path is oldRoot or inside it, returns the same path under newRoot; otherwise null.
        public static string Rebase(string path, string oldRoot, string newRoot)
        {
            if (path == null) return null;
            oldRoot = oldRoot.TrimEnd('\\');
            if (path.TrimEnd('\\').Equals(oldRoot, StringComparison.OrdinalIgnoreCase)) return newRoot;
            if (path.StartsWith(oldRoot + "\\", StringComparison.OrdinalIgnoreCase)) return newRoot.TrimEnd('\\') + path.Substring(oldRoot.Length);
            return null;
        }

        // A network share or mapped network drive. Decided from the path and the drive letter only, without
        // touching the network, so it's safe to call on the UI thread.
        public static bool IsNetworkPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith(@"\\")) return !path.StartsWith(@"\\?\") || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);
            return path.Length >= 2 && path[1] == ':' && Native.GetDriveType(path.Substring(0, 2) + @"\") == 4; // DRIVE_REMOTE
        }

        public static bool SameFolder(string a, string b)
        {
            return a != null && b != null && string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        // Runs body while holding the file's cross-process lock, one holder at a time across all windows.
        // The lock is re-entrant, so body may call WriteAllTextAtomic on the same file.
        public static T WithFileLock<T>(string path, Func<T> body)
        {
            using (System.Threading.Mutex m = new System.Threading.Mutex(false, "OrclFx.Write." + PathKey(path)))
            {
                bool owned = false;
                try { owned = m.WaitOne(5000); } catch (System.Threading.AbandonedMutexException) { owned = true; }
                if (!owned) throw new IOException("another program is writing " + Path.GetFileName(path));
                try { return body(); }
                finally { m.ReleaseMutex(); }
            }
        }

        // Writes to a temporary file first and then swaps it in, so a crash never leaves a half-written file.
        public static void WriteAllTextAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WithFileLock(path, delegate
            {
                // A unique temp name, so writers never share a temporary file.
                string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", true);
                    else File.Move(tmp, path);
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
                return true;
            });
        }

        // Removes the "downloaded from the internet" mark (the Zone.Identifier stream) that browsers attach. On the
        // installed program it makes Windows ask "The publisher could not be verified" on every start; the user
        // already chose to install it. True if the file has no mark afterwards.
        public static bool RemoveDownloadMark(string path)
        {
            string stream = path + ":Zone.Identifier";
            if (Native.DeleteFile(stream)) return true;
            int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            return err == 2 || err == 3; // there was no mark
        }

        // Puts a copy of source in place of target without ever leaving target broken: the copy is made next to
        // it as target.new and checked first, then swapped in in one step (the previous file stays as target.old
        // until DeleteOld). If anything fails, target is still the previous, working file.
        public static void ReplaceFileSafely(string source, string target)
        {
            string fresh = target + ".new", old = target + ".old";
            try { if (File.Exists(fresh)) File.Delete(fresh); } catch { }
            File.Copy(source, fresh, true);
            try
            {
                if (new FileInfo(fresh).Length != new FileInfo(source).Length) throw new IOException("the copy of " + Path.GetFileName(target) + " is incomplete");
                if (File.Exists(target))
                {
                    try { if (File.Exists(old)) File.Delete(old); } catch { }
                    try { File.Replace(fresh, target, old, true); }
                    catch
                    {
                        // File.Replace can fail after it has already moved target to target.old (for example when
                        // antivirus holds the new copy): put the previous file back before reporting the failure.
                        if (!File.Exists(target) && File.Exists(old)) try { File.Move(old, target); } catch { }
                        throw;
                    }
                }
                else File.Move(fresh, target);
            }
            finally { try { if (File.Exists(fresh)) File.Delete(fresh); } catch { } }
            DeleteOld(target);
        }

        // Removes target.old left by ReplaceFileSafely (it can still be in use right after an update).
        public static void DeleteOld(string target)
        {
            try { if (File.Exists(target + ".old")) File.Delete(target + ".old"); } catch { }
        }

        // A short, stable key for a file path, usable in a mutex name.
        public static string PathKey(string path) { return TextKey(Path.GetFullPath(path)); }

        // A short, stable key for any text, ignoring case.
        public static string TextKey(string text)
        {
            using (System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
                return BitConverter.ToString(h, 0, 10).Replace("-", "");
            }
        }

        public static string FormatBytes(long b)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = b;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return u == 0 ? b + " B" : v.ToString(v >= 100 ? "0" : "0.0") + " " + units[u];
        }

        // Disk sizes for the status bar: GB with one decimal, TB from 1000 GB.
        public static string FormatDiskSize(ulong b)
        {
            double gb = b / 1073741824.0;
            return gb >= 1000 ? (gb / 1024).ToString("0.0") + " TB" : gb.ToString("0.0") + " GB";
        }

        // 1.1.5.0 is shown as 1.1.005.
        public static string FormatVersion(Version v) { return v.Major + "." + v.Minor + "." + v.Build.ToString("000"); }
    }
}
