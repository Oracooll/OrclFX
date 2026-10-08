// OrclFX: reading the settings file (key=value lines) and its per-pane tab entries. No UI; covered by tests\.
using System;
using System.IO;

namespace OrclFileExplorer
{
    static class SettingsFile
    {
        // The settings lines, or those of the .bak copy when the file is missing, unreadable or has no usable tab.
        public static string[] ReadLines(string file)
        {
            foreach (string f in new string[] { file, file + ".bak" })
            {
                try
                {
                    if (!File.Exists(f)) continue;
                    string[] lines = File.ReadAllLines(f);
                    foreach (string l in lines) if (IsUsableTabLine(l)) return lines;
                }
                catch { }
            }
            return null;
        }

        // A "pane<i>.tab=L|folder" line that would actually open a tab.
        public static bool IsUsableTabLine(string line)
        {
            int eq = line == null ? -1 : line.IndexOf('=');
            int pane; bool isTab, locked; string folder;
            return eq > 0 && TryParsePaneKey(line.Substring(0, eq), out pane, out isTab) && isTab &&
                TryParseTab(line.Substring(eq + 1), out locked, out folder) && folder.Trim().Length > 0;
        }

        // "pane<i>.tab" and "pane<i>.active" for panes 0-3.
        public static bool TryParsePaneKey(string key, out int pane, out bool isTab)
        {
            pane = -1;
            isTab = false;
            if (key == null || key.Length < 6 || !key.StartsWith("pane") || key[4] < '0' || key[4] > '3') return false;
            string rest = key.Substring(5);
            if (rest == ".tab") isTab = true;
            else if (rest != ".active") return false;
            pane = key[4] - '0';
            return true;
        }

        // "pane<i>.tabcolor": the colour label of the tab saved on the line before.
        public static bool TryParseTabColorKey(string key, out int pane)
        {
            pane = -1;
            if (key == null || key.Length != 14 || !key.StartsWith("pane") || !key.EndsWith(".tabcolor") || key[4] < '0' || key[4] > '3') return false;
            pane = key[4] - '0';
            return true;
        }

        // A tab is saved as "L|folder" (locked) or "U|folder".
        public static string FormatTab(bool locked, string folder) { return (locked ? "L|" : "U|") + folder; }

        public static bool TryParseTab(string value, out bool locked, out string folder)
        {
            locked = false;
            folder = null;
            if (value == null || value.Length <= 2 || value[1] != '|') return false;
            locked = value[0] == 'L';
            folder = value.Substring(2);
            return true;
        }
    }
}
