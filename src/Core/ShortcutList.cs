// OrclFX: the shared shortcuts file (format, portable paths, three-way merge). No UI; covered by tests\.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OrclFileExplorer
{
    // A shortcut is a (label, folder path) pair. The file holds one "label|path" line per shortcut.
    static class ShortcutList
    {
        public const string Header = "# OrclFX shortcuts, one per line as: label, a vertical bar, then the folder. Shared between computers through OneDrive.";

        // Store paths relative to OneDrive / the user profile so they resolve on every computer.
        public static string ToPortable(string path)
        {
            foreach (string v in new string[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial", "USERPROFILE" })
            {
                string root = Environment.GetEnvironmentVariable(v);
                if (string.IsNullOrEmpty(root)) continue;
                root = root.TrimEnd('\\');
                if (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                    return "%" + v + "%" + path.Substring(root.Length);
            }
            return path;
        }

        // Lines starting with "#" and without a "|" are comments, so a label like "#Archive" survives.
        public static List<KeyValuePair<string, string>> Parse(string[] lines)
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>();
            foreach (string line in lines)
            {
                int bar = line.IndexOf('|');
                if (bar <= 0) continue;
                r.Add(new KeyValuePair<string, string>(line.Substring(0, bar).Trim(),
                    Environment.ExpandEnvironmentVariables(line.Substring(bar + 1).Trim())));
            }
            return r;
        }

        // A list file always has its header line (Serialize writes it even for an empty list), so one with neither
        // a comment nor a shortcut was damaged (cut off by a crash, zeros from a sync glitch) and isn't trusted.
        public static bool IsIntact(string[] lines)
        {
            foreach (string l in lines) if (l.StartsWith("#") || l.IndexOf('|') > 0) return true;
            return false;
        }

        // The file's lines, or its backup's (file.bak, kept by every save) when the file is damaged.
        public static string[] ReadLines(string file)
        {
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            if (IsIntact(lines)) return lines;
            try
            {
                string bak = file + ".bak";
                if (File.Exists(bak))
                {
                    string[] b = File.ReadAllLines(bak, Encoding.UTF8);
                    if (IsIntact(b)) return b;
                }
            }
            catch { }
            return lines;
        }

        // Puts the good backup back in place of a damaged file, so the next save keeps it as the backup (the file is
        // never missing meanwhile, even if that save then fails).
        static void RestoreBackup(string file)
        {
            string bak = file + ".bak";
            if (File.Exists(bak) && IsIntact(File.ReadAllLines(bak, Encoding.UTF8))) File.Copy(bak, file, true);
        }

        public static string Serialize(List<KeyValuePair<string, string>> entries)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (KeyValuePair<string, string> e in entries) sb.AppendLine(e.Key + "|" + ToPortable(e.Value));
            return sb.ToString();
        }

        // Saves the local list as one locked read-merge-write, so two windows sharing the file can't both
        // merge the same old contents and then overwrite each other. Whatever is in the file now (another
        // window's or another computer's save) is merged with the local changes against baseList, the
        // version both started from. Returns the list that was written, which becomes the new base.
        public static List<KeyValuePair<string, string>> SaveMerged(string file, List<KeyValuePair<string, string>> baseList,
            List<KeyValuePair<string, string>> local)
        {
            return Util.WithFileLock(file, delegate
            {
                List<KeyValuePair<string, string>> merged = local;
                if (File.Exists(file))
                {
                    string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                    // A damaged file holds nothing to merge (reading it as "every shortcut was removed" would delete
                    // them all), and it mustn't become the backup.
                    if (IsIntact(lines)) merged = Merge(baseList, local, Parse(lines));
                    else RestoreBackup(file);
                }
                Util.WriteAllTextAtomic(file, Serialize(merged));
                return merged;
            });
        }

        // current plus the shortcuts of extra it doesn't have yet (same folder = same shortcut), added at the end.
        // Used when there is no common ancestor to merge against: nothing is lost, nothing is removed.
        public static List<KeyValuePair<string, string>> Union(List<KeyValuePair<string, string>> current, List<KeyValuePair<string, string>> extra)
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>(current);
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> e in current) have.Add(Key(e.Value));
            foreach (KeyValuePair<string, string> e in extra) if (have.Add(Key(e.Value))) r.Add(e);
            return r;
        }

        // The copies OneDrive makes when two computers changed the file before syncing: it keeps both, renaming
        // one to "shortcuts-<computer>.txt" (sometimes with a number) next to "shortcuts.txt".
        public static List<string> ConflictCopies(string file)
        {
            List<string> r = new List<string>();
            try
            {
                string dir = Path.GetDirectoryName(file), name = Path.GetFileNameWithoutExtension(file), ext = Path.GetExtension(file);
                if (!Directory.Exists(dir)) return r;
                foreach (string f in Directory.GetFiles(dir, name + "-*" + ext))
                    if (f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) r.Add(f);
                r.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return r;
        }

        // Brings another list into target, under target's lock. When lastMerged (what was taken from that list
        // the previous time) is known it's a three-way merge, so additions, renames and removals on either side
        // all carry over; otherwise a union, which never loses a shortcut.
        public static void MergeInto(string target, List<KeyValuePair<string, string>> source, List<KeyValuePair<string, string>> lastMerged)
        {
            Util.WithFileLock(target, delegate
            {
                List<KeyValuePair<string, string>> current = new List<KeyValuePair<string, string>>();
                if (File.Exists(target))
                {
                    current = Parse(ReadLines(target));
                    // Damaged (read from its backup instead): replaced without becoming the backup itself.
                    if (!IsIntact(File.ReadAllLines(target, Encoding.UTF8))) RestoreBackup(target);
                }
                List<KeyValuePair<string, string>> merged = lastMerged == null ? Union(current, source) : Merge(lastMerged, current, source);
                Util.WriteAllTextAtomic(target, Serialize(merged));
                return true;
            });
        }

        // Three-way merge of the shortcuts (label, path) against their common ancestor: additions on either
        // side are kept, a deletion on either side wins, a label changed here wins over the other side, and
        // the order comes from whichever side rearranged it.
        public static List<KeyValuePair<string, string>> Merge(List<KeyValuePair<string, string>> baseList,
            List<KeyValuePair<string, string>> local, List<KeyValuePair<string, string>> remote)
        {
            Dictionary<string, string> b = ToMap(baseList), l = ToMap(local), r = ToMap(remote);
            bool localReordered = !SameOrder(baseList, local, b, l);
            List<KeyValuePair<string, string>> first = localReordered ? local : remote, second = localReordered ? remote : local;
            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();
            HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (List<KeyValuePair<string, string>> src in new List<KeyValuePair<string, string>>[] { first, second })
                foreach (KeyValuePair<string, string> e in src)
                {
                    string p = Key(e.Value);
                    if (done.Contains(p)) continue;
                    bool inB = b.ContainsKey(p), inL = l.ContainsKey(p), inR = r.ContainsKey(p);
                    bool keep = inB ? (inL && inR) : (inL || inR);
                    if (!keep) continue;
                    done.Add(p);
                    string label = inL && (!inB || l[p] != b[p]) ? l[p] : inR ? r[p] : l[p];
                    result.Add(new KeyValuePair<string, string>(label, e.Value));
                }
            return result;
        }

        static string Key(string path) { return (path ?? "").TrimEnd('\\').ToLowerInvariant(); }

        static Dictionary<string, string> ToMap(List<KeyValuePair<string, string>> list)
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> e in list) m[Key(e.Value)] = e.Key;
            return m;
        }

        // Whether the entries both lists share appear in the same order.
        static bool SameOrder(List<KeyValuePair<string, string>> a, List<KeyValuePair<string, string>> b,
            Dictionary<string, string> aMap, Dictionary<string, string> bMap)
        {
            List<string> x = new List<string>(), y = new List<string>();
            foreach (KeyValuePair<string, string> e in a) if (bMap.ContainsKey(Key(e.Value))) x.Add(Key(e.Value));
            foreach (KeyValuePair<string, string> e in b) if (aMap.ContainsKey(Key(e.Value))) y.Add(Key(e.Value));
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++) if (x[i] != y[i]) return false;
            return true;
        }
    }
}
