// OrclFX: saved layouts (named sets of panes and tabs) and the shared file that holds them. No UI;
// covered by tests\.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OrclFileExplorer
{
    // One tab of a layout (or of the settings file): its folder, whether it's locked, and its colour label.
    class TabSpec
    {
        public string Folder;
        public bool Locked;
        public int Color;   // 0 = none, otherwise an index into Theme.TabColors (1-based)
        public TabSpec(string folder, bool locked, int color) { Folder = folder; Locked = locked; Color = color; }
    }

    // A named set of panes side by side, their widths and their tabs.
    class Layout
    {
        public string Name;
        public int PaneCount = 2;
        public float[] Weights = { 1, 1, 1, 1 };
        public int ActivePane;
        public readonly List<TabSpec>[] Tabs = { new List<TabSpec>(), new List<TabSpec>(), new List<TabSpec>(), new List<TabSpec>() };
        public readonly int[] ActiveTab = new int[4];

        public int TabCount
        {
            get { int n = 0; for (int i = 0; i < PaneCount; i++) n += Tabs[i].Count; return n; }
        }
    }

    static class LayoutFile
    {
        public const string Header = "# OrclFX layouts: a [name] line, then its panes and tabs. Shared between computers through OneDrive.";
        public const int MaxColor = 6;

        // "[name]" starts a layout; the lines after it use the settings file's keys (panes, paneweights, activepane,
        // pane<i>.active, pane<i>.tab, pane<i>.tabcolor). Unknown lines are skipped, so newer files still load.
        public static List<Layout> Parse(string[] lines)
        {
            List<Layout> r = new List<Layout>();
            Layout cur = null;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                {
                    cur = new Layout();
                    cur.Name = line.Substring(1, line.Length - 2).Trim();
                    r.Add(cur);
                    continue;
                }
                if (cur == null) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                int n, pi;
                bool isTab;
                switch (k)
                {
                    case "panes": if (int.TryParse(v, out n) && n >= 1 && n <= 4) cur.PaneCount = n; break;
                    case "activepane": if (int.TryParse(v, out n) && n >= 0 && n < 4) cur.ActivePane = n; break;
                    case "paneweights":
                        string[] ws = v.Split(',');
                        for (int i = 0; i < ws.Length && i < 4; i++)
                        {
                            float w;
                            if (float.TryParse(ws[i], NumberStyles.Float, CultureInfo.InvariantCulture, out w) && w > 0.05f && w < 20f) cur.Weights[i] = w;
                        }
                        break;
                    default:
                        if (SettingsFile.TryParseTabColorKey(k, out pi))
                        {
                            if (cur.Tabs[pi].Count > 0 && int.TryParse(v, out n)) cur.Tabs[pi][cur.Tabs[pi].Count - 1].Color = ClampColor(n);
                        }
                        else if (SettingsFile.TryParsePaneKey(k, out pi, out isTab))
                        {
                            bool locked; string folder;
                            if (!isTab) { if (int.TryParse(v, out n)) cur.ActiveTab[pi] = n; }
                            else if (SettingsFile.TryParseTab(v, out locked, out folder) && folder.Trim().Length > 0)
                                cur.Tabs[pi].Add(new TabSpec(Environment.ExpandEnvironmentVariables(folder.Trim()), locked, 0));
                        }
                        break;
                }
            }
            // A layout without a single tab in its panes is no use; the active pane must be a shown one.
            r.RemoveAll(delegate(Layout l) { return l.Name.Length == 0 || l.TabCount == 0; });
            foreach (Layout l in r) if (l.ActivePane >= l.PaneCount) l.ActivePane = 0;
            return r;
        }

        public static int ClampColor(int n) { return n >= 0 && n <= MaxColor ? n : 0; }

        public static string Serialize(List<Layout> layouts)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (Layout l in layouts)
            {
                sb.AppendLine();
                sb.AppendLine("[" + l.Name + "]");
                sb.AppendLine("panes=" + l.PaneCount);
                string[] w = new string[4];
                for (int i = 0; i < 4; i++) w[i] = l.Weights[i].ToString("0.####", CultureInfo.InvariantCulture);
                sb.AppendLine("paneweights=" + string.Join(",", w));
                sb.AppendLine("activepane=" + l.ActivePane);
                for (int i = 0; i < l.PaneCount; i++)
                {
                    sb.AppendLine("pane" + i + ".active=" + l.ActiveTab[i]);
                    foreach (TabSpec t in l.Tabs[i])
                    {
                        sb.AppendLine("pane" + i + ".tab=" + SettingsFile.FormatTab(t.Locked, ShortcutList.ToPortable(t.Folder)));
                        if (t.Color != 0) sb.AppendLine("pane" + i + ".tabcolor=" + t.Color);
                    }
                }
            }
            return sb.ToString();
        }

        // A name as it can be stored: one line, no brackets, not empty.
        public static string CleanName(string name)
        {
            string s = (name ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace("[", "").Replace("]", "").Trim();
            return s.Length > 60 ? s.Substring(0, 60).Trim() : s;
        }

        public static List<Layout> Read(string file)
        {
            if (!File.Exists(file)) return new List<Layout>();
            return Parse(ShortcutList.ReadLines(file)); // a damaged file is read from its backup
        }

        public static Layout Find(List<Layout> layouts, string name)
        {
            foreach (Layout l in layouts) if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        // Adds the layout, or replaces the one with its name, re-reading the file under its lock so another
        // computer's or window's layouts saved meanwhile are kept.
        public static void Save(string file, Layout layout)
        {
            Change(file, delegate(List<Layout> all)
            {
                int i = all.FindIndex(delegate(Layout l) { return string.Equals(l.Name, layout.Name, StringComparison.OrdinalIgnoreCase); });
                if (i >= 0) all[i] = layout; else all.Add(layout);
            });
        }

        public static void Delete(string file, string name)
        {
            Change(file, delegate(List<Layout> all) { all.RemoveAll(delegate(Layout l) { return string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase); }); });
        }

        static void Change(string file, Action<List<Layout>> change)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            Util.WithFileLock(file, delegate
            {
                List<Layout> all = Read(file);
                change(all);
                Util.WriteAllTextAtomic(file, Serialize(all));
                return true;
            });
        }
    }
}
