// Tests for the folder-size scan, run on a small folder tree built for the test.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace OrclFileExplorer.Tests
{
    static class SizeJobTests
    {
        static SizeJob Run(string root, long entryLimit)
        {
            SizeJob j = SizeJob.Start(root, null, entryLimit, 60);
            Stopwatch w = Stopwatch.StartNew();
            while (!j.Finished && w.Elapsed.TotalSeconds < 20) Thread.Sleep(20);
            Assert.True(j.Finished, "the scan finished within 20 seconds");
            return j;
        }

        static SizeEntry Find(List<SizeEntry> entries, string name)
        {
            foreach (SizeEntry e in entries) if (e.Name == name) return e;
            return null;
        }

        static bool MakeJunction(string link, string target)
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 && Directory.Exists(link);
            }
        }

        [Test]
        static void TotalsPerSubfolder()
        {
            using (TempDir d = new TempDir())
            {
                d.File(@"a\one.bin", new string('x', 100));
                d.File(@"a\deep\er\two.bin", new string('x', 200));
                d.File(@"b\three.bin", new string('x', 50));
                Directory.CreateDirectory(Path.Combine(d.Path, "empty"));
                d.File("top.bin", new string('x', 10));
                // A junction back into the tree must be skipped, or "a" would be counted twice.
                string link = Path.Combine(d.Path, "link-to-a");
                bool junction = MakeJunction(link, Path.Combine(d.Path, "a"));
                try
                {
                    SizeJob j = Run(d.Path, SizeJob.MaxEntries);
                    List<SizeEntry> s = j.Snapshot();
                    Assert.Equal(null, j.Failure, "failure");
                    Assert.Equal(0, j.Errors, "unreadable folders");
                    Assert.Equal(300L, Find(s, "a").Bytes, "a: bytes");
                    Assert.Equal(2L, Find(s, "a").Files, "a: files");
                    Assert.Equal(50L, Find(s, "b").Bytes, "b: bytes");
                    Assert.Equal(0L, Find(s, "empty").Bytes, "empty folder");
                    Assert.Equal(10L, Find(s, "(files in this folder)").Bytes, "files directly in the folder");
                    Assert.Equal(360L, j.TotalBytes, "total bytes");
                    Assert.Equal(4L, j.TotalFiles, "total files");
                    Assert.Equal("a", s[0].Name, "largest first");
                    if (junction) Assert.True(Find(s, "link-to-a") == null, "junction skipped");
                    else Console.WriteLine("        (couldn't create a junction here; that check was skipped)");
                    foreach (SizeEntry e in s) Assert.True(e.Done, e.Name + " done");
                }
                finally { if (junction) Directory.Delete(link); }
            }
        }

        [Test]
        static void NetworkCheck()
        {
            using (TempDir d = new TempDir())
            {
                Assert.True(!SizeJob.IsOnNetwork(d.Path), "a local folder");
                Assert.True(!SizeJob.IsOnNetwork(d.Path + "\\"), "with a trailing backslash");
                Assert.True(SizeJob.IsOnNetwork(@"\\server\share\folder"), "a network path");
                Assert.True(!SizeJob.IsOnNetwork(Path.Combine(d.Path, "missing")), "a missing folder is left to the scan");
                string target = Path.Combine(d.Path, "target");
                Directory.CreateDirectory(target);
                string link = Path.Combine(d.Path, "link");
                if (MakeJunction(link, target))
                    try { Assert.True(!SizeJob.IsOnNetwork(link), "a junction to a local folder"); }
                    finally { Directory.Delete(link); }
            }
        }

        [Test]
        static void StopsAtTheItemLimit()
        {
            using (TempDir d = new TempDir())
            {
                for (int i = 0; i < 20; i++) d.File(@"f\" + i + ".txt", "x");
                SizeJob j = Run(d.Path, 5);
                Assert.Equal("more than 5 items to scan", j.Failure, "failure");
            }
        }

        [Test]
        static void CountsMissingFolderAsUnreadable()
        {
            using (TempDir d = new TempDir())
            {
                SizeJob j = Run(Path.Combine(d.Path, "does-not-exist"), SizeJob.MaxEntries);
                Assert.Equal(0L, j.TotalBytes, "total");
                Assert.True(j.Errors >= 1 || j.Failure != null, "the problem is reported (errors " + j.Errors + ")");
            }
        }

        [Test]
        static void ReportsProgress()
        {
            using (TempDir d = new TempDir())
            {
                d.File(@"a\x.bin", "12345");
                int calls = 0;
                SizeJob j = SizeJob.Start(d.Path, delegate(SizeJob job) { Interlocked.Increment(ref calls); }, SizeJob.MaxEntries, 60);
                Stopwatch w = Stopwatch.StartNew();
                while (!j.Finished && w.Elapsed.TotalSeconds < 20) Thread.Sleep(20);
                Thread.Sleep(50);
                Assert.True(calls >= 2, "at least the first and last update, got " + calls);
            }
        }
    }
}
