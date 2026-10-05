// Orcl File Explorer: where a tab the app opens goes in the tab row. No UI; covered by tests\.
using System;
using System.Collections.Generic;

namespace OrclFileExplorer
{
    static class TabOrder
    {
        // Position for a new tab opened from the active tab (Ctrl+T, a folder opened from a locked tab, Find
        // results, Duplicate ...): right after the active tab, but never in the middle of a group of locked
        // tabs. From a locked tab it goes after the last locked tab of that group. Tabs the user drags are
        // placed wherever they're dropped; this only applies to tabs the app opens.
        public static int InsertIndex(IList<bool> locked, int active)
        {
            if (active < 0 || active >= locked.Count) return locked.Count;
            int at = active + 1;
            if (locked[active]) while (at < locked.Count && locked[at]) at++;
            return at;
        }
    }
}
