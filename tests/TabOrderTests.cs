// Tests for where the app puts new tabs: never in the middle of a group of locked tabs.
using System;
using System.Collections.Generic;

namespace OrclFileExplorer.Tests
{
    static class TabOrderTests
    {
        // "LLU" = locked, locked, unlocked; returns the row after inserting N at the computed place.
        static string Insert(string row, int active)
        {
            List<bool> locked = new List<bool>();
            foreach (char c in row) locked.Add(c == 'L');
            int at = TabOrder.InsertIndex(locked, active);
            return row.Substring(0, at) + "N" + row.Substring(at);
        }

        [Test]
        static void FromALockedTabGoesAfterTheLockedGroup()
        {
            Assert.Equal("LLLNU", Insert("LLLU", 0), "from the first of three locked tabs");
            Assert.Equal("LLLNU", Insert("LLLU", 1), "from the middle one");
            Assert.Equal("LLLN", Insert("LLL", 2), "from the last one, at the end");
            Assert.Equal("LLNULL", Insert("LLULL", 0), "only that group: the next group stays where it is");
        }

        [Test]
        static void FromAnUnlockedTabGoesRightAfterIt()
        {
            Assert.Equal("UNLL", Insert("ULL", 0), "next to the unlocked tab, before a locked group");
            Assert.Equal("LUNU", Insert("LUU", 1), "between unlocked tabs");
            Assert.Equal("UN", Insert("U", 0), "single tab");
        }

        [Test]
        static void NoActiveTab()
        {
            Assert.Equal("LUN", Insert("LU", -1), "at the end");
            Assert.Equal("N", Insert("", -1), "empty row");
        }
    }
}
