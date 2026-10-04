// Orcl File Explorer: the shared shortcuts file (format, portable paths, three-way merge). No UI; covered by tests\.
using System;
using System.Collections.Generic;
using System.Text;

namespace OrclFileExplorer
{
    // A shortcut is a (label, folder path) pair. The file holds one "label|path" line per shortcut.
    static class ShortcutList
    {
        public const string Header = "# Orcl File Explorer shortcuts, one per line as: label, a vertical bar, then the folder. Shared between computers through OneDrive.";

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

        public static string Serialize(List<KeyValuePair<string, string>> entries)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (KeyValuePair<string, string> e in entries) sb.AppendLine(e.Key + "|" + ToPortable(e.Value));
            return sb.ToString();
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
