// Tests for saved layouts: the file format, portable paths, saving and deleting by name, damaged files.
using System;
using System.Collections.Generic;
using System.IO;

namespace OrclFileExplorer.Tests
{
    static class LayoutsTests
    {
        static Layout Make(string name)
        {
            Layout l = new Layout();
            l.Name = name;
            l.PaneCount = 2;
            l.Weights[0] = 1.5f; l.Weights[1] = 0.5f;
            l.ActivePane = 1;
            l.Tabs[0].Add(new TabSpec(@"C:\Work", true, 2));
            l.Tabs[0].Add(new TabSpec(@"D:\Data", false, 0));
            l.Tabs[1].Add(new TabSpec(@"C:\Photos", false, 6));
            l.ActiveTab[0] = 1;
            return l;
        }

        static string Show(Layout l)
        {
            List<string> s = new List<string>();
            s.Add(l.Name + " panes=" + l.PaneCount + " active=" + l.ActivePane + " w=" + l.Weights[0] + "," + l.Weights[1]);
            for (int i = 0; i < l.PaneCount; i++)
            {
                s.Add("pane" + i + " active=" + l.ActiveTab[i]);
                foreach (TabSpec t in l.Tabs[i]) s.Add((t.Locked ? "L " : "U ") + t.Folder + " c" + t.Color);
            }
            return string.Join(" | ", s.ToArray());
        }

        [Test]
        static void RoundTrip()
        {
            List<Layout> back = LayoutFile.Parse(LayoutFile.Serialize(new List<Layout> { Make("Work"), Make("Photos") }).Split('\n'));
            Assert.Equal(2, back.Count, "layouts");
            Assert.Equal(Show(Make("Work")), Show(back[0]), "first");
            Assert.Equal("Photos", back[1].Name, "second name");
        }

        [Test]
        static void PathsArePortable()
        {
            string old = Environment.GetEnvironmentVariable("OneDrive");
            try
            {
                Environment.SetEnvironmentVariable("OneDrive", @"C:\Users\Me\OneDrive");
                Layout l = Make("X");
                l.Tabs[0][0].Folder = @"C:\Users\Me\OneDrive\Docs";
                string text = LayoutFile.Serialize(new List<Layout> { l });
                Assert.True(text.Contains(@"pane0.tab=L|%OneDrive%\Docs"), "stored relative to OneDrive");
                Environment.SetEnvironmentVariable("OneDrive", @"E:\OneDrive");
                Assert.Equal(@"E:\OneDrive\Docs", LayoutFile.Parse(text.Split('\n'))[0].Tabs[0][0].Folder, "resolved on the other computer");
            }
            finally { Environment.SetEnvironmentVariable("OneDrive", old); }
        }

        [Test]
        static void UnusableLayoutsAndLinesAreSkipped()
        {
            List<Layout> r = LayoutFile.Parse(new string[] {
                "panes=3", "[Empty]", "panes=2", "[Ok]", "panes=1", "activepane=3", "pane0.tab=U|C:\\A", "pane0.tabcolor=99",
                "pane0.tabcolor=4", "pane9.tab=U|C:\\B", "garbage", "pane1.tab=U|C:\\NotShown" });
            Assert.Equal(1, r.Count, "layouts");
            Assert.Equal("Ok", r[0].Name, "name");
            Assert.Equal(0, r[0].ActivePane, "active pane is a shown one");
            Assert.Equal(4, r[0].Tabs[0][0].Color, "the last colour line counts");
            Assert.Equal(1, r[0].TabCount, "tabs in shown panes");
        }

        [Test]
        static void SaveReplacesByNameAndDeleteRemoves()
        {
            using (TempDir d = new TempDir())
            {
                string f = Path.Combine(d.Path, "layouts.txt");
                LayoutFile.Save(f, Make("Work"));
                LayoutFile.Save(f, Make("Photos"));
                Layout w2 = Make("WORK");
                w2.Tabs[1].Add(new TabSpec(@"C:\More", false, 0));
                LayoutFile.Save(f, w2);
                List<Layout> all = LayoutFile.Read(f);
                Assert.Equal(2, all.Count, "layouts after replacing one");
                Assert.Equal(4, LayoutFile.Find(all, "work").TabCount, "the replaced one");
                LayoutFile.Delete(f, "photos");
                Assert.Equal(1, LayoutFile.Read(f).Count, "after deleting");
            }
        }

        [Test]
        static void DamagedFileIsReadFromItsBackup()
        {
            using (TempDir d = new TempDir())
            {
                string f = Path.Combine(d.Path, "layouts.txt");
                LayoutFile.Save(f, Make("Work"));
                LayoutFile.Save(f, Make("Photos")); // the backup holds [Work]
                File.WriteAllText(f, "");
                Assert.Equal(1, LayoutFile.Read(f).Count, "layouts from the backup");
            }
        }

        [Test]
        static void Names()
        {
            Assert.Equal("Work 2", LayoutFile.CleanName("  [Work]\n2 "), "brackets and line breaks");
            Assert.Equal("", LayoutFile.CleanName("[]"), "nothing left");
        }

        [Test]
        static void TabColorKeys()
        {
            int p;
            Assert.True(SettingsFile.TryParseTabColorKey("pane2.tabcolor", out p) && p == 2, "pane2");
            Assert.True(!SettingsFile.TryParseTabColorKey("pane4.tabcolor", out p), "no pane 4");
            Assert.True(!SettingsFile.TryParseTabColorKey("pane0.tab", out p), "a tab line");
            Assert.True(!SettingsFile.IsUsableTabLine("pane0.tabcolor=3"), "a colour line isn't a tab");
        }
    }
}
