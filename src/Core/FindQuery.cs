// Orcl File Explorer: the Find history (recent searches). No UI; covered by tests\.
using System;
using System.Collections.Generic;

namespace OrclFileExplorer
{
    static class FindQuery
    {
        public const int HistorySize = 15;

        // Puts text at the top of the history (once, whatever its case) and keeps the newest HistorySize entries.
        public static List<string> Remember(List<string> history, string text)
        {
            List<string> r = new List<string>();
            text = (text ?? "").Trim();
            if (text.Length > 0) r.Add(text);
            foreach (string h in history)
                if (r.Count < HistorySize && !string.Equals(h, text, StringComparison.OrdinalIgnoreCase) && h.Trim().Length > 0) r.Add(h);
            return r;
        }
    }
}
