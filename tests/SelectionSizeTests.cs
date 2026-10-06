// Tests for the size of the selection shown in the status bar.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OrclFileExplorer.Tests
{
    static class SelectionSizeTests
    {
        static SelectionSize Run(List<string> paths, long entryLimit = SelectionSize.MaxEntries)
        {
            SelectionSize s = SelectionSize.Start(paths, null, entryLimit, 30);
            for (int i = 0; i < 300 && !s.Finished; i++) Thread.Sleep(20);
            Assert.True(s.Finished, "finished");
            return s;
        }

        [Test]
        static void FilesAndFoldersWithTheirSubfolders()
        {
            using (TempDir d = new TempDir())
            {
                string a = d.File("a.txt", new string('x', 100));
                string sub = Path.Combine(d.Path, "Sub");
                Directory.CreateDirectory(Path.Combine(sub, "Deeper"));
                File.WriteAllText(Path.Combine(sub, "b.txt"), new string('y', 200));
                File.WriteAllText(Path.Combine(sub, "Deeper", "c.txt"), new string('z', 300));
                SelectionSize s = Run(new List<string> { a, sub });
                Assert.Equal(600L, s.Bytes, "bytes");
                Assert.Equal(3L, s.Files, "files");
                Assert.True(!s.Partial, "complete");
                Assert.Equal(Util.FormatBytes(600), s.Text, "text");
            }
        }

        [Test]
        static void MissingItemsAndLimitsGiveALowerBound()
        {
            using (TempDir d = new TempDir())
            {
                string a = d.File("a.txt", "12345");
                SelectionSize s = Run(new List<string> { a, Path.Combine(d.Path, "gone.txt") });
                Assert.Equal(5L, s.Bytes, "what could be counted");
                Assert.True(s.Partial && s.Text.StartsWith("at least"), "marked as a lower bound");
                for (int i = 0; i < 600; i++) d.File("f" + i + ".txt", "x");
                s = Run(new List<string> { d.Path }, 300);
                Assert.True(s.Partial, "stopped at the limit");
            }
        }

        [Test]
        static void AWholeDriveIsNotScanned()
        {
            SelectionSize s = Run(new List<string> { Path.GetPathRoot(Environment.SystemDirectory) });
            Assert.Equal(0L, s.Files, "nothing counted");
            Assert.True(s.Partial, "marked as not counted");
        }
    }
}
