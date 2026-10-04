// Tests for reading the settings file: per-pane tab keys, tab entries and recovery from the .bak copy.
using System;
using System.IO;

namespace OrclFileExplorer.Tests
{
    static class SettingsFileTests
    {
        static string Key(string k)
        {
            int pane; bool isTab;
            if (!SettingsFile.TryParsePaneKey(k, out pane, out isTab)) return "-";
            return pane + (isTab ? " tab" : " active");
        }

        [Test]
        static void PaneKeys()
        {
            // Version 1.1.001 compared these keys with the wrong lengths and lost every tab.
            Assert.Equal("0 tab", Key("pane0.tab"), "pane0.tab");
            Assert.Equal("3 tab", Key("pane3.tab"), "pane3.tab");
            Assert.Equal("2 active", Key("pane2.active"), "pane2.active");
            foreach (string bad in new string[] { "pane4.tab", "pane0.tabs", "pane0.activeX", "paneX.tab", "panes", "paneweights", "pane", "", null })
                Assert.Equal("-", Key(bad), "'" + bad + "'");
        }

        [Test]
        static void Tabs()
        {
            bool locked; string folder;
            Assert.True(SettingsFile.TryParseTab(@"L|C:\Work", out locked, out folder) && locked && folder == @"C:\Work", "locked tab");
            Assert.True(SettingsFile.TryParseTab("U|::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", out locked, out folder) && !locked &&
                folder == "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "This PC");
            Assert.True(!SettingsFile.TryParseTab(@"C:\Work", out locked, out folder), "no prefix");
            Assert.True(!SettingsFile.TryParseTab("L|", out locked, out folder), "no folder");
            Assert.True(SettingsFile.TryParseTab(SettingsFile.FormatTab(true, @"D:\X|Y"), out locked, out folder) && locked && folder == @"D:\X|Y", "round trip");
        }

        [Test]
        static void ReadLines_UsesTheFileWhenItHasTabs()
        {
            using (TempDir d = new TempDir())
            {
                string f = d.File("state.txt", "panes=2\npane0.tab=U|C:\\A\n");
                d.File("state.txt.bak", "pane0.tab=U|C:\\Old\n");
                string[] lines = SettingsFile.ReadLines(f);
                Assert.True(lines != null && Array.IndexOf(lines, "pane0.tab=U|C:\\A") >= 0, "read the file itself");
            }
        }

        [Test]
        static void ReadLines_FallsBackToBak()
        {
            using (TempDir d = new TempDir())
            {
                string f = Path.Combine(d.Path, "state.txt");
                d.File("state.txt.bak", "pane1.tab=L|C:\\Saved\n");
                Assert.True(Array.IndexOf(SettingsFile.ReadLines(f) ?? new string[0], "pane1.tab=L|C:\\Saved") >= 0, "file missing");
                d.File("state.txt", "");
                Assert.True(Array.IndexOf(SettingsFile.ReadLines(f) ?? new string[0], "pane1.tab=L|C:\\Saved") >= 0, "file empty");
                d.File("state.txt", "panes=2\ntheme=1\n");
                Assert.True(Array.IndexOf(SettingsFile.ReadLines(f) ?? new string[0], "pane1.tab=L|C:\\Saved") >= 0, "file without tabs");
            }
        }

        [Test]
        static void ReadLines_DamagedFileDoesNotBeatAGoodBackup()
        {
            // The audit's case: a tab line with an unusable value used to win over a valid backup.
            using (TempDir d = new TempDir())
            {
                string f = d.File("state.txt", "panes=2\npane0.tab=garbage\npane1.tab=L|\n");
                d.File("state.txt.bak", "pane0.tab=U|C:\\Good\n");
                Assert.True(Array.IndexOf(SettingsFile.ReadLines(f) ?? new string[0], "pane0.tab=U|C:\\Good") >= 0, "the backup is used");
            }
        }

        [Test]
        static void UsableTabLines()
        {
            Assert.True(SettingsFile.IsUsableTabLine(@"pane0.tab=U|C:\A"), "normal");
            Assert.True(SettingsFile.IsUsableTabLine(@"pane3.tab=L|::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"), "locked This PC");
            foreach (string bad in new string[] { "pane0.tab=garbage", "pane0.tab=U|", "pane0.tab=U|   ", "pane0.active=1", "pane9.tab=U|C:\\A", "pane0.tab", "", null })
                Assert.True(!SettingsFile.IsUsableTabLine(bad), "'" + bad + "'");
        }

        [Test]
        static void ReadLines_NothingUsable()
        {
            using (TempDir d = new TempDir())
            {
                Assert.True(SettingsFile.ReadLines(Path.Combine(d.Path, "state.txt")) == null, "no files");
                string f = d.File("state.txt", "theme=1\n");
                d.File("state.txt.bak", "garbage");
                Assert.True(SettingsFile.ReadLines(f) == null, "no tabs anywhere");
            }
        }
    }
}
