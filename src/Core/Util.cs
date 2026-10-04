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

        // A short, stable key for a file path, usable in a mutex name.
        public static string PathKey(string path)
        {
            string p = Path.GetFullPath(path).ToLowerInvariant();
            using (System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(p));
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
