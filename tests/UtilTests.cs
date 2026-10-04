// Tests for the path, file and formatting helpers.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace OrclFileExplorer.Tests
{
    static class UtilTests
    {
        [Test]
        static void Rebase()
        {
            Assert.Equal(@"D:\New\Sub\File", Util.Rebase(@"C:\Old\Sub\File", @"C:\Old", @"D:\New"), "inside");
            Assert.Equal(@"D:\New", Util.Rebase(@"c:\old\", @"C:\Old", @"D:\New"), "the folder itself, other case");
            Assert.Equal(@"D:\New\X", Util.Rebase(@"C:\Old\X", @"C:\Old\", @"D:\New\"), "trailing backslashes");
            Assert.Equal(null, Util.Rebase(@"C:\Older\X", @"C:\Old", @"D:\New"), "a folder that only starts with the same letters");
            Assert.Equal(null, Util.Rebase(null, @"C:\Old", @"D:\New"), "no path");
        }

        [Test]
        static void SameFolder()
        {
            Assert.True(Util.SameFolder(@"C:\A\B", @"c:\a\b\"), "case and trailing backslash");
            Assert.True(!Util.SameFolder(@"C:\A\B", @"C:\A\BC"), "different folders");
            Assert.True(!Util.SameFolder(null, @"C:\A"), "no path");
        }

        [Test]
        static void PathKey()
        {
            string k = Util.PathKey(@"C:\Users\Me\state.txt");
            Assert.True(Regex.IsMatch(k, "^[0-9A-F]{20}$"), "20 hex characters, got " + k);
            Assert.Equal(k, Util.PathKey(@"c:\users\ME\STATE.TXT"), "same file in other case");
            Assert.Equal(k, Util.PathKey(@"C:\Users\Me\..\Me\state.txt"), "same file written another way");
            Assert.True(k != Util.PathKey(@"C:\Users\Me\state2.txt"), "different files differ");
        }

        [Test]
        static void FormatBytes()
        {
            Assert.Equal("0 B", Util.FormatBytes(0), "0");
            Assert.Equal("1023 B", Util.FormatBytes(1023), "1023");
            Assert.Equal("1.0 KB", Util.FormatBytes(1024), "1 KB");
            Assert.Equal("1.5 KB", Util.FormatBytes(1536), "1.5 KB");
            Assert.Equal("150 MB", Util.FormatBytes(150L << 20), "150 MB");
            Assert.Equal("2048 TB", Util.FormatBytes(2048L << 40), "largest unit is TB");
        }

        [Test]
        static void FormatDiskSize()
        {
            Assert.Equal("500.0 GB", Util.FormatDiskSize(500UL << 30), "GB");
            Assert.Equal("2.0 TB", Util.FormatDiskSize(2000UL << 30), "TB from 1000 GB");
        }

        [Test]
        static void Version()
        {
            Assert.Equal("1.1.005", Util.FormatVersion(new Version(1, 1, 5, 0)), "three-digit build");
            Assert.Equal("1.2.123", Util.FormatVersion(new Version(1, 2, 123, 0)), "1.2.123");
            Assert.True(Regex.IsMatch(Installer.Version, @"^\d+\.\d+\.\d{3}$"), "the app's own version, got " + Installer.Version);
        }

        [Test]
        static void WriteAllTextAtomic_KeepsABackup()
        {
            using (TempDir d = new TempDir())
            {
                string f = Path.Combine(d.Path, "sub", "state.txt");
                Util.WriteAllTextAtomic(f, "one");
                Assert.Equal("one", File.ReadAllText(f), "first write, folder created");
                Util.WriteAllTextAtomic(f, "two");
                Assert.Equal("two", File.ReadAllText(f), "second write");
                Assert.Equal("one", File.ReadAllText(f + ".bak"), "backup of the previous version");
                Assert.Equal(0, Directory.GetFiles(Path.GetDirectoryName(f), "*.tmp").Length, "temporary files left");
            }
        }

        [Test]
        static void WriteAllTextAtomic_ManyWritersAtOnce()
        {
            using (TempDir d = new TempDir())
            {
                string f = Path.Combine(d.Path, "shortcuts.txt");
                List<Exception> errors = new List<Exception>();
                List<Thread> threads = new List<Thread>();
                for (int t = 0; t < 4; t++)
                {
                    int id = t;
                    Thread th = new Thread(delegate()
                    {
                        for (int i = 0; i < 30; i++)
                            try { Util.WriteAllTextAtomic(f, "writer " + id + " line " + i + "\r\n"); }
                            catch (Exception e) { lock (errors) errors.Add(e); }
                    });
                    threads.Add(th);
                    th.Start();
                }
                foreach (Thread th in threads) th.Join();
                Assert.Equal(0, errors.Count, "failed writes" + (errors.Count > 0 ? " (" + errors[0].Message + ")" : ""));
                Assert.True(Regex.IsMatch(File.ReadAllText(f), @"^writer \d line \d+\r\n$"), "the file holds one complete write");
                Assert.Equal(0, Directory.GetFiles(d.Path, "*.tmp").Length, "temporary files left");
            }
        }
    }
}
