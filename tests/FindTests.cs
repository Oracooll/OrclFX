// Tests for Find: name patterns, the background search, and the history of recent searches.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace OrclFileExplorer.Tests
{
    static class FindTests
    {
        [Test]
        static void PatternPartOfName()
        {
            FindPattern p = new FindPattern("report");
            Assert.True(p.Matches("Q3 Report.xlsx"), "contains, any case");
            Assert.True(p.Matches("report"), "the whole name");
            Assert.True(!p.Matches("repo.txt"), "only part of the word");
        }

        [Test]
        static void PatternWildcards()
        {
            FindPattern p = new FindPattern("*.pdf");
            Assert.True(p.Matches("Manual.PDF"), "extension, any case");
            Assert.True(!p.Matches("manual.pdf.txt"), "the whole name must match");
            FindPattern q = new FindPattern("report??.docx");
            Assert.True(q.Matches("report01.docx") && !q.Matches("report1.docx"), "? is exactly one character");
            Assert.True(new FindPattern("a+b (1).txt").Matches("a+b (1).txt"), "other characters are literal");
            Assert.True(new FindPattern("[x]*").Matches("[x] notes") && !new FindPattern("[x]*").Matches("x notes"), "brackets are literal");
        }

        [Test]
        static void PatternSeveral()
        {
            FindPattern p = new FindPattern(" *.pdf ; *.docx ;; budget ");
            Assert.True(p.Matches("a.pdf") && p.Matches("b.DOCX") && p.Matches("Budget 2026.xlsx"), "any of them");
            Assert.True(!p.Matches("c.xlsx"), "none of them");
            Assert.True(new FindPattern(" ; ").IsEmpty && new FindPattern(null).IsEmpty, "empty");
        }

        static FileSearch Run(string root, string text, int max, List<string> found)
        {
            FileSearch s = FileSearch.Start(root, text, delegate(FileSearch f, List<string> batch) { lock (found) found.AddRange(batch); }, max);
            Stopwatch w = Stopwatch.StartNew();
            while (!s.Finished && w.Elapsed.TotalSeconds < 20) Thread.Sleep(20);
            Assert.True(s.Finished, "the search finished within 20 seconds");
            return s;
        }

        static List<string> Names(string root, List<string> paths)
        {
            List<string> r = new List<string>();
            foreach (string p in paths) r.Add(p.Substring(root.Length + 1));
            r.Sort(StringComparer.OrdinalIgnoreCase);
            return r;
        }

        [Test]
        static void SearchesSubfoldersAndSkipsLinks()
        {
            using (TempDir d = new TempDir())
            {
                d.File("Report2024.txt", "x");
                d.File(@"sub\notes.txt", "x");
                d.File(@"sub\deeper\report-final.pdf", "x");
                d.File(@"sub\deeper\other.docx", "x");
                Directory.CreateDirectory(Path.Combine(d.Path, @"sub\Reports"));
                string link = Path.Combine(d.Path, @"sub\report-link");
                bool junction = SizeJobTests.MakeJunction(link, Path.Combine(d.Path, "sub"));
                try
                {
                    List<string> found = new List<string>();
                    FileSearch s = Run(d.Path, "report", FileSearch.MaxResults, found);
                    List<string> expect = new List<string> { "Report2024.txt", @"sub\deeper\report-final.pdf", @"sub\Reports" };
                    // The link itself matches by name, but what's behind it isn't searched (no loop, nothing twice).
                    if (junction) expect.Add(@"sub\report-link");
                    expect.Sort(StringComparer.OrdinalIgnoreCase);
                    Assert.Sequence(expect, Names(d.Path, found), "found");
                    Assert.Equal(expect.Count, s.Found, "count");
                    Assert.Equal(4, s.Folders, "folders searched (root, sub, deeper, Reports)");
                    Assert.Equal(0, s.Errors, "unreadable folders");
                    Assert.True(!s.Truncated, "not truncated");

                    found.Clear();
                    Run(d.Path, "*.pdf;*.docx", FileSearch.MaxResults, found);
                    Assert.Sequence(new string[] { @"sub\deeper\other.docx", @"sub\deeper\report-final.pdf" }, Names(d.Path, found), "patterns");
                }
                finally { if (junction) Directory.Delete(link); }
            }
        }

        [Test]
        static void SearchingADriveRoot()
        {
            // "C:\" must stay "C:\" (not "C:", which means the current folder on drive C).
            FileSearch s = FileSearch.Start(@"C:\", "no-such-name-xyz-123", null, 1);
            s.Cancel = true;
            Assert.Equal(@"C:\", s.Root, "root");
            Assert.Equal(@"D:\Data", FileSearch.Start(@"D:\Data\", "x", null, 1).Root, "a trailing backslash is dropped");
        }

        [Test]
        static void StopsAtTheLimit()
        {
            using (TempDir d = new TempDir())
            {
                for (int i = 0; i < 30; i++) d.File(@"f\match" + i + ".txt", "x");
                List<string> found = new List<string>();
                FileSearch s = Run(d.Path, "match", 10, found);
                Assert.Equal(10, s.Found, "found");
                Assert.Equal(10, found.Count, "reported");
                Assert.True(s.Truncated, "says it stopped at the limit");
            }
        }

        [Test]
        static void CanBeStopped()
        {
            using (TempDir d = new TempDir())
            {
                for (int i = 0; i < 200; i++) d.File(@"a" + (i % 20) + @"\f" + i + ".txt", "x");
                FileSearch s = FileSearch.Start(d.Path, "f", null);
                s.Cancel = true;
                Stopwatch w = Stopwatch.StartNew();
                while (!s.Finished && w.Elapsed.TotalSeconds < 10) Thread.Sleep(10);
                Assert.True(s.Finished, "a stopped search ends");
            }
        }

        [Test]
        static void MissingFolderIsReported()
        {
            using (TempDir d = new TempDir())
            {
                List<string> found = new List<string>();
                FileSearch s = Run(Path.Combine(d.Path, "gone"), "x", 100, found);
                Assert.Equal(0, s.Found, "found");
                Assert.True(s.Errors >= 1, "the unreadable folder is counted");
            }
        }

        [Test]
        static void History()
        {
            List<string> h = new List<string>();
            h = FindQuery.Remember(h, "report");
            h = FindQuery.Remember(h, "*.pdf");
            h = FindQuery.Remember(h, "REPORT ");
            Assert.Sequence(new string[] { "REPORT", "*.pdf" }, h, "newest first, once, whatever the case");
            for (int i = 0; i < 30; i++) h = FindQuery.Remember(h, "q" + i);
            Assert.Equal(FindQuery.HistorySize, h.Count, "kept");
            Assert.Equal("q29", h[0], "newest");
            Assert.Sequence(new string[] { "q29" }, FindQuery.Remember(new List<string> { "  " }, "q29"), "blank entries dropped");
        }
    }
}
